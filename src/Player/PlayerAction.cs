namespace Oniblade.Player;

/// <summary>
/// 玩家可被缓冲/消费的动作。
/// 刻意用 enum 而不是 Godot 的 StringName：StringName 会触碰引擎互操作，
/// 用它做键就再也无法脱离引擎跑输入缓冲的单测了。
/// </summary>
public enum PlayerAction
{
    Attack,
    Guard,
    Dodge,
    LockOn,
    Interact,
    OniMagic,
    ItemUse,
}
