using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 帧窗计算（02 文档 §2.1 / §1 取消规则）。
/// 这些数字是全部战斗手感的地基，边界必须钉死。
/// </summary>
public class AttackTimingTests
{
    /// <summary>轻斩·壹：前摇 8 / 判定 4 / 后摇 14 / 后摇第 6 帧起可取消。</summary>
    private static AttackTiming Light1 => new()
    {
        StartupFrames = 8,
        ActiveFrames = 4,
        RecoveryFrames = 14,
        CancelFromRecoveryFrame = 6,
    };

    [Fact]
    public void Ranges_Add_Up()
    {
        AttackTiming t = Light1;

        Assert.Equal(26, t.TotalFrames);
        Assert.Equal(8, t.ActiveStart);
        Assert.Equal(12, t.ActiveEnd);
        Assert.Equal(12, t.RecoveryStart);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    [InlineData(25, false)]
    [InlineData(26, false)]
    public void Active_Window_Boundaries(int frame, bool expected)
    {
        Assert.Equal(expected, Light1.IsActiveAt(frame));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Recovery_Window_Boundaries(int frame, bool expected)
    {
        Assert.Equal(expected, Light1.IsInRecoveryAt(frame));
    }

    [Fact]
    public void Cancel_Window_Opens_Inside_Recovery_Not_Before_Active_Ends()
    {
        // 后摇偏移 6 → 绝对帧 12 + 6 = 18
        Assert.Equal(18, Light1.CancelOpenFrame);
        Assert.False(Light1.CanCancelAt(17));
        Assert.True(Light1.CanCancelAt(18));
    }

    [Fact]
    public void Cancel_Window_Is_Never_Open_For_Uncancelable_Attack()
    {
        var charged = new AttackTiming
        {
            StartupFrames = 34,
            ActiveFrames = 6,
            RecoveryFrames = 30,
            CancelFromRecoveryFrame = -1,
        };

        Assert.False(charged.Cancelable);
        Assert.Equal(int.MaxValue, charged.CancelOpenFrame);
        Assert.False(charged.CanCancelAt(0));
        Assert.False(charged.CanCancelAt(1000));
    }
}

/// <summary>
/// 三连推进。**"狂按不会更快"** 这条规则必须由测试守住：
/// 在取消窗之前按，一次都不许接上。
/// </summary>
public class AttackSequenceTests
{
    private static AttackSequence ThreeHitCombo() => new(new[]
    {
        new AttackTiming { StartupFrames = 8, ActiveFrames = 4, RecoveryFrames = 14, CancelFromRecoveryFrame = 6 },
        new AttackTiming { StartupFrames = 9, ActiveFrames = 4, RecoveryFrames = 16, CancelFromRecoveryFrame = 7 },
        new AttackTiming { StartupFrames = 13, ActiveFrames = 5, RecoveryFrames = 26, CancelFromRecoveryFrame = 12 },
    });

    private static void Tick(AttackSequence sequence, int frames)
    {
        for (int i = 0; i < frames; i++)
            sequence.Tick();
    }

    [Fact]
    public void Starts_At_First_Step()
    {
        var sequence = ThreeHitCombo();

        sequence.Start();

        Assert.True(sequence.IsRunning);
        Assert.Equal(0, sequence.StepIndex);
        Assert.Equal(0, sequence.Frame);
    }

    [Fact]
    public void Cannot_Chain_Before_Cancel_Window()
    {
        var sequence = ThreeHitCombo();
        sequence.Start();

        Tick(sequence, 17);              // 取消窗在第 18 帧才开

        Assert.False(sequence.CanChain);
        Assert.False(sequence.TryChain());
        Assert.Equal(0, sequence.StepIndex);
    }

    [Fact]
    public void Can_Chain_Once_Cancel_Window_Opens()
    {
        var sequence = ThreeHitCombo();
        sequence.Start();
        Tick(sequence, 18);

        Assert.True(sequence.CanChain);
        Assert.True(sequence.TryChain());
        Assert.Equal(1, sequence.StepIndex);
        Assert.Equal(0, sequence.Frame);
    }

    [Fact]
    public void Cannot_Chain_Past_The_Last_Step()
    {
        var sequence = ThreeHitCombo();
        sequence.Start();

        Tick(sequence, 18);
        Assert.True(sequence.TryChain());
        Tick(sequence, 21);              // 轻斩贰：12+7 = 19
        Assert.True(sequence.TryChain());
        Assert.Equal(2, sequence.StepIndex);

        Tick(sequence, 100);             // 第三段再往后拉满

        Assert.False(sequence.CanChain);
        Assert.False(sequence.TryChain());
        Assert.Equal(2, sequence.StepIndex);
    }

    [Fact]
    public void Finishes_After_Total_Frames()
    {
        var sequence = ThreeHitCombo();
        sequence.Start();

        Tick(sequence, 25);
        Assert.False(sequence.IsFinished);

        Tick(sequence, 1);               // 第 26 帧
        Assert.True(sequence.IsFinished);
    }

    [Fact]
    public void Active_Only_Inside_Active_Frames()
    {
        var sequence = ThreeHitCombo();
        sequence.Start();

        Tick(sequence, 7);
        Assert.False(sequence.IsActive);

        Tick(sequence, 1);               // 第 8 帧
        Assert.True(sequence.IsActive);

        Tick(sequence, 4);               // 第 12 帧
        Assert.False(sequence.IsActive);
    }

    [Fact]
    public void Empty_Sequence_Is_Not_Running()
    {
        var sequence = new AttackSequence(System.Array.Empty<AttackTiming>());

        sequence.Start();

        Assert.False(sequence.IsRunning);
        Assert.False(sequence.CanChain);
    }
}
