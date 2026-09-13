using Godot;

namespace Oniblade.Levels;

/// <summary>
/// 一场遭遇战的配置（T42）。**数值一律进这里**（铁律 1），代码里一个数都不写。
///
/// 挂到关卡里已有的 `combat_area` 标记上——T32 的白盒早就把这些点标好了，
/// 遭遇战直接站在它的肩膀上，不要另起一套坐标。
/// </summary>
[GlobalClass]
public partial class EncounterProfile : Resource
{
    [ExportGroup("触发")]

    /// <summary>玩家走到多近算"进场"（米）。</summary>
    [Export] public float ActivationRadius { get; set; } = 9f;

    [ExportGroup("敌人")]

    /// <summary>敌人场景路径（`res://...`）。放路径而不是 PackedScene，是为了让 .tres 能被人手写。</summary>
    [Export] public string EnemyScenePath { get; set; } = "res://scenes/actors/enemies/Enemy.tscn";

    [Export] public int EnemyCount { get; set; } = 3;

    /// <summary>围绕遭遇点生成的半径（米）。太近会挤在玩家身上，太远会看不见。</summary>
    [Export] public float SpawnRadius { get; set; } = 5f;

    [ExportGroup("清场")]

    /// <summary>连续多少帧没有活敌人才算清场（见 <see cref="EncounterLogic"/> 的解释）。</summary>
    [Export] public int ClearHoldFrames { get; set; } = 12;
}
