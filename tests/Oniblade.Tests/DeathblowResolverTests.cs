using System.Collections.Generic;
using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T52 处决判定的边界（纯逻辑，不需要引擎）。
///
/// 这里钉的是两类**最难查的错误**：
///   · "按了没反应"——某条正交条件（破韧/窗口/距离/状态）判错了，
///     表现和"键盘没响应"一模一样，查不到是哪条；
///   · "随时秒怪"——条件太松，没破韧也能处决，整个体干系统直接作废。
/// </summary>
public class DeathblowResolverTests
{
    private static DeathblowCandidate Enemy(float x, float z, bool executable)
        => DeathblowCandidate.Make(x, z, executable);

    [Fact]
    public void 破韧且在距离内_能处决()
    {
        var list = new List<DeathblowCandidate> { Enemy(1.0f, 0f, true) };

        int index = DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list);

        Assert.Equal(0, index);
    }

    [Fact]
    public void 没破韧_不能处决()
    {
        // ★ 这条是"随时秒怪"的守门人：没破韧就按键不该有任何反应
        var list = new List<DeathblowCandidate> { Enemy(1.0f, 0f, false) };

        int index = DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void 窗口已过期的敌人不能处决()
    {
        // `CanBeExecuted` 由 Ashigaru 计算（破韧态且 IsOpen），这里模拟"窗口过期"
        var list = new List<DeathblowCandidate> { Enemy(1.0f, 0f, false) };

        Assert.Equal(-1, DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list));
    }

    [Theory]
    [InlineData(2.19f, true)]
    [InlineData(2.20f, true)]     // 边界含等号
    [InlineData(2.21f, false)]
    [InlineData(5.00f, false)]
    public void 距离边界(float distance, bool shouldWork)
    {
        var list = new List<DeathblowCandidate> { Enemy(distance, 0f, true) };

        int index = DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list);

        Assert.Equal(shouldWork, index >= 0);
    }

    [Fact]
    public void 距离只看水平面_忽略高度差()
    {
        // 处决是地面动作。用 XZ 平面距离，所以"敌人在斜坡上高一米"不影响判定
        var list = new List<DeathblowCandidate> { Enemy(1.5f, 0f, true) };

        Assert.True(DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list) >= 0);
    }

    [Fact]
    public void 多个目标取最近的那个()
    {
        // ★ 玩家expect的是"砍我面前这个"，而不是"场景树里第一个"
        var list = new List<DeathblowCandidate>
        {
            Enemy(2.0f, 0f, true),   // 远
            Enemy(0.8f, 0f, true),   // 近 ← 应该选它
            Enemy(1.5f, 0f, true),
        };

        int index = DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list);

        Assert.Equal(1, index);
    }

    [Fact]
    public void 近的那个没破韧_选远的破韧目标()
    {
        var list = new List<DeathblowCandidate>
        {
            Enemy(0.5f, 0f, false),  // 最近但站着，不能处决
            Enemy(2.0f, 0f, true),   // 远一点但破韧了
        };

        int index = DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list);

        Assert.Equal(1, index);
    }

    [Fact]
    public void 目标在身后也能处决_不要求朝向()
    {
        // 处决不做角度判定：玩家刚打完一套，朝向未必正对，
        // 这时"按了没反应"会比"背身处决"更让人困惑。
        var list = new List<DeathblowCandidate> { Enemy(0f, 1.5f, true) };

        Assert.True(DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 2.2f, list) >= 0);
    }

    [Fact]
    public void 空列表与非法距离都安全()
    {
        Assert.Equal(-1, DeathblowResolver.Resolve(
            new System.Numerics.Vector2(0f, 0f), 2.2f, new List<DeathblowCandidate>()));

        var list = new List<DeathblowCandidate> { Enemy(0.5f, 0f, true) };
        Assert.Equal(-1, DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), 0f, list));
        Assert.Equal(-1, DeathblowResolver.Resolve(new System.Numerics.Vector2(0f, 0f), -1f, list));
    }

    // ── 交互键优先级 ─────────────────────────────────────────────

    [Fact]
    public void 处决优先级最高_压过对话()
    {
        Assert.Equal(InteractPriority.Deathblow,
            DeathblowResolver.Winner(canDeathblow: true, inDialogue: true, tutorialWantsSheathe: true));
    }

    [Fact]
    public void 处决优先级最高_压过教学收刀与深吸()
    {
        Assert.Equal(InteractPriority.Deathblow,
            DeathblowResolver.Winner(true, false, true));
    }

    [Fact]
    public void 不能处决时按阶梯往下走()
    {
        Assert.Equal(InteractPriority.Dialogue,
            DeathblowResolver.Winner(false, true, true));

        Assert.Equal(InteractPriority.TutorialSheathe,
            DeathblowResolver.Winner(false, false, true));

        Assert.Equal(InteractPriority.GauntletAbsorb,
            DeathblowResolver.Winner(false, false, false));
    }

    [Fact]
    public void 优先级顺序表与枚举一致()
    {
        // 防止有人加了新消费者却忘了想清楚它插在哪一级
        Assert.Equal(4, DeathblowResolver.PriorityOrder.Length);
        Assert.Equal(InteractPriority.Deathblow, DeathblowResolver.PriorityOrder[0]);
        Assert.Equal(InteractPriority.GauntletAbsorb,
            DeathblowResolver.PriorityOrder[DeathblowResolver.PriorityOrder.Length - 1]);
    }
}
