using System;

namespace Oniblade.Combat;

/// <summary>喝血的三段（02 §2.4）。</summary>
public enum HealPhase
{
	/// <summary>掏壶。此段**可以被打断**，且被打断则本次不消耗次数。</summary>
	Startup,

	/// <summary>饮用。进入此段后受击**不再打断动作**，但**伤害照常结算**。</summary>
	Drink,

	/// <summary>收招。可被闪避/防御取消。</summary>
	Recovery,

	/// <summary>整段结束。</summary>
	Done,
}

/// <summary>
/// 喝血的三段帧窗（纯逻辑，可单测）。02 §2.4。
///
/// 为什么单独抽出来：这个机制的全部设计意图都压在**一条边界**上——
/// "起手段可以被打断、饮用段不能"。这条线错了，机制就变成另一个东西：
/// 要么退化成"喝血随便被断"（只狼式的背板惩罚），要么退化成"喝血无敌"
/// （受伤不打断也不掉血）。两种退化在试玩里都只是"感觉怪怪的"，
/// 很难靠手感定位，所以必须由单测钉住。
///
/// 与 <see cref="DodgeWindow"/> / <see cref="GuardWindowState"/> 同一个约定：
/// 第 0 帧 = 玩家按下喝血的那一帧，且**属于起手段**（所以同一帧挨打会打断它）。
/// </summary>
public sealed class HealWindow
{
	private int _frame;

	public int StartupFrames { get; private set; }
	public int DrinkFrames { get; private set; }
	public int RecoveryFrames { get; private set; }

	/// <summary>已经过到第几帧（按下那一帧记为 0）。</summary>
	public int Frame => _frame;

	public int TotalFrames => StartupFrames + DrinkFrames + RecoveryFrames;

	/// <summary>饮用段的起始帧（= 起手段长度）。</summary>
	public int DrinkStartFrame => StartupFrames;

	/// <summary>收招段的起始帧。</summary>
	public int RecoveryStartFrame => StartupFrames + DrinkFrames;

	public void Begin(int startupFrames, int drinkFrames, int recoveryFrames)
	{
		StartupFrames = Math.Max(0, startupFrames);
		DrinkFrames = Math.Max(0, drinkFrames);
		RecoveryFrames = Math.Max(0, recoveryFrames);
		_frame = 0;
	}

	/// <summary>本帧处于哪一段（在 <see cref="Advance"/> **之前**读）。</summary>
	public HealPhase Phase
	{
		get
		{
			if (_frame < StartupFrames)
				return HealPhase.Startup;

			if (_frame < RecoveryStartFrame)
				return HealPhase.Drink;

			if (_frame < TotalFrames)
				return HealPhase.Recovery;

			return HealPhase.Done;
		}
	}

	/// <summary>
	/// 本帧受击**会不会打断动作**（02 §2.4 的核心规则）。
	/// 只有起手段会——饮用段之后"玩家永远能喝完"，但伤害照常扣。
	/// </summary>
	public bool CanBeInterruptedNow => Phase == HealPhase.Startup;

	/// <summary>本帧正好跨进饮用段：这是**扣次数**的那一帧（起手被打断就不扣）。</summary>
	public bool EnteredDrinkThisFrame => _frame == DrinkStartFrame;

	/// <summary>本帧正好跨进收招段：这是**回血生效**的那一帧（"喝完"）。</summary>
	public bool EnteredRecoveryThisFrame => _frame == RecoveryStartFrame;

	public bool IsFinished => _frame >= TotalFrames;

	/// <summary>这一帧结束了，进入下一帧。</summary>
	public void Advance() => _frame++;
}
