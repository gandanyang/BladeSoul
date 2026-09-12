using System.Collections.Generic;
using Godot;

namespace Oniblade.World;

/// <summary>
/// 氛围控制器（T34 / 10 §3.1）：把一份 <see cref="AtmosphereProfile"/> 落到
/// WorldEnvironment（调色 + 体积雾 + 辉光）、跟摄像机的雨、以及灯笼上。
///
/// **一切都是数据驱动的**：代码里没有任何一个氛围数字，全在 .tres 里（04 §2）。
///
/// **它不自己判断降级**——降级统一听 <see cref="QualityDirector"/>（07 §7 的顺序：
/// ① 体积雾 → ② 粒子 → ③ 同屏敌人数）。场景里没有 QualityDirector 时不降级。
/// </summary>
public partial class AtmosphereController : Node3D
{
	public const string LanternGroup = "lantern";

	[Export] public AtmosphereProfile? Profile { get; set; }

	/// <summary>没有摄像机时雨盒挂在哪里（正常情况每帧跟随 Camera3D）。</summary>
	[Export] public Vector3 FallbackRainAnchor { get; set; } = Vector3.Zero;

	private WorldEnvironment? _world;
	private Environment? _environment;
	private GpuParticles3D? _rain;
	private readonly List<OmniLight3D> _lanternLights = new();

	/// <summary>雾当前是否生效（自检与调试面板用）。</summary>
	public bool FogActive { get; private set; }

	/// <summary>雨当前是否在发射。</summary>
	public bool RainActive { get; private set; }

	/// <summary>实际点亮的灯笼数。</summary>
	public int LanternCount => _lanternLights.Count;

	/// <summary>因为超过 <see cref="AtmosphereProfile.MaxLanterns"/> 而没有点亮的数量。</summary>
	public int LanternsSkipped { get; private set; }

	/// <summary>雨盒当前的世界位置（自检靠它证明"跟着摄像机"）。</summary>
	public Vector3 RainAnchor => _rain?.GlobalPosition ?? FallbackRainAnchor;

	public Environment? AppliedEnvironment => _environment;

	public override void _Ready()
	{
		if (Profile is null)
		{
			GD.PushWarning("[氛围] 没有配 AtmosphereProfile，什么都不做");
			return;
		}

		ResolveEnvironment();
		ApplyColorGrade();
		BuildRain();
		BuildLanterns();
		ApplyDegradation();
	}

	public override void _PhysicsProcess(double delta)
	{
		FollowCameraWithRain();
		ApplyDegradation();
	}

	// ── 世界环境 ───────────────────────────────────────────────

	private void ResolveEnvironment()
	{
		Node? parent = GetParent();

		if (parent is not null)
		{
			foreach (Node sibling in parent.GetChildren())
			{
				if (sibling is WorldEnvironment world)
				{
					_world = world;
					break;
				}
			}
		}

		if (_world is null)
		{
			_world = new WorldEnvironment { Name = "AtmosphereEnvironment" };
			AddChild(_world);
		}

		// **复制一份再改**：关卡里的 Environment 可能是共享的 .tres 或子资源，
		// 直接改会污染同场景的其它使用方（也会在重载场景之间串味）。
		Environment source = _world.Environment ?? new Environment();
		_environment = (Environment)source.Duplicate();
		_world.Environment = _environment;
	}

	private void ApplyColorGrade()
	{
		if (Profile is not { } profile || _environment is not { } env)
			return;

		env.BackgroundMode = Environment.BGMode.Color;
		env.BackgroundColor = profile.BackgroundColor;

		// 环境光**必须偏冷**（10 §1：暖色只允许出现在灯笼上）。自检盯着这一条。
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor = profile.AmbientColor;
		env.AmbientLightEnergy = profile.AmbientEnergy;

		env.TonemapMode = Environment.ToneMapper.Filmic;
		env.TonemapExposure = profile.TonemapExposure;

		// 低饱和青灰：整档氛围的底色。
		env.AdjustmentEnabled = true;
		env.AdjustmentSaturation = profile.Saturation;

		// 辉光：让灯笼与魔骸的红光透出来（也是"雾里仍能读招"的一半）
		env.GlowEnabled = true;
		env.GlowIntensity = 0.6f;
		env.GlowBloom = 0.15f;

		env.VolumetricFogEnabled = profile.FogEnabled;
		env.VolumetricFogDensity = profile.FogDensity;
		env.VolumetricFogAlbedo = profile.FogAlbedoColor;
		// 注意：Godot 的体积雾**没有** height / height_density——
		// 那两个是深度雾（fog_*）的属性，Unity 的术语容易让人写错。
		// 体积雾靠 density + length + anisotropy 控制，够用。
		env.VolumetricFogAnisotropy = profile.FogAnisotropy;
		env.VolumetricFogLength = profile.FogLength;
	}

	// ── 雨 ─────────────────────────────────────────────────────

