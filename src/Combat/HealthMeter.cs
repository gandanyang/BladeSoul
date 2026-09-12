using System;

namespace Oniblade.Combat;

/// <summary>血量（纯逻辑，可单测）。</summary>
public sealed class HealthMeter
{
    public int Max { get; }
    public int Current { get; private set; }

    public bool IsDead => Current <= 0;
    public float Ratio => Current / (float)Max;

    public HealthMeter(int max)
    {
        Max = max < 1 ? 1 : max;
        Current = Max;
    }

    /// <summary>扣血，返回**实际**扣掉的量（受上限截断）。</summary>
    public int Apply(int damage)
    {
        if (damage <= 0)
            return 0;

        int before = Current;
        Current = Math.Max(0, Current - damage);
        return before - Current;
    }

    public int ApplyScaled(int damage, float scale)
        => Apply((int)Math.Round(damage * Math.Max(0f, scale)));

    public void Heal(int amount)
    {
        if (amount <= 0)
            return;

        Current = Math.Min(Max, Current + amount);
    }

    public void ReviveTo(int percentOfMax)
    {
        Current = Math.Min(Max, Math.Max(1, Max * percentOfMax / 100));
    }
}
