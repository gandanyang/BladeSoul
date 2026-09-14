namespace Oniblade.Combat;

/// <summary>
/// 破韧窗口的纯逻辑（T52）——**不依赖场景树**，可以被单元测试直接实例化。
///
/// 它只回答一个问题：**"现在还能不能处决"**。
///
/// 窗口是按**帧**走的，不是按秒（04 §12：逻辑帧是权威，动画只是表现）。
/// 之所以要做成独立类而不是写在状态里：状态类持有 `CombatActor`、只能靠端到端场景验证，
/// 而"窗口几帧过期"这种边界最容易错一帧，必须能用单测钉死。
/// </summary>
public sealed class PostureBrokenWindow
{
    private int _framesLeft;

    /// <summary>当前窗口总长（帧）。0 表示从未开过。</summary>
    public int DurationFrames { get; private set; }

    /// <summary>窗口还开着的帧数。0 = 已过期，不再可处决。</summary>
    public int FramesLeft => _framesLeft;

    /// <summary>窗口是否开着（= 此刻按处决键有效）。</summary>
    public bool IsOpen => _framesLeft > 0;

    /// <summary>开过几次窗。用于断言"窗口只开一次、过期不复活"。</summary>
    public int OpenCount { get; private set; }

    /// <summary>
    /// 打开窗口。<paramref name="durationFrames"/> 必须来自数据
    /// （`data/**/*.tres`），不许在调用点写字面量。
    /// </summary>
    public void Begin(int durationFrames)
    {
        DurationFrames = durationFrames < 0 ? 0 : durationFrames;
        _framesLeft = DurationFrames;
        OpenCount++;
    }

    /// <summary>过一帧。**只递减，绝不因为"又被打了一下"而重置**——
    /// 否则玩家一直砍就能把窗口一直续下去，处决就永远不急了。</summary>
    public void Tick()
    {
        if (_framesLeft > 0)
            _framesLeft--;
    }

    /// <summary>关掉窗口（已经处决了，或者敌人死了）。</summary>
    public void Close()
    {
        _framesLeft = 0;
    }

    /// <summary>窗口结束后的恢复。用于完整地重新开始一轮。</summary>
    public void Reset()
    {
        _framesLeft = 0;
        DurationFrames = 0;
        OpenCount = 0;
    }
}
