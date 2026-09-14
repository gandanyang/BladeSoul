using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;

namespace Oniblade.Core;

/// <summary>
/// 战斗仲裁器：每 physics 帧只做一件事——
/// **收集所有重叠 → 排序 → 逐个交给裁决器 → 把结果落地**。
///
/// 排序保证同帧结果可复现（`ActorId` 是稳定的）：
/// 否则"同帧互击谁先结算"会随节点顺序变化，调试时会出现"重开一次结果就不一样"。
///
/// 它比所有战斗单位**晚**运行（`ProcessPhysicsPriority = 100`），
/// 这样收集到的一定是本单位这一帧刚更新过的判定状态，而不是上一帧的。
/// </summary>
public partial class CombatArbiter : Node
{
    public static CombatArbiter? Instance { get; private set; }

    private readonly List<Hitbox> _hitboxes = new(32);
    private readonly List<Hurtbox> _scratch = new(8);
    private readonly List<Pending> _pending = new(32);

    private readonly struct Pending
    {
        public Pending(Hitbox attacker, Hurtbox defender)
        {
            Attacker = attacker;
            Defender = defender;
        }

        public Hitbox Attacker { get; }
        public Hurtbox Defender { get; }
    }

    public override void _EnterTree()
    {
        Instance = this;
        AddToGroup("combat_arbiter");

        // 晚于所有战斗单位运行。
        ProcessPhysicsPriority = 100;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Register(Hitbox hitbox)
    {
        if (!_hitboxes.Contains(hitbox))
            _hitboxes.Add(hitbox);
    }

    public void Unregister(Hitbox hitbox) => _hitboxes.Remove(hitbox);

    public override void _PhysicsProcess(double delta)
    {
        _pending.Clear();

        // 1) 收集本帧所有重叠
        foreach (Hitbox hitbox in _hitboxes)
        {
            if (!hitbox.IsActiveThisFrame)
                continue;

            CombatActor attacker = hitbox.Actor;
            if (attacker is null || attacker.IsDead || !attacker.CanDealDamage)
                continue;

            int count = hitbox.Query(_scratch);
            for (int i = 0; i < count; i++)
            {
                Hurtbox hurt = _scratch[i];
                if (hurt.OwnerActor is null || hurt.OwnerActor.IsDead)
                    continue;
                if (hitbox.AlreadyHit(hurt.OwnerActor))
                    continue;

                _pending.Add(new Pending(hitbox, hurt));
            }
        }

        if (_pending.Count == 0)
            return;

        // 2) 排序：先按攻击方 ActorId，再按受击方 ActorId → 同帧结果可复现
        _pending.Sort(static (a, b) =>
        {
            int byAttacker = a.Attacker.Actor.ActorId.CompareTo(b.Attacker.Actor.ActorId);
            return byAttacker != 0
                ? byAttacker
                : a.Defender.OwnerActor.ActorId.CompareTo(b.Defender.OwnerActor.ActorId);
        });

        // 3) 逐个裁决并落地
        foreach (Pending pending in _pending)
            ResolveOne(pending);
    }

    private static void ResolveOne(in Pending pending)
    {
        Hitbox hitbox = pending.Attacker;
        Hurtbox hurtbox = pending.Defender;

        CombatActor attacker = hitbox.Actor;
        CombatActor defender = hurtbox.OwnerActor;

        // 同一帧里可能已经被别的攻击打死了。
        if (attacker.IsDead || defender.IsDead || !attacker.CanDealDamage)
            return;

        AttackData? attack = hitbox.Data;

        var attackerSnapshot = new AttackerSnapshot
        {
            ActorId = attacker.ActorId,
            Traits = attack?.ToTraits() ?? AttackTraits.Neutral,
            IsActive = hitbox.IsActiveThisFrame,
            IsAttackAction = attacker.IsAttackActiveNow,
            IssenVulnerable = attack?.IssenVulnerable ?? true,
        };

        DefenderSnapshot defenderSnapshot = defender.GetDefenderSnapshot(attacker.GlobalPosition);

        ResolveResult result = CombatResolver.Resolve(attackerSnapshot, defenderSnapshot);

        if (result.Verdict == Verdict.Miss)
        {
            // 完美闪避的入口（T13 规则 3）：攻击被**完全躲开**时通知防御方。
            // Miss 不走 CombatActor.ReceiveVerdict（它连顿帧都不给，否则玩家会以为打中了），
            // 所以这里是"我躲开了"这件事**唯一**的可观测点。
            if (defender is IAttackEvasionListener listener)
                listener.OnAttackEvaded(attacker);

            return;
        }

        hitbox.MarkHit(defender);

        IssenKind usedIssen = defenderSnapshot.IssenKind;

        defender.ReceiveVerdict(result, attacker, attack, usedIssen);

        Vector3 toDefender = defender.GlobalPosition - attacker.GlobalPosition;
        toDefender.Y = 0f;

        EventBus.Instance?.RaiseHitResolved(new HitEvent
        {
            AttackerId = attacker.ActorId,
            DefenderId = defender.ActorId,
            Verdict = result.Verdict,
            // T53：把"这一刀是什么性质"一起带给反馈层——特效与音频按它分档
            // （斩=金属、打=沉闷、突=尖锐、暗=闷响）。没有攻击数据的结算落到斩击档。
            AttackType = attack?.Type ?? DamageType.Slash,
            AttackId = attack?.Id ?? string.Empty,
            IssenKind = usedIssen,
            Damage = result.Verdict == Verdict.Hit ? result.Damage : 0,
            PostureDamage = result.PostureDamage,
            HitStopFrames = result.HitStopFrames,
            Frame = (int)Engine.GetPhysicsFrames(),
            Killed = defender.IsDead,
            // 特效层要"打在哪、顺着哪个方向"（T28）。
            // 接触点取受击体积（CollisionShape3D 子节点）的世界位置——胸口高度。
            // ⚠️ 不能用 hurtbox.GlobalPosition：Hurtbox 节点本身通常没有 transform，
            //    位置＝角色根（脚底），火花会贴地（D1，2026-09-15 修）。
            Position = hurtbox.GlobalContactPoint,
            Direction = toDefender.LengthSquared() > 0.0001f
                ? toDefender.Normalized()
                : -defender.GlobalTransform.Basis.Z,
        });
    }
}
