using Godot;
using Oniblade.Audio;

namespace Oniblade.Combat.Data;

/// <summary>
/// 弹开反馈的**一档**（T53 / 07 §1.1.3）。
///
/// ★ **它回答的是"质感"，不是"强度"。** 两者是不同的轴，别混：
/// 强度档 F1~F5 决定量级（弹开本来就是高强度），本档决定**同一个弹开**在
/// 斩 / 打 / 突 / 暗 四种攻击下听起来、看起来分别是什么样。
/// 把两层压成一个数字，就会滑向"打击比斩击更响所以所有通道一起加"的偷懒做法。
///
/// 四档与 <see cref="DamageType"/> 的四个值一一对应，数值落在
/// `data/combat/deflect_feedback.tres`——**调平衡改那里，不重新编译**。
/// </summary>
[GlobalClass]
public partial class DeflectFeedbackProfile : Resource
{
    /// <summary>档位名（与 <see cref="DamageType"/> 同名，便于调试面板与自检打印）。</summary>
    [Export] public string Id { get; set; } = "";

    // ── 声音 ───────────────────────────────────────────────────────

    /// <summary>
    /// 弹开音效。**Slash 档沿用既有的 <see cref="CombatSfx.Deflect"/>**：
    /// 那个"金属叮"本来就是斩击该有的声音（非谐波分音＝金属），不需要新文件。
    /// </summary>
    [Export] public CombatSfx Sfx { get; set; } = CombatSfx.Deflect;

    /// <summary>
    /// 音高倍率。★ 与连击升调（<c>AudioDirector.DeflectPitchScale</c>）**相乘**，不是替换：
    /// "连着弹开音越来越高"是核心反馈，不能被本档吃掉。
    /// </summary>
    [Export] public float PitchScale { get; set; } = 1f;

    /// <summary>音量（dB）。负值变小。</summary>
    [Export] public float VolumeDb { get; set; }

    // ── 火花 ───────────────────────────────────────────────────────

    /// <summary>粒子数量。</summary>
    [Export] public int SparkAmount { get; set; } = 14;

    /// <summary>锥面散布角（度）。越小越集中（突刺＝集中成束，打击＝散开）。</summary>
    [Export] public float SparkSpreadDeg { get; set; } = 40f;

    /// <summary>初速倍率（相对弹开基准）。越大溅得越快、越"脆"。</summary>
    [Export] public float SparkSpeedScale { get; set; } = 1f;

    /// <summary>粒子尺寸倍率。</summary>
    [Export] public float SparkScale { get; set; } = 1f;

    /// <summary>火花颜色。</summary>
    [Export] public Color SparkColor { get; set; } = new(0.78f, 0.88f, 1.00f);

    /// <summary>
    /// 火花存在帧数。**硬上限是 SparkBurst.DeflectLifetimeFrames（8 帧）**：
    /// 10 §4 的第一原则是"火花不能盖住判定"，所以这一项只能**更短**
    /// （突刺＝短促），不能更长。消费点会 clamp，这里写大了也不会真的越界。
    /// </summary>
    [Export] public int SparkLifetimeFrames { get; set; } = 8;
}
