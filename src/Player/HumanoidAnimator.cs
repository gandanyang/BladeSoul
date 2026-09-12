using System.Collections.Generic;
using Godot;

namespace Oniblade.Player;

/// <summary>
/// 程序化人形动画：把玩家状态翻译成 41 骨 Mixamo 骨架的姿势。
///
/// **为什么是程序化的**：AI 生成的模型只有骨架、没有动画剪辑（Mixamo 动画是后续管线），
/// 但"站着滑行"的观感比灰盒还差。先用代码把最基本的位移/攻击/受击姿势做出来，
/// 等动画资产到位后整块替换即可。
///
/// **轴向怎么来的**：不同骨架的局部轴朝向不一样，写死 X/Y/Z 一定会错。
/// 这里在启动时把"角色的左右方向（世界 X）"用每根骨的 **rest 全局姿态** 反算到它的局部空间，
/// 于是"绕这个轴旋转 = 前后摆动"对任何骨架都成立。
/// </summary>
public class HumanoidAnimator
{
	private static readonly string[] Tracked =
	{
		"Hip", "Spine01", "Spine02", "Head",
		"L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
		"L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
	};

	private const float AttackDuration = 0.5f;
	private const float IssenDuration = 0.35f;
	private const float HitDuration = 0.22f;

	private readonly Skeleton3D? _skel;
	private readonly Node3D _root;
	private readonly Dictionary<string, int> _bone = new();
	private readonly Dictionary<int, Vector3> _axis = new();

	private float _phase;
	private float _bob;
	private float _attackElapsed = -1f;
	private float _issenElapsed = -1f;
	private float _hitElapsed = -1f;
	private float _hitStrength;

	/// <summary>是否处于防御姿态（由玩家每帧写入）。</summary>
	public bool Guarding { get; set; }

	public HumanoidAnimator(Node3D modelRoot)
	{
		_root = modelRoot;
		_skel = FindSkeleton(modelRoot);
		if (_skel is null)
			return;

		foreach (string name in Tracked)
		{
			int bone = _skel.FindBone(name);
			if (bone < 0)
				continue;

			_bone[name] = bone;
			Basis rest = _skel.GetBoneGlobalRest(bone).Basis;
			Vector3 axis = rest.Inverse() * Vector3.Right;
			_axis[bone] = axis.LengthSquared() > 1e-6f ? axis.Normalized() : Vector3.Right;
		}
	}

	public bool Valid => _skel is not null && _bone.Count > 0;

	public void PlayAttack() => _attackElapsed = 0f;
	public void PlayIssen() => _issenElapsed = 0f;
	public void PlayHitReact(float strength)
	{
		_hitElapsed = 0f;
		_hitStrength = Mathf.Clamp(strength, 0f, 2f);
	}

	public void AnimateLocomotion(float speed01, float delta)
	{
		if (!Valid)
			return;

		_skel!.ResetBonePoses();
		speed01 = Mathf.Clamp(speed01, 0f, 1.5f);
		_phase += delta * Mathf.Lerp(2.5f, 10f, Mathf.Min(speed01, 1f));
		float swing = Mathf.Sin(_phase) * speed01;
		_bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.05f * speed01;

		if (Guarding)
		{
			Rot("L_Upperarm", 1.15f);
			Rot("R_Upperarm", 1.15f);
			Rot("L_Forearm", -1.0f);
			Rot("R_Forearm", -1.05f);
			Rot("L_Thigh", -0.12f);
			Rot("R_Thigh", -0.12f);
			Rot("L_Calf", -0.2f);
			Rot("R_Calf", -0.2f);
			return;
		}

		Rot("L_Thigh", swing * 0.8f);
		Rot("R_Thigh", -swing * 0.8f);
		Rot("L_Calf", -Mathf.Max(0f, Mathf.Sin(_phase)) * 0.9f * Mathf.Min(speed01, 1f));
		Rot("R_Calf", -Mathf.Max(0f, -Mathf.Sin(_phase)) * 0.9f * Mathf.Min(speed01, 1f));
		Rot("L_Upperarm", -swing * 0.6f);
		Rot("R_Upperarm", swing * 0.6f);
		Rot("L_Forearm", -Mathf.Abs(swing) * 0.25f);
		Rot("R_Forearm", -Mathf.Abs(swing) * 0.25f);
		Rot("Spine01", 0.12f * Mathf.Min(speed01, 1f));
		Rot("Head", -0.12f * Mathf.Min(speed01, 1f));
	}

	public void AnimateCombat(float delta)
	{
		if (!Valid)
			return;

		float rootX = 0f;

		if (_hitElapsed >= 0f)
		{
			_hitElapsed += delta;
			float t = Mathf.Clamp(_hitElapsed / HitDuration, 0f, 1f);
			float falloff = (1f - t) * (1f - t) * _hitStrength;
			Rot("Spine01", -0.5f * falloff);
			Rot("Spine02", -0.3f * falloff);
			Rot("Head", 0.4f * falloff);
			rootX = Mathf.Sin(_hitElapsed * 90f) * 0.05f * falloff;
			if (t >= 1f)
				_hitElapsed = -1f;
		}

		if (_issenElapsed >= 0f)
		{
			_issenElapsed += delta;
			float t = Mathf.Clamp(_issenElapsed / IssenDuration, 0f, 1f);
			float e = 1f - Mathf.Pow(1f - t, 3f);
			Rot("R_Upperarm", Mathf.Lerp(-2.2f, 1.6f, e));
			Rot("R_Forearm", Mathf.Lerp(0.3f, -0.4f, e));
			Rot("L_Upperarm", Mathf.Lerp(0.2f, -0.5f, e));
			Rot("Spine01", Mathf.Lerp(0f, 0.25f, e));
			if (t >= 1f)
				_issenElapsed = -1f;
		}
		else if (_attackElapsed >= 0f)
		{
			_attackElapsed += delta;
			float t = Mathf.Clamp(_attackElapsed / AttackDuration, 0f, 1f);
			float e = 1f - Mathf.Pow(1f - t, 3f);
			Rot("R_Upperarm", Mathf.Lerp(-2.4f, 1.5f, e));
			Rot("R_Forearm", Mathf.Lerp(0.2f, -0.3f, e));
			Rot("L_Upperarm", Mathf.Lerp(0.2f, -0.4f, e));
			Rot("Spine01", Mathf.Lerp(0f, 0.2f, e));
			if (t >= 1f)
				_attackElapsed = -1f;
		}

		_root.Position = new Vector3(rootX, _bob, 0f);
	}

	private void Rot(string name, float angle)
	{
		if (!_bone.TryGetValue(name, out int bone))
			return;

		_skel!.SetBonePoseRotation(bone, new Quaternion(_axis[bone], angle));
	}

	private static Skeleton3D? FindSkeleton(Node node)
	{
		if (node is Skeleton3D skeleton)
			return skeleton;

		foreach (Node child in node.GetChildren())
		{
			Skeleton3D? found = FindSkeleton(child);
			if (found is not null)
				return found;
		}

		return null;
	}
}
