using System;

namespace Oniblade.Combat;

/// <summary>
/// 体干条（纯逻辑，可单测）。
/// 体干是**涨**的：0 → Max，满了就破防。停止受击 RegenDelayFrames 帧后开始回落。
///
/// 关键规则（02 文档 §5）：血量越低，体干恢复越慢。
/// 这条规则让"先削血再削体干"成为有效战术，也给了不敢弹开的玩家一条推进路径。
/// </summary>
public sealed class PostureMeter
{
    public int Max { get; }

    public int Current { get; private set; }

    /// <summary>体干已满（破防状态）。</summary>
    public bool IsBroken { get; private set; }

    /// <summary>停止受击多久后开始回复。</summary>
    public int RegenDelayFrames { get; set; } = 30;

    /// <summary>每秒回复量（满血时的基准值）。</summary>
    public float RegenPerSecond { get; set; } = 22f;

    /// <summary>难度 / 角色属性的整体缩放。</summary>
    public float RegenScale { get; set; } = 1f;

    private int _framesSinceHit = int.MaxValue;

    public PostureMeter(int max)
    {
        Max = max < 1 ? 1 : max;
        Current = 0;
    }

    public float Ratio => Current / (float)Max;

    /// <summary>施加体干伤。返回 true 表示本次打满、进入破防。</summary>
    public bool Apply(int postureDamage)
    {
        if (postureDamage <= 0)
            return false;

        Current = Math.Min(Current + postureDamage, Max);
        _framesSinceHit = 0;

        if (Current >= Max)
        {
            IsBroken = true;
            return true;
        }

        return false;
    }

    /// <summary>按最大体干的百分比施加（一闪用）。</summary>
    public bool ApplyPercent(float percent)
        => Apply((int)Math.Round(Max * Math.Clamp(percent, 0f, 1f)));

    /// <summary>被处决 / 破防结束后清零。</summary>
    public void Reset()
    {
        Current = 0;
        IsBroken = false;
        _framesSinceHit = int.MaxValue;
    }

    /// <summary>每帧调用。<paramref name="healthRatio"/> 是当前血量比例（0~1）。</summary>
    public void Tick(int frames, float healthRatio)
    {
        if (frames <= 0)
            return;

        if (_framesSinceHit < RegenDelayFrames)
        {
            _framesSinceHit += frames;
            return;
        }

        // 破防期间不回复，否则破防窗口会自己被吃掉。
        if (IsBroken)
            return;

        if (Current <= 0)
            return;

        float perFrame = RegenPerSecond * RegenMultiplierForHealth(healthRatio) * RegenScale / Utils.Frames.PerSecond;
        Current = Math.Max(0, Current - (int)Math.Ceiling(perFrame * frames));
    }

    /// <summary>血量越低，体干恢复越慢。</summary>
    public static float RegenMultiplierForHealth(float healthRatio) => healthRatio switch
    {
        > 0.70f => 1.0f,
        >= 0.30f => 0.6f,
        _ => 0.3f,
    };
}
