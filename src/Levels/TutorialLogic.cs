namespace Oniblade.Levels;

/// <summary>教学第一幕的三段（T47 / docs/15）：**一次只教一个机制**。</summary>
public enum TutorialBeat
{
    /// <summary>移动 / 视角 / 跳跃。</summary>
    Move,

    /// <summary>轻攻击（三连）。</summary>
    LightAttack,

    /// <summary>重攻击（蓄力斩）。</summary>
    ChargedAttack,

    /// <summary>三段都过了。</summary>
    Done,
}

/// <summary>
/// 教学第一幕的推进规则——**纯逻辑，不依赖场景树**（铁律 8），所以它能进 xUnit。
///
/// 它管两件事：
/// 1. **三段按顺序推进**（移动 → 轻攻击 → 重攻击）
/// 2. **提示纪律**（05 §5 铁律 3）：**只在失败 2 次之后才给提示**，
///    而且**一段只给一次**——目的是不打断玩家第一次的沉浸感。
///
/// "失败"在这里的定义是**卡住**：这一段的目标在 <see cref="StuckFramesPerFailure"/>
/// 帧内毫无进展。移动、挥刀这种教学没有"打输"，
/// 玩家真正需要被拉一把的时刻是**他不知道自己该干什么**。
/// </summary>
public sealed class TutorialLogic
{
    /// <summary>卡住多少帧算一次"失败"（默认 5 秒）。</summary>
    public int StuckFramesPerFailure { get; set; } = 300;

    /// <summary>失败几次之后才允许给提示（05 §5 铁律 3：2 次）。</summary>
    public int HintsAfterFailures { get; set; } = 2;

    public TutorialBeat Beat { get; private set; } = TutorialBeat.Move;

    /// <summary>本段已经失败过几次（换段归零）。</summary>
    public int FailureCount { get; private set; }

    /// <summary>本段的提示是不是已经给过了（一段只给一次）。</summary>
    public bool HintShown { get; private set; }

    private int _stuckFrames;

    /// <summary>
    /// 每帧喂"这一段的目标达成了没有"。
    /// 返回 true ＝ **刚刚推进到了下一段**（调用方该换目标了）。
    /// </summary>
    public bool NotifyProgress(bool objectiveDone)
    {
        if (Beat == TutorialBeat.Done)
            return false;

        if (objectiveDone)
        {
            Advance();
            return true;
        }

        _stuckFrames++;
        if (_stuckFrames >= StuckFramesPerFailure)
        {
            _stuckFrames = 0;
            FailureCount++;
        }

        return false;
    }

    /// <summary>现在该不该给提示（失败够次数了、而且本段还没给过）。</summary>
    public bool ShouldShowHint => !HintShown && FailureCount >= HintsAfterFailures;

    /// <summary>提示已经播出去了。</summary>
    public void MarkHintShown() => HintShown = true;

    private void Advance()
    {
        Beat = Beat switch
        {
            TutorialBeat.Move => TutorialBeat.LightAttack,
            TutorialBeat.LightAttack => TutorialBeat.ChargedAttack,
            _ => TutorialBeat.Done,
        };

        // 换段＝全新的目标，失败计数与提示状态都要清干净，
        // 否则上一段的失败会把下一段的提示提前放出来。
        FailureCount = 0;
        HintShown = false;
        _stuckFrames = 0;
    }
}
