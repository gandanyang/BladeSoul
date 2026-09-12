namespace Oniblade.Combat;

/// <summary>
/// 单次攻防接触的裁决结论。裁决器只回答"发生了什么"，不负责扣血、放特效。
/// 顺序即优先级（见 CombatResolver），不要随意调整成员顺序。
/// </summary>
public enum Verdict
{
    /// <summary>完全无效（防御方处于闪避无敌帧）。</summary>
    Miss,
    Hit,
    Block,
    /// <summary>格挡成立但体干已满 → 破防大硬直。</summary>
    GuardBreak,
    /// <summary>弹开：零体干消耗，削敌体干，并授予"弹一闪"。</summary>
    Deflect,
    /// <summary>拼刀：双方招式同帧相撞。</summary>
    Clash,
    Issen,
    Deathblow,
}
