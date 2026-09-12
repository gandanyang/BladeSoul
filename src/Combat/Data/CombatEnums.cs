namespace Oniblade.Combat.Data;

/// <summary>伤害类型，用于抗性、音效选择与命中特效。</summary>
public enum DamageType
{
    Slash,
    Thrust,
    Blunt,
    Dark,
}

/// <summary>一闪的种类。除真一闪外，其余都由「获得 buff → 下一次攻击命中时升格为一闪」产生。</summary>
public enum IssenKind
{
    /// <summary>无 buff。</summary>
    None,
    /// <summary>真一闪：敌人在"命中前窗口"内按攻击键。</summary>
    Shin,
    /// <summary>弹一闪：弹开成功后按攻击。</summary>
    Deflect,
    /// <summary>避一闪：完美闪避后按攻击。</summary>
    Dodge,
    /// <summary>拼刀一闪：拼刀胜利后按攻击。</summary>
    Clash,
    /// <summary>连锁一闪：一闪命中后再按攻击。</summary>
    Chain,
}

/// <summary>敌人档次，决定一闪的收益（杂兵即死 / 精英重削体干 / BOSS 削体干+硬直）。</summary>
public enum EnemyTier
{
    Grunt,
    Elite,
    Boss,
}

/// <summary>敌人"危"攻击的三种形态，用不同音高与动作提示区分。</summary>
public enum PerilousKind
{
    None,
    /// <summary>危·突刺：高音，可弹开，可看破。</summary>
    Thrust,
    /// <summary>危·横扫：中音，不可格挡/弹开，必须闪避或跳跃。</summary>
    Sweep,
    /// <summary>危·抓取：低音，不可格挡/弹开，必须闪避。</summary>
    Grab,
}
