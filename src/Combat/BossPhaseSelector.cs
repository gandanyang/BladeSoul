using System.Collections.Generic;

namespace Oniblade.Combat;

/// <summary>
/// BOSS 阶段判定（T9 / 08 §3 P2-2）：**阶段是数据，不是散落在脚本里的 `if (health &lt; 0.5f)`**。
///
/// 为什么必须这样：05 有一条验收标准是"**P2 新增压力 ≤ P1 的 1.4 倍**"。
/// 如果阶段边界散在代码里，这条标准**没法执行**——没人能一眼看出阶段是从哪儿切的。
///
/// 阈值是**血量比例**（1.0 ＝ 满血），列表**从高到低**。
/// </summary>
public static class BossPhaseSelector
{
    /// <summary>返回当前阶段下标（0 起）。空列表返回 -1。</summary>
    public static int ResolvePhase(float healthRatio, IReadOnlyList<float> thresholds)
    {
        if (thresholds.Count == 0)
            return -1;

        // 阶段 i 从"血量跌到 thresholds[i] 以下"开始。所以答案是
        // **满足 `血量 ≤ thresholds[i]` 的最大下标**。
        // ⚠️ 第一版这里写的是"找到第一个 `血量 ≥ thresholds[i]` 就返回"——
        // 那会让 0.8（还高于 0.6 这道边界）被判成第 2 阶段。**单测当场抓出来了。**
        int phase = 0;
        for (int i = 0; i < thresholds.Count; i++)
        {
            if (healthRatio <= thresholds[i])
                phase = i;
        }

        return phase;
    }

    /// <summary>
    /// 阈值表是否**降序且合法**（第一个 ≤ 1.0，最后一个 &gt; 0，严格递减）。
    /// 自检与单测都靠它——写反的顺序会让阶段跳来跳去，而且很难看出来。
    /// </summary>
    public static bool IsDescending(IReadOnlyList<float> thresholds)
    {
        if (thresholds.Count == 0)
            return false;

        if (thresholds[0] > 1.0001f)
            return false;

        for (int i = 1; i < thresholds.Count; i++)
        {
            if (thresholds[i] >= thresholds[i - 1])
                return false;
        }

        return thresholds[thresholds.Count - 1] > 0f;
    }
}
