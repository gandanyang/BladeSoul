using System.Collections.Generic;
using Godot;
using Oniblade.Audio;
using Oniblade.Combat.Data;
using Oniblade.Core;

namespace Oniblade.Progression;

/// <summary>
/// 蚀（03 §2.5）的三阶段。**只影响叙事与结局，绝不碰任何数值。**
/// </summary>
public enum ErosionStage
{
	/// <summary>初鸣：听过一次，然后决定不再多要。</summary>
	FirstCry,

	/// <summary>共鸣：开始听懂魔骸的哀鸣。</summary>
	Resonance,

	/// <summary>同化：在刀身与血泊的倒影里看见自己的未来。</summary>
	Assimilation,
}

/// <summary>
/// 噬魂笼手（03 §6.1 / §6.6）。挂在玩家身上，做四件事：
///
/// 1. **自动牵引**——魔骸死后生成魄火，魄火自己飞过来被吸进来（玩家不用去捡）。
/// 2. **连续吸魂反馈**——连吸数决定音阶，让吸魂成为战斗节奏的一部分。
/// 3. **「深吸」**——按住交互键强行大范围吸取（期间不能移动/攻击），代价是侵蚀。
/// 4. **侵蚀记账**——只由「深吸」与精英/BOSS 魄累积，**不影响任何数值**（01 §0 规则 4）。
/// </summary>
public partial class OniGauntlet : Node3D
{
	public const string GroupName = "oni_gauntlet";

	/// <summary>连吸计数多久没有新魄就归零（帧）。</summary>
	[Export] public int ChainResetFrames { get; set; } = 90;

	/// <summary>精英 / BOSS 魄单次吸收增加的侵蚀。</summary>
	[Export] public int GreatSoulErosion { get; set; } = 1;

	/// <summary>「深吸」状态下每个魄增加的侵蚀——比自动牵引贵得多。</summary>
	[Export] public int DeepAbsorbErosion { get; set; } = 1;

	/// <summary>笼手世界坐标上的吸附点（灰盒期用身体位置）。</summary>
	public Vector3 AbsorbPoint => GlobalPosition;

	public int SoulCount { get; private set; }
	public int ChainCount { get; private set; }
	public int DeepAbsorbCount { get; private set; }
	public int Erosion { get; private set; }

	/// <summary>正在「深吸」：玩家不能移动、不能攻击（由 PlayerActor 读取）。</summary>
	public bool IsDeepAbsorbing { get; private set; }

	/// <summary>03 §2.5 的三阶段。</summary>
	public ErosionStage Stage => Erosion switch
	{
		<= 0 => ErosionStage.FirstCry,
		<= 2 => ErosionStage.Resonance,
		_ => ErosionStage.Assimilation,
	};

	private int _chainFramesLeft;
	private int _soulsThisChain;
	private EventBus? _bus;

	public override void _EnterTree() => AddToGroup(GroupName);

	/// <summary>
	/// 显式订阅（而不是在 <c>_Ready</c> 里偷偷订阅）：
	/// 这样无头测试可以按自己的节奏接线，不依赖树序。
	/// </summary>
	public void Attach(EventBus bus)
	{
		if (_bus is not null)
			_bus.ActorDefeated -= OnActorDefeated;

		_bus = bus;
		_bus.ActorDefeated += OnActorDefeated;
	}

	public override void _ExitTree()
	{
		if (_bus is not null)
			_bus.ActorDefeated -= OnActorDefeated;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_chainFramesLeft > 0)
		{
			_chainFramesLeft--;
			if (_chainFramesLeft == 0)
				BreakChain();
		}
	}

	/// <summary>按住交互键 = 深吸。</summary>
	public void SetDeepAbsorbing(bool value) => IsDeepAbsorbing = value;

	/// <summary>由 <see cref="SoulOrb"/> 在飞到身上时回调。</summary>
	public void Absorb(SoulOrb orb)
	{
		SoulCount += orb.Amount;
		_chainFramesLeft = ChainResetFrames;
		_soulsThisChain++;
		ChainCount = _soulsThisChain;

		if (orb.ConsumedByDeepAbsorb)
		{
			DeepAbsorbCount++;
			Erosion += DeepAbsorbErosion;
		}

		if (orb.IsGreat)
			Erosion += GreatSoulErosion;

		PlayAbsorbFeedback();

		EventBus.Instance?.RaiseSoulGained(new SoulGainedEvent { Type = orb.Type, Amount = orb.Amount });
	}

	public void BreakChain()
	{
		_soulsThisChain = 0;
		ChainCount = 0;
	}

	private void PlayAbsorbFeedback()
	{
		AudioDirector? audio = AudioDirector.Instance;
		if (audio is null)
			return;

		// 每多连吸一个升 2 个半音，上限 +12——与弹开音高递增共用同一套"手感语言"。
		int semitones = Mathf.Min(_soulsThisChain * 2, 12);
		audio.PlayCombat(CombatSfx.SoulAbsorb, Mathf.Pow(1.0594631f, semitones));
	}

	private void OnActorDefeated(ActorDefeatedEvent e)
	{
		var orb = new SoulOrb
		{
			Type = SoulType.Crimson,
			Amount = AmountFor(e.Tier),
			IsGreat = e.Tier != EnemyTier.Grunt,
			ChainDelaySeconds = 0.06f * 0f,
		};

		Node? parent = GetTree().CurrentScene;
		if (parent is null)
			return;

		parent.AddChild(orb);
		orb.GlobalPosition = e.Position + new Vector3(0f, 1.1f, 0f);
	}

	private static int AmountFor(EnemyTier tier) => tier switch
	{
		EnemyTier.Elite => 40,
		EnemyTier.Boss => 120,
		_ => 10,
	};
}
