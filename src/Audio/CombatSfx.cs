namespace Oniblade.Audio;

/// <summary>
/// 战斗音效的**逻辑名字**。音频层负责把它映射到实际音频文件与总线；
/// 战斗代码只说"我发生了弹开"，不关心用的是哪个 .ogg。
/// </summary>
public enum CombatSfx
{
    HitSlash,
    HitBlock,
    Deflect,
    Clash,
    GuardBreak,
    IssenSlash,
    IssenImpact,
    Deathblow,
    WhooshLight,
    WhooshHeavy,
    DodgeWhoosh,
    PerilousThrust,
    PerilousSweep,
    PerilousGrab,
}
