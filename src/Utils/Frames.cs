using System;

namespace Oniblade.Utils;

/// <summary>
/// 全项目唯一的帧/秒换算入口。
/// 逻辑一律用 60fps 的整数帧表达，秒只允许出现在 UI、音频、Tween 这三处。
/// </summary>
public static class Frames
{
    public const int PerSecond = 60;
    public const double SecondsPerFrame = 1.0 / PerSecond;

    public static double ToSeconds(int frames) => frames * SecondsPerFrame;

    public static float ToDelta(int frames) => frames / (float)PerSecond;

    public static int FromSeconds(double seconds) => (int)Math.Round(seconds * PerSecond);

    public static int Clamp(int frames, int min, int max)
        => frames < min ? min : frames > max ? max : frames;
}
