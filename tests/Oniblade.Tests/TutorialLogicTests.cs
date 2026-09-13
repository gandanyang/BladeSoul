using Oniblade.Levels;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 教学第一幕的推进与提示纪律（T47 / 05 §5 铁律）。
/// </summary>
public class TutorialLogicTests
{
    [Fact]
    public void StartsAtMove_AndAdvancesInOrder()
    {
        var logic = new TutorialLogic();
        Assert.Equal(TutorialBeat.Move, logic.Beat);

        Assert.True(logic.NotifyProgress(true));
        Assert.Equal(TutorialBeat.LightAttack, logic.Beat);

        Assert.True(logic.NotifyProgress(true));
        Assert.Equal(TutorialBeat.ChargedAttack, logic.Beat);

        Assert.True(logic.NotifyProgress(true));
        Assert.Equal(TutorialBeat.Done, logic.Beat);

        Assert.False(logic.NotifyProgress(true));   // 走完了就不再推进
    }

    [Fact]
    public void DoesNotHintBeforeTwoFailures()   // ★ 05 §5 铁律 3
    {
        var logic = new TutorialLogic { StuckFramesPerFailure = 10, HintsAfterFailures = 2 };

        for (int i = 0; i < 10; i++)
            logic.NotifyProgress(false);

        Assert.Equal(1, logic.FailureCount);
        Assert.False(logic.ShouldShowHint);          // 才失败 1 次 → 不许提示
    }

    [Fact]
    public void HintsOnceAfterTwoFailures()
    {
        var logic = new TutorialLogic { StuckFramesPerFailure = 10, HintsAfterFailures = 2 };

        for (int i = 0; i < 20; i++)
            logic.NotifyProgress(false);

        Assert.Equal(2, logic.FailureCount);
        Assert.True(logic.ShouldShowHint);

        logic.MarkHintShown();
        Assert.False(logic.ShouldShowHint);          // 一段只给一次
    }

    [Fact]
    public void FailureCountResets_WhenBeatAdvances()
    {
        var logic = new TutorialLogic { StuckFramesPerFailure = 10, HintsAfterFailures = 2 };

        for (int i = 0; i < 20; i++)
            logic.NotifyProgress(false);
        Assert.Equal(2, logic.FailureCount);

        logic.NotifyProgress(true);                  // 过了这一段

        Assert.Equal(TutorialBeat.LightAttack, logic.Beat);
        Assert.Equal(0, logic.FailureCount);         // ★ 上一段的失败不许带到下一段
        Assert.False(logic.ShouldShowHint);
    }

    [Fact]
    public void ProgressResetsTheStuckCounter()
    {
        // 差点卡住 → 但是动了 → 计时必须归零，否则"偶尔停一下"会被算成失败。
        var logic = new TutorialLogic { StuckFramesPerFailure = 10 };

        for (int i = 0; i < 9; i++)
            logic.NotifyProgress(false);
        logic.NotifyProgress(true);

        Assert.Equal(TutorialBeat.LightAttack, logic.Beat);
        Assert.Equal(0, logic.FailureCount);
    }
}
