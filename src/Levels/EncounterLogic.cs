namespace Oniblade.Levels;

/// <summary>遭遇战的三个阶段（T42）。</summary>
public enum EncounterPhase
{
    /// <summary>还没触发（玩家没走进来）。</summary>
    Dormant,

    /// <summary>打起来了。</summary>
    Active,

    /// <summary>清场了，路放开了。</summary>
    Cleared,
}

/// <summary>
/// 一场遭遇战的规则——**纯逻辑，不依赖场景树**（铁律 8），所以它能进 xUnit。
///
/// 它只回答两个问题：**"该不该刷敌人了"** 和 **"算不算清场了"**。
/// 生成与放行是 `EncounterZone` 的事。
///
/// ★ 清场要**连续 N 帧**没有活敌人才算数——和掉落保护同一个理由：
/// 敌人死亡那一帧与节点回收之间会有一瞬间的读数抖动，
/// 一帧就下结论会让"放行"在错误的时机弹开。
/// </summary>
public sealed class EncounterLogic
{
    /// <summary>清场确认帧数（连续这么多帧没活敌人才放行）。</summary>
    public int ClearHoldFrames { get; set; } = 12;

    public EncounterPhase Phase { get; private set; } = EncounterPhase.Dormant;

    /// <summary>这一次遭遇战一共刷了几只（放行与测试都要读）。</summary>
    public int SpawnedCount { get; private set; }

    private int _clearFrames;

    /// <summary>喂"玩家在不在圈里"。返回 true ＝ **这一刻该生成敌人了**（只返回一次）。</summary>
    public bool NotifyPlayerInside(bool inside)
    {
        if (Phase != EncounterPhase.Dormant || !inside)
            return false;

        Phase = EncounterPhase.Active;
        return true;      // 由调用方生成敌人，再调 NotifySpawned
    }

    public void NotifySpawned(int count)
    {
        SpawnedCount = count;
        _clearFrames = 0;
    }

    /// <summary>喂"还剩几个活的"。返回 true ＝ **这一刻清场了**（只返回一次）。</summary>
    public bool NotifyAliveCount(int alive)
    {
        if (Phase != EncounterPhase.Active)
            return false;

        if (alive > 0)
        {
            _clearFrames = 0;
            return false;
        }

        _clearFrames++;
        if (_clearFrames < ClearHoldFrames)
            return false;

        Phase = EncounterPhase.Cleared;
        return true;
    }
}
