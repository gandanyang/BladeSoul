using System;
using Oniblade.Combat.Data;

namespace Oniblade.Combat;

/// <summary>
/// ★ 整个游戏的心脏。
///
/// 同一 physics frame 内，攻方判定框与防御方受击框重叠时，按下列顺序裁决，
/// **命中即返回，不再往下走**：
///   1. 无敌帧            → MISS
///   2. 一闪 buff + 可闪   → ISSEN
///   3. 双方都在攻击判定帧  → CLASH（拼刀）
///   4. 弹开窗            → DEFLECT
///   5. 格挡且夹角 ≤ 70°   → BLOCK（体干满则 GUARD_BREAK）
///   6. 以上都不满足        → HIT
///
/// 纯逻辑：不引用任何 Godot 节点 / 场景树 / 物理服务器，可在 xUnit 里直接单测。
/// 任何"顺手在这里加个特效"的念头都是错的——特效属于 CombatActor.ApplyVerdict()。
/// </summary>
public static class CombatResolver
{
    /// <summary>格挡有效半角（度）。超过这个夹角从背后/侧面打来，格挡无效。</summary>
    public const int GuardHalfAngleDeg = 70;

    private const int HitStopHit = 4;
    private const int HitStopBlock = 6;
    private const int HitStopDeflect = 8;
    private const int HitStopClash = 10;
    private const int HitStopGuardBreak = 10;
    private const int HitStopIssen = 14;
    private const int IssenSlowMoMs = 300;
    private const int DeflectPostureDamage = 18;
    private const int DeflectIssenBuffFrames = 10;
    private const int ChainIssenBuffFrames = 12;

    public static ResolveResult Resolve(in AttackerSnapshot atk, in DefenderSnapshot def)
    {
        // 1. 闪避无敌 → 完全无效。优先级最高，连一闪都打不中。
        if (def.IsInvulnerable)
            return new ResolveResult { Verdict = Verdict.Miss };

        // 2. 一闪：防御方持有 buff，且攻方本帧可被一闪。
        if (def.IssenKind != IssenKind.None && atk.IssenVulnerable)
            return IssenResult(def.IssenKind);

        // 3. 拼刀：**双方都在判定帧**（刀对刀撞上了）。
        //    注意：这里要求防御方也在判定帧，而不是"在出招"——
        //    否则砍一个正在挥空后摇的敌人也会变成拼刀，玩家会觉得莫名其妙。
        if (atk.IsActive && def.IsActive)
            return new ResolveResult
            {
                Verdict = Verdict.Clash,
                HitStopFrames = HitStopClash,
                // 拼刀胜利后才授予 buff，这里不授予。
            };

        // 4. 弹开：进入防御后的窗口内被命中，且该招可弹。
        if (def.InDeflectWindow && atk.Traits.Parryable)
            return new ResolveResult
            {
                Verdict = Verdict.Deflect,
                PostureDamage = DeflectPostureDamage,
                HitStopFrames = HitStopDeflect,
                GrantIssen = IssenKind.Deflect,
                GrantIssenFrames = DeflectIssenBuffFrames,
            };

        // 5. 格挡：正面 ±70°，且该招不是「危」。
        if (def.IsGuarding
            && !atk.Traits.Unblockable
            && Math.Abs(def.GuardAngleDeg) <= GuardHalfAngleDeg)
        {
            if (def.MaxPosture > 0 && def.CurrentPosture >= def.MaxPosture)
                return new ResolveResult { Verdict = Verdict.GuardBreak, HitStopFrames = HitStopGuardBreak };

            return new ResolveResult
            {
                Verdict = Verdict.Block,
                PostureDamage = atk.Traits.PostureDamage,
                HitStopFrames = HitStopBlock,
            };
        }

        // 6. 命中。
        return new ResolveResult
        {
            Verdict = Verdict.Hit,
            Damage = atk.Traits.Damage,
            PostureDamage = atk.Traits.PostureDamage,
            HitStopFrames = HitStopHit,
        };
    }

    private static ResolveResult IssenResult(IssenKind kind) => new()
    {
        Verdict = Verdict.Issen,
        HitStopFrames = HitStopIssen,
        SlowMoMs = IssenSlowMoMs,
        // 一闪的收益按敌人档次百分比结算，见 IssenTable。
        GrantIssen = kind == IssenKind.Shin ? IssenKind.None : IssenKind.Chain,
        GrantIssenFrames = kind == IssenKind.Shin ? 0 : ChainIssenBuffFrames,
    };
}
