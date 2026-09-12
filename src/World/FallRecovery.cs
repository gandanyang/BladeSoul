using Godot;

namespace Oniblade.World;

/// <summary>
/// 「掉出世界」的判定——**纯逻辑，不依赖场景树**（AGENTS.md 铁律 8），所以它单测得了。
///
/// 规则只有两条：**低于阈值高度**，或**水平离原点太远**；
/// 但必须**连续 N 帧**都成立才算数（见 <see cref="FallRecoveryProfile"/> 里对原因的解释）。
///
/// 它只回答"该不该重置"，**不负责怎么重置**——那是 `FallGuard` 的事。
/// </summary>
public sealed class FallRecovery
{
    public float ThresholdY { get; set; } = -6f;
    public int ConfirmFrames { get; set; } = 12;
    public float MaxHorizontalDistance { get; set; } = 200f;

    /// <summary>已经连续越界多少帧（调试与断言用）。</summary>
    public int OutFrames { get; private set; }

    /// <summary>喂一帧位置。返回 true 表示**确认越界，该重置了**。</summary>
    public bool Update(float x, float y, float z)
    {
        bool outOfBounds = y < ThresholdY || new Vector2(x, z).Length() > MaxHorizontalDistance;

        if (!outOfBounds)
        {
            OutFrames = 0;
            return false;
        }

        OutFrames++;
        return OutFrames >= ConfirmFrames;
    }

    /// <summary>重置之后清掉计数，否则会一帧接一帧地重复触发。</summary>
    public void Clear() => OutFrames = 0;
}
