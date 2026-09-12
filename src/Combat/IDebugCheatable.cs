namespace Oniblade.Combat;

/// <summary>
/// 只读侧 + 写入侧的一对调试接口：
/// <see cref="ICombatActorDebug"/> 给面板读，本接口给面板写。
///
/// **只有调试面板（F4 无敌开关）允许使用本接口，任何战斗逻辑都不许读它。**
/// 这样"作弊"这件事在代码里只有一个入口，将来删掉也不用翻遍战斗代码。
///
/// 面板是可选实现的：节点没实现本接口时 F4 只会显示"不支持"，不会报错。
/// （T2 新增接口，待制作人登记进 00 文档 §2.3。）
/// </summary>
public interface IDebugCheatable
{
    /// <summary>F4：让这个单位彻底不吃伤害与体干（不影响生产逻辑，只影响调试）。</summary>
    bool DebugInvulnerable { get; set; }
}
