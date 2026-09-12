using Oniblade.Combat;
using Oniblade.Combat.Data;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 02 文档 §4 的六条规则，一条都不能错。
/// 这里的测试**不接触任何 Godot 类型**，所以能脱离引擎跑。
/// </summary>
public class CombatResolverTests
{
    private static AttackerSnapshot Attacker(
        int damage = 20,
        int postureDamage = 10,
        AttackTraits? traits = null,
        bool isActive = true,
        bool isAttackAction = false,
        bool issenVulnerable = true) => new()
        {
            ActorId = 1,
            Traits = traits ?? new AttackTraits
            {
                Damage = damage,
                PostureDamage = postureDamage,
                Parryable = true,
            },
            IsActive = isActive,
            IsAttackAction = isAttackAction,
            IssenVulnerable = issenVulnerable,
        };

    private static DefenderSnapshot Defender(
        bool invulnerable = false,
        bool isActive = false,
        bool inDeflectWindow = false,
        bool guarding = false,
        int guardAngleDeg = 0,
        int posture = 0,
        int maxPosture = 100,
        IssenKind issen = IssenKind.None) => new()
        {
            ActorId = 2,
            IsInvulnerable = invulnerable,
            IsActive = isActive,
            InDeflectWindow = inDeflectWindow,
            IsGuarding = guarding,
            GuardAngleDeg = guardAngleDeg,
            CurrentPosture = posture,
            MaxPosture = maxPosture,
            IssenKind = issen,
        };

    // ── 规则 1：无敌帧 ───────────────────────────────────────────

    [Fact]
    public void Rule1_Invulnerable_Always_Misses()
    {
        var atk = Attacker(isActive: true, isAttackAction: true);
        var def = Defender(invulnerable: true, guarding: true);

        Assert.Equal(Verdict.Miss, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule1_Invulnerable_Beats_Even_Issen()
    {
        var atk = Attacker(isActive: true, issenVulnerable: true);
        var def = Defender(invulnerable: true, issen: IssenKind.Shin);

        Assert.Equal(Verdict.Miss, CombatResolver.Resolve(atk, def).Verdict);
    }

    // ── 规则 2：一闪 ─────────────────────────────────────────────

    [Fact]
    public void Rule2_Issen_Beats_Clash()
    {
        var atk = Attacker(isActive: true, isAttackAction: true, issenVulnerable: true);
        var def = Defender(issen: IssenKind.Deflect);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Issen, r.Verdict);
        Assert.Equal(14, r.HitStopFrames);
    }

    [Fact]
    public void Rule2_Issen_Falls_Through_When_Attacker_Not_Vulnerable()
    {
        // 敌人处于"霸体不可闪"的招式时，一闪 buff 不应该凭空生效。
        var atk = Attacker(isActive: true, issenVulnerable: false);
        var def = Defender(guarding: true, issen: IssenKind.Shin);

        Assert.Equal(Verdict.Block, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule2_Shin_Issen_Does_Not_Grant_Chain_Buff()
    {
        var atk = Attacker(issenVulnerable: true);
        var def = Defender(issen: IssenKind.Shin);

        Assert.Equal(IssenKind.None, CombatResolver.Resolve(atk, def).GrantIssen);
    }

    [Fact]
    public void Rule2_Deflect_Issen_Grants_Chain_Buff()
    {
        var atk = Attacker(issenVulnerable: true);
        var def = Defender(issen: IssenKind.Deflect);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(IssenKind.Chain, r.GrantIssen);
        Assert.Equal(12, r.GrantIssenFrames);
    }

    // ── 规则 3：拼刀 ─────────────────────────────────────────────

    [Fact]
    public void Rule3_Clash_Beats_Deflect_Window()
    {
        var atk = Attacker(isActive: true, isAttackAction: true);
        var def = Defender(isActive: true, inDeflectWindow: true, guarding: true);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Clash, r.Verdict);
        Assert.Equal(10, r.HitStopFrames);
    }

    [Fact]
    public void Rule3_Clash_Requires_Attacker_To_Be_Active()
    {
        // 双方都在出招但攻方还在前摇 → 不算拼刀。
        var atk = Attacker(isActive: false, isAttackAction: true);
        var def = Defender(isActive: true);

        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule3_Clash_Requires_Both_Sides_In_Active_Frames()
    {
        // 敌人正在收招后摇时被我砍中 → 就是普通命中，不是拼刀。
        var atk = Attacker(isActive: true, isAttackAction: true);
        var def = Defender(isActive: false);

        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(atk, def).Verdict);
    }

    // ── 规则 4：弹开 ─────────────────────────────────────────────

    [Fact]
    public void Rule4_Deflect_Zeroes_Posture_And_Grants_Buff()
    {
        var atk = Attacker(postureDamage: 40);
        var def = Defender(inDeflectWindow: true, guarding: true);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Deflect, r.Verdict);
        Assert.Equal(18, r.PostureDamage);          // 弹开的削敌体干是固定值，不随被弹招式变化
        Assert.Equal(IssenKind.Deflect, r.GrantIssen);
        Assert.Equal(10, r.GrantIssenFrames);
    }

    [Fact]
    public void Rule4_Deflect_Window_Does_Not_Save_You_From_Unparryable()
    {
        var atk = Attacker(traits: AttackTraits.Perilous);
        var def = Defender(inDeflectWindow: true, guarding: true);

        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(atk, def).Verdict);
    }

