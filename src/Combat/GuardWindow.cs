using System;

namespace Oniblade.Combat;

/// <summary>
/// 进入防御时"从哪来"。它决定要不要付取消硬直（02 文档 §1 取消规则）。
/// </summary>
public enum GuardEntrySource
{
	/// <summary>从站立/移动进入：进入那一帧就打开弹开窗，没有任何硬直。</summary>
	Neutral = 0,

	/// <summary>
	/// 从攻击（后摇取消窗内）或受击硬直**取消**进入：
	/// 前 <c>GuardCancelLockFrames</c> 帧只能格挡、不能弹开。
	/// 惩罚只惩罚效率——这几帧玩家**不会挨打**，只是弹不开。
	/// </summary>
	Cancel = 1,
}

/// <summary>
/// 防御的"弹开窗什么时候打开"（纯逻辑，可单测）。
///
/// 为什么单独抽出来：这条规则有两个来源（进入方式 + 难度档硬直帧数），
/// 又要和 <see cref="CombatTuning"/> 算出的窗口宽度配合，
/// 混在状态类里就只能靠"玩一玩感觉对不对"来验证。
///
/// 两件事必须成立，否则弹开会退化：
/// 1. **取消硬直期内不弹开，但仍然格挡**（防防御键连打，但绝不惩罚存活）。
/// 2. **整段防御只打开一次窗口**——<c>CombatActor.OpenDeflectWindow</c> 是取最大值，
///    每帧都调就会把窗口刷成永不过期，防御立刻变成"按住就赢"。
/// </summary>
public sealed class GuardWindowState
{
	private int _framesSinceEntry;

	public GuardEntrySource Source { get; private set; } = GuardEntrySource.Neutral;

	/// <summary>本次防御实际要付的取消硬直帧数（<see cref="GuardEntrySource.Neutral"/> 时为 0）。</summary>
	public int CancelLockFrames { get; private set; }

	/// <summary>进入防御后第几帧打开弹开窗（0 = 进入那一帧）。</summary>
	public int OpenFrame => CancelLockFrames;

	/// <summary>进入防御后已经过到第几帧（进入那一帧记为 0）。</summary>
	public int FramesSinceEntry => _framesSinceEntry;

	public void Begin(GuardEntrySource source, int guardCancelLockFrames)
	{
		Source = source;
		CancelLockFrames = source == GuardEntrySource.Cancel ? Math.Max(0, guardCancelLockFrames) : 0;
		_framesSinceEntry = 0;
	}

	/// <summary>
	/// 本帧判定（在 <see cref="Advance"/> **之前**读）：取消硬直中——
	/// **仍然在格挡**（<c>IsGuarding</c> 为真），只是这一帧弹不开。
	/// </summary>
	public bool IsInCancelLock => _framesSinceEntry < CancelLockFrames;

	/// <summary>
	/// 本帧判定（在 <see cref="Advance"/> **之前**读）：现在正是该打开弹开窗的那一帧。
	/// 整段防御里只可能为真一帧。
	/// </summary>
	public bool OpensWindowThisFrame => _framesSinceEntry == CancelLockFrames;

	/// <summary>这一帧结束了，进入下一帧。</summary>
	public void Advance() => _framesSinceEntry++;
}
