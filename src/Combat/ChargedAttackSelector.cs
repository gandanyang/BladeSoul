namespace Oniblade.Combat;

/// <summary>
/// 「按住攻击键多久 → 该打出哪一段蓄力斩」——**纯逻辑，不依赖引擎**，
/// 所以它能进 xUnit（AGENTS.md 铁律 8）。T46。
///
/// **三段阈值就是各段自己的 `StartupFrames`**（02 §2.1：34 / 48 / 62）——
/// 不再另造一套数字（"每个数字只能有一个来源"）。
/// 因为蓄力的那几十帧**就是这一招的前摇**，所以 `ChargedAttackState` 里的前摇记 0。
///
/// 返回 0 ＝ **没到第一段阈值**——玩家只是点了一下，该走普通轻攻击。
/// </summary>
public static class ChargedAttackSelector
{
    public static int ResolveLevel(int holdFrames, int threshold1, int threshold2, int threshold3)
    {
        if (holdFrames >= threshold3)
            return 3;

        if (holdFrames >= threshold2)
            return 2;

        if (holdFrames >= threshold1)
            return 1;

        return 0;
    }
}