    // ── 规则 5：格挡 ─────────────────────────────────────────────

    [Fact]
    public void Rule5_Block_Takes_Posture_Damage_But_No_Health_Damage()
    {
        var atk = Attacker(damage: 20, postureDamage: 12);
        var def = Defender(guarding: true);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Block, r.Verdict);
        Assert.Equal(0, r.Damage);
        Assert.Equal(12, r.PostureDamage);
    }

    [Fact]
    public void Rule5_Block_Fails_Outside_Guard_Angle()
    {
        var atk = Attacker();
        var def = Defender(guarding: true, guardAngleDeg: CombatResolver.GuardHalfAngleDeg + 1);

        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule5_Block_Works_At_Exactly_Guard_Angle()
    {
        var atk = Attacker();
        var def = Defender(guarding: true, guardAngleDeg: CombatResolver.GuardHalfAngleDeg);

        Assert.Equal(Verdict.Block, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule5_Full_Posture_Becomes_GuardBreak()
    {
        var atk = Attacker();
        var def = Defender(guarding: true, posture: 100, maxPosture: 100);

        Assert.Equal(Verdict.GuardBreak, CombatResolver.Resolve(atk, def).Verdict);
    }

    [Fact]
    public void Rule5_Unblockable_Ignores_Guard()
    {
        var atk = Attacker(traits: AttackTraits.Perilous);
        var def = Defender(guarding: true, guardAngleDeg: 0);

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Hit, r.Verdict);
    }

    // ── 规则 6：命中 ─────────────────────────────────────────────

    [Fact]
    public void Rule6_Hit_Applies_Full_Damage_And_Posture()
    {
        var atk = Attacker(damage: 25, postureDamage: 9);
        var def = Defender();

        var r = CombatResolver.Resolve(atk, def);

        Assert.Equal(Verdict.Hit, r.Verdict);
        Assert.Equal(25, r.Damage);
        Assert.Equal(9, r.PostureDamage);
        Assert.Equal(4, r.HitStopFrames);
    }

    [Fact]
    public void Neutral_Traits_Are_Blockable_And_Parryable()
    {
        Assert.True(AttackTraits.Neutral.Parryable);
        Assert.False(AttackTraits.Neutral.Unblockable);
        Assert.False(AttackTraits.Perilous.Parryable);
        Assert.True(AttackTraits.Perilous.Unblockable);
    }
}
