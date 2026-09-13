using Oniblade.Levels;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 遭遇战的规则（T42）。钉死三件容易写错的事：
/// ① 只触发**一次**；② 玩家没进来就不触发；③ 清场要**连续 N 帧**没活敌人才算。
/// </summary>
public class EncounterLogicTests
{
    [Fact]
    public void StaysDormant_UntilPlayerStepsIn()
    {
        var logic = new EncounterLogic();

        Assert.False(logic.NotifyPlayerInside(false));
        Assert.Equal(EncounterPhase.Dormant, logic.Phase);
    }

    [Fact]
    public void ActivatesExactlyOnce()
    {
        var logic = new EncounterLogic();

        Assert.True(logic.NotifyPlayerInside(true));      // 第一次进来 → 该刷怪了
        Assert.Equal(EncounterPhase.Active, logic.Phase);
        Assert.False(logic.NotifyPlayerInside(true));     // 再喂也不会刷第二次
    }

    [Fact]
    public void DoesNotClearWhileAnyEnemyAlive()
    {
        var logic = new EncounterLogic { ClearHoldFrames = 3 };
        logic.NotifyPlayerInside(true);
        logic.NotifySpawned(2);

        Assert.False(logic.NotifyAliveCount(1));
        Assert.False(logic.NotifyAliveCount(0));   // 第 1 帧
        Assert.False(logic.NotifyAliveCount(0));   // 第 2 帧
        Assert.Equal(EncounterPhase.Active, logic.Phase);
    }

    [Fact]
    public void ClearsAfterHoldFrames_AndOnlyOnce()
    {
        var logic = new EncounterLogic { ClearHoldFrames = 3 };
        logic.NotifyPlayerInside(true);
        logic.NotifySpawned(2);

        logic.NotifyAliveCount(0);
        logic.NotifyAliveCount(0);
        Assert.True(logic.NotifyAliveCount(0));    // 第 3 帧 → 清场
        Assert.Equal(EncounterPhase.Cleared, logic.Phase);
        Assert.False(logic.NotifyAliveCount(0));   // 不再重复报清场
    }

    [Fact]
    public void HoldCounterResets_WhenAnEnemyComesBackAlive()
    {
        // 这是"清场抖动"的防线：读数闪一下 1，确认计时必须归零重来。
        var logic = new EncounterLogic { ClearHoldFrames = 3 };
        logic.NotifyPlayerInside(true);
        logic.NotifySpawned(2);

        Assert.False(logic.NotifyAliveCount(0));   // 第 1 帧
        Assert.False(logic.NotifyAliveCount(1));   // ← 抖了一下：计时必须归零
        Assert.False(logic.NotifyAliveCount(0));   // 归零后重新数：第 1 帧
        Assert.False(logic.NotifyAliveCount(0));   // 第 2 帧
        Assert.Equal(EncounterPhase.Active, logic.Phase);   // 还没清（这次不能再调它了）
        Assert.True(logic.NotifyAliveCount(0));    // 第 3 个**连续**帧才算清场
    }
}
