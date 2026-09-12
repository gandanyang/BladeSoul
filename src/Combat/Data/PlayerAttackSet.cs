using System.Collections.Generic;
using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 玩家的招式表。**它是一份数据，不是一个 if 链**——
/// 想换连段、加第四段、改顺序，改 `.tres` 就行，不用碰代码。
///
/// 刻意不用数组：具名槽位在 Inspector 里更好读，也不会踩到
/// 手写 `.tscn` 里类型化数组的序列化坑。
/// </summary>
[GlobalClass]
public partial class PlayerAttackSet : Resource
{
    [ExportGroup("轻斩连段（按住攻击键按顺序接）")]
    [Export] public AttackData? Light1 { get; set; }
    [Export] public AttackData? Light2 { get; set; }
    [Export] public AttackData? Light3 { get; set; }

    [ExportGroup("其他招式（M1 之后接入输入）")]
    [Export] public AttackData? Thrust { get; set; }
    [Export] public AttackData? Charged1 { get; set; }
    [Export] public AttackData? Charged2 { get; set; }
    [Export] public AttackData? Charged3 { get; set; }

    /// <summary>把轻斩连段拼成状态机能吃的数组（跳过空槽）。</summary>
    public AttackData[] BuildLightCombo()
    {
        var combo = new List<AttackData>(3);

        if (Light1 is not null) combo.Add(Light1);
        if (Light2 is not null) combo.Add(Light2);
        if (Light3 is not null) combo.Add(Light3);

        return combo.ToArray();
    }
}
