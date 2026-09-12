namespace Oniblade.Combat;

/// <summary>
/// 「松开防御后又马上按下」的判定（纯逻辑，可单测）。
///
/// 这条规则补的是 02 §8 原有的「连打防御惩罚」——原实现只覆盖了
/// "从攻击/受击**取消**进入防御"，从站立反复点按防御时每次都会重新开一次窗，
/// 于是连打可以零时机地拿到弹开收益。
///
/// **能解决什么、不能解决什么（别对它期待过高）：**
///
/// - 能解决：**高频连点**。按 2 帧松 2 帧这种循环，第二次之后的每次按下都会被判成
///   取消进入（付 `GuardCancelLockFrames` 硬直），而玩家往往在窗口打开前就松手了，
///   结果是**窗口根本开不出来**。
/// - 不能解决：**慢速连点**（每次按住 ≥5 帧、间隔 ≥锁定期）。那样窗口照样会开，
///   因为窗口一旦打开就存在 9 帧，这与"看准时机的按"在机器眼里没有区别。
///
/// 所以**对抗连打的主要机制不是这个锁，而是「危」攻击**（02 §3 裁定：弹开只应对一般攻击）。
/// 连打防御的玩家能活，但打不动；遇上危攻击照样挨打。这与 01 文档
/// "惩罚只惩罚效率，不惩罚存活"的立场一致——**我们本来就不打算靠惩罚按键来防连打**。
/// </summary>
public static class GuardReentry
{
    /// <summary>
    /// 这次按下算不算"快速重按"。
    /// </summary>
    /// <param name="currentFrame">当前本地帧号。</param>
    /// <param name="lastGuardReleaseFrame">上一次松开防御的帧号；从未松过为 -1。</param>
    /// <param name="reentryLockFrames">惩罚窗口（`DifficultyProfile.GuardReentryLockFrames`）。</param>
    public static bool IsQuickReentry(int currentFrame, int lastGuardReleaseFrame, int reentryLockFrames)
    {
        if (lastGuardReleaseFrame < 0 || reentryLockFrames <= 0)
            return false;   // 第一次按下，或者这一档不启用惩罚

        return currentFrame - lastGuardReleaseFrame < reentryLockFrames;
    }
}
