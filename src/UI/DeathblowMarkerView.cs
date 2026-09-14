namespace Oniblade.UI;

/// <summary>
/// 处决标记的**显示判定**（T52）——不依赖场景树的纯逻辑，可被单测直接验。
///
/// # 为什么单独拆出来
/// 处决窗口只有 2 秒，而"该不该亮标记"如果判错，症状是
/// **"我明明看它跪下了，按 F 却没反应"**（标记亮着但窗口已关），
/// 或者反过来"标记没亮但其实能处决"（玩家根本不知道有这个机制）。
/// 两种都很难在实机里定位，所以把判定写成纯函数，把每条边界钉死在单测里。
///
/// 拆出来的第二个理由：UI 层不许引用 <c>CombatActor</c>/<c>Ashigaru</c> 具体类型
/// （docs/00 §2.8），所以它只吃 <c>bool</c> 和距离这些原始数据。
/// </summary>
public static class DeathblowMarkerView
{
    /// <summary>
    /// 该不该给这个目标亮处决标记。
    /// </summary>
    /// <param name="canBeExecuted">目标此刻可被处决（来自只读接口的 <c>CanBeExecuted</c>）。</param>
    /// <param name="distance">玩家到目标的水平距离（米）。</param>
    /// <param name="maxDistance">处决距离上限（米，与 <c>DeathblowProfile.MaxDistance</c> 同源）。</param>
    /// <param name="markerEnabled">标记功能的总开关。</param>
    /// <remarks>
    /// ★ 距离判定**必须**做，而且必须与处决判定同源。
    /// 只判 <paramref name="canBeExecuted"/> 的话，远处一个刚被破韧的敌人也会亮标记，
    /// 玩家跑过去按 F —— 而那时窗口早关了。标记就成了骗人的。
    /// </remarks>
    public static bool ShouldShow(
        bool canBeExecuted,
        float distance,
        float maxDistance,
        bool markerEnabled = true)
    {
        if (!markerEnabled || !canBeExecuted)
            return false;

        if (maxDistance <= 0f)
            return false;

        return distance <= maxDistance;
    }

    /// <summary>
    /// 标记的紧张度 0~1（1 = 窗口马上要关）。UI 用它做闪烁节奏。
    ///
    /// 用**剩余比例**而不是绝对帧数：将来精英怪窗口更短（比如 60 帧），
    /// 按绝对帧数算的话它会一直闪，反而看不出"要没了"。
    /// </summary>
    public static float Urgency(int framesLeft, int totalFrames)
    {
        if (totalFrames <= 0)
            return 0f;

        float remaining = framesLeft / (float)totalFrames;

        if (remaining >= 1f)
            return 0f;

        return remaining <= 0f ? 1f : 1f - remaining;
    }
}