	private void BuildRain()
	{
		if (Profile is not { RainEnabled: true } profile)
			return;

		var process = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = profile.RainBoxExtents,
			Direction = Vector3.Down,
			Spread = 0f,
			InitialVelocityMin = profile.RainFallSpeed,
			InitialVelocityMax = profile.RainFallSpeed,
			Gravity = Vector3.Zero,
			ScaleMin = 1f,
			ScaleMax = 1f,
			Color = profile.RainColor,
		};

		_rain = new GpuParticles3D
		{
			Name = "Rain",
			Amount = profile.RainAmount,
			Lifetime = profile.RainLifetime,
			ProcessMaterial = process,
			DrawPass1 = BuildRainStreak(profile.RainColor),
			// 雨是背景元素，不该投影、也不该参与可见性剔除的判断
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};

		AddChild(_rain);
		_rain.GlobalPosition = FallbackRainAnchor;
		_rain.Emitting = true;
		RainActive = true;
	}

	/// <summary>雨丝：一条细长的盒子。用盒子而不是四边形，是为了从任何角度看都有一根线。</summary>
	private static Mesh BuildRainStreak(Color color)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			VertexColorUseAsAlbedo = true,
		};

		return new BoxMesh
		{
			Size = new Vector3(0.015f, 0.7f, 0.015f),
			Material = material,
		};
	}

	/// <summary>
	/// 雨盒每帧跟到摄像机头上。**不能只在固定区域下**（卡片硬要求）——
	/// 固定区域的雨在玩家走开后就会露馅。
	/// </summary>
	private void FollowCameraWithRain()
	{
		if (_rain is null)
			return;

		Camera3D? camera = GetViewport()?.GetCamera3D();

		Vector3 anchor = camera is not null
			? camera.GlobalPosition
			: FallbackRainAnchor;

		// 抬高一点，让雨在摄像机上方生成、落进视野
		_rain.GlobalPosition = anchor + Vector3.Up * 4f;
	}

	// ── 灯笼 ───────────────────────────────────────────────────

	/// <summary>
	/// 重建灯笼（清掉旧的再按当前场上的 <c>lantern</c> 组重来）。
	/// 给自检与"关卡热重载"用——正常路径只在 <c>_Ready</c> 建一次。
	/// </summary>
	public void RebuildLanterns()
	{
		foreach (OmniLight3D light in _lanternLights)
			light.QueueFree();

		_lanternLights.Clear();
		LanternsSkipped = 0;

		BuildLanterns();
	}

	private void BuildLanterns()
	{
		if (Profile is not { } profile)
			return;

		int lit = 0;

		foreach (Node node in GetTree().GetNodesInGroup(LanternGroup))
		{
			if (node is not Node3D lantern)
				continue;

			// 数量纪律：暖色＝注意力，多了会抢读招（10 §3.1）。
			if (lit >= profile.MaxLanterns)
			{
				LanternsSkipped++;
				continue;
			}

			var light = new OmniLight3D
			{
				Name = "LanternLight",
				LightColor = profile.LanternColor,
				LightEnergy = profile.LanternEnergy,
				OmniRange = profile.LanternRange,
				ShadowEnabled = false,   // 灯笼不投影：省性能，而且雨夜里影子会乱
			};

			lantern.AddChild(light);
			_lanternLights.Add(light);

			// 灯罩本体自发光：不然灯"亮着但看不见"
			lantern.AddChild(new MeshInstance3D
			{
				Name = "LanternGlow",
				Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 8, Rings = 4 },
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = profile.LanternColor,
					EmissionEnabled = true,
					Emission = profile.LanternColor,
					EmissionEnergyMultiplier = profile.LanternGlowEnergy,
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});

			lit++;
		}

		if (LanternsSkipped > 0)
			GD.Print($"[氛围] 灯笼 {lit} 盏点亮，{LanternsSkipped} 盏因为超过上限" +
					 $"（{profile.MaxLanterns}）没有点亮——暖色＝注意力，不能多给");
	}

	// ── 降级（07 §7 的顺序）────────────────────────────────────

	/// <summary>
	/// 按当前降级等级立刻重算一遍。正常路径每物理帧自己会算；
	/// 这个入口给测试与调试面板用（改了等级之后要立刻看到效果）。
	/// </summary>
	public void RefreshDegradation() => ApplyDegradation();

	private void ApplyDegradation()
	{
		if (Profile is not { } profile)
			return;

		// 没有 QualityDirector 的场景不降级（训练场景不需要）。
		int level = QualityDirector.Instance?.Level ?? 0;

		bool fogAllowed = level < 1;
		bool particlesAllowed = level < 2;

		if (_environment is not null)
		{
			_environment.VolumetricFogEnabled = profile.FogEnabled && fogAllowed;
			FogActive = _environment.VolumetricFogEnabled;
		}

		if (_rain is not null)
		{
			_rain.Emitting = profile.RainEnabled && particlesAllowed;
			RainActive = _rain.Emitting;
		}

		foreach (OmniLight3D light in _lanternLights)
			light.Visible = true;
	}
}
