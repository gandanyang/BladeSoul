#!/usr/bin/env pwsh
# Generates the 14 placeholder combat SFX as 16-bit mono 44.1kHz WAV files.
# Fully procedural and reproducible: same seed -> same bytes.
#
#   powershell -NoProfile -File tools\gen_placeholder_sfx.ps1
#
# These are PLACEHOLDERS (07 doc: "占位先行，美术/音频绝不阻塞玩法").
# Replace them with real assets later; docs/CREDITS.md records their status.
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 as ANSI
# unless it has a UTF-8 BOM, which mangles non-ASCII string literals.

$ErrorActionPreference = 'Stop'

$SampleRate = 44100
$Root = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $Root 'assets\audio\sfx'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-Buffer([double]$Seconds) {
    return New-Object 'double[]' ([int]($Seconds * $SampleRate))
}

# Exponentially decaying sine. Decay is an exponent per second: exp(-t*Decay).
function Add-Partial {
    param(
        [double[]]$Buf,
        [double]$Freq,
        [double]$Amp,
        [double]$Decay,
        [double]$Phase = 0.0,
        [double]$Delay = 0.0
    )
    $w = 2.0 * [Math]::PI * $Freq
    for ($i = 0; $i -lt $Buf.Length; $i++) {
        $t = $i / $SampleRate
        if ($t -lt $Delay) { continue }
        $u = $t - $Delay
        $Buf[$i] += $Amp * [Math]::Exp(-$u * $Decay) * [Math]::Sin($w * $u + $Phase)
    }
}

# One-pole low-passed noise with a fast attack and exponential decay.
function Add-Noise {
    param(
        [double[]]$Buf,
        [double]$Amp,
        [double]$Decay,
        [System.Random]$Rng,
        [double]$LowPass = 1.0,
        [double]$Attack = 0.001,
        [double]$Delay = 0.0
    )
    $prev = 0.0
    for ($i = 0; $i -lt $Buf.Length; $i++) {
        $t = $i / $SampleRate
        if ($t -lt $Delay) { continue }
        $u = $t - $Delay
        $env = if ($u -lt $Attack) { $u / [Math]::Max($Attack, 0.00001) } else { [Math]::Exp(-($u - $Attack) * $Decay) }
        $prev = $prev + $LowPass * (($Rng.NextDouble() * 2.0 - 1.0) - $prev)
        $Buf[$i] += $Amp * $env * $prev
    }
}

# Swelling band of noise - the "whoosh" shape (no tonal content at all).
function Add-Swell {
    param(
        [double[]]$Buf,
        [double]$Amp,
        [System.Random]$Rng,
        [double]$LowPass = 0.5,
        [double]$PeakAt = 0.4
    )
    $prev = 0.0
    $len = $Buf.Length
    for ($i = 0; $i -lt $len; $i++) {
        $x = $i / [double]$len
        $env = if ($x -lt $PeakAt) { [Math]::Pow($x / $PeakAt, 1.6) } else { [Math]::Pow(1.0 - (($x - $PeakAt) / (1.0 - $PeakAt)), 1.2) }
        $prev = $prev + $LowPass * (($Rng.NextDouble() * 2.0 - 1.0) - $prev)
        $Buf[$i] += $Amp * $env * $prev
    }
}

function Limit-Peak {
    param([double[]]$Buf, [double]$Peak = 0.88)
    $max = 0.0
    foreach ($v in $Buf) {
        $a = [Math]::Abs($v)
        if ($a -gt $max) { $max = $a }
    }
    if ($max -le 0.000001) { return }
    $k = $Peak / $max
    for ($i = 0; $i -lt $Buf.Length; $i++) { $Buf[$i] = $Buf[$i] * $k }
}

function Save-Wav {
    param([double[]]$Buf, [string]$Path)
    $dataSize = $Buf.Length * 2
    $stream = [System.IO.File]::Create($Path)
    $w = New-Object System.IO.BinaryWriter($stream)
    try {
        $w.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $w.Write([uint32](36 + $dataSize))
        $w.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $w.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $w.Write([uint32]16)
        $w.Write([uint16]1)                    # PCM
        $w.Write([uint16]1)                    # mono
        $w.Write([uint32]$SampleRate)
        $w.Write([uint32]($SampleRate * 2))    # byte rate
        $w.Write([uint16]2)                    # block align
        $w.Write([uint16]16)                   # bits per sample
        $w.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $w.Write([uint32]$dataSize)

        $bytes = New-Object 'byte[]' $dataSize
        for ($i = 0; $i -lt $Buf.Length; $i++) {
            $s = [Math]::Max(-1.0, [Math]::Min(1.0, $Buf[$i]))
            $v = [int][Math]::Round($s * 32767.0)
            if ($v -lt 0) { $v += 65536 }
            $bytes[$i * 2] = [byte]($v -band 0xFF)
            $bytes[$i * 2 + 1] = [byte](($v -shr 8) -band 0xFF)
        }
        $w.Write($bytes)
    }
    finally {
        $w.Close()
        $stream.Dispose()
    }
}

# ---------------------------------------------------------------- sounds ----

# The single most important sound in the game: the metal "ting" of a deflect.
# Inharmonic partials (not a harmonic series) is what makes metal sound like metal.
function Build-Deflect {
    $b = New-Buffer 0.75
    $rng = New-Object System.Random 11
    Add-Noise -Buf $b -Amp 0.55 -Decay 140.0 -Rng $rng -LowPass 1.0 -Attack 0.0004
    Add-Partial -Buf $b -Freq 2489 -Amp 0.95 -Decay 8.0
    Add-Partial -Buf $b -Freq 3721 -Amp 0.62 -Decay 10.0
    Add-Partial -Buf $b -Freq 5141 -Amp 0.45 -Decay 12.5
    Add-Partial -Buf $b -Freq 6893 -Amp 0.30 -Decay 16.0
    Add-Partial -Buf $b -Freq 9137 -Amp 0.20 -Decay 21.0
    Limit-Peak $b 0.92
    return $b
}

function Build-Clash {
    $b = New-Buffer 0.85
    $rng = New-Object System.Random 22
    Add-Noise -Buf $b -Amp 0.5 -Decay 22.0 -Rng $rng -LowPass 0.55 -Attack 0.0006
    Add-Partial -Buf $b -Freq 183 -Amp 0.95 -Decay 6.5
    Add-Partial -Buf $b -Freq 327 -Amp 0.60 -Decay 8.0
    Add-Partial -Buf $b -Freq 547 -Amp 0.42 -Decay 11.0
    Add-Partial -Buf $b -Freq 1204 -Amp 0.25 -Decay 15.0
    Limit-Peak $b 0.9
    return $b
}

function Build-HitSlash {
    $b = New-Buffer 0.28
    $rng = New-Object System.Random 33
    Add-Noise -Buf $b -Amp 1.0 -Decay 42.0 -Rng $rng -LowPass 0.92 -Attack 0.0004
    Add-Partial -Buf $b -Freq 420 -Amp 0.35 -Decay 40.0
    Add-Partial -Buf $b -Freq 1470 -Amp 0.18 -Decay 55.0
    Limit-Peak $b 0.9
    return $b
}

function Build-HitBlock {
    $b = New-Buffer 0.34
    $rng = New-Object System.Random 44
    Add-Noise -Buf $b -Amp 0.6 -Decay 26.0 -Rng $rng -LowPass 0.30 -Attack 0.0008
    Add-Partial -Buf $b -Freq 118 -Amp 0.95 -Decay 16.0
    Add-Partial -Buf $b -Freq 236 -Amp 0.40 -Decay 20.0
    Limit-Peak $b 0.88
    return $b
}

function Build-GuardBreak {
    $b = New-Buffer 0.6
    $rng = New-Object System.Random 55
    Add-Noise -Buf $b -Amp 0.75 -Decay 30.0 -Rng $rng -LowPass 0.85 -Attack 0.0004
    # Bone/splinter cracks: a few discrete snaps on top of the burst.
    Add-Noise -Buf $b -Amp 0.7 -Decay 70.0 -Rng $rng -LowPass 0.95 -Attack 0.0003 -Delay 0.045
    Add-Noise -Buf $b -Amp 0.6 -Decay 80.0 -Rng $rng -LowPass 0.95 -Attack 0.0003 -Delay 0.098
    Add-Partial -Buf $b -Freq 96 -Amp 0.7 -Decay 11.0
    Limit-Peak $b 0.92
    return $b
}

function Build-IssenSlash {
    $b = New-Buffer 0.3
    $rng = New-Object System.Random 66
    Add-Noise -Buf $b -Amp 1.0 -Decay 55.0 -Rng $rng -LowPass 1.0 -Attack 0.0002
    Add-Partial -Buf $b -Freq 3120 -Amp 0.35 -Decay 40.0
    Add-Partial -Buf $b -Freq 5250 -Amp 0.2 -Decay 50.0
    Limit-Peak $b 0.95
    return $b
}

function Build-IssenImpact {
    $b = New-Buffer 0.8
    $rng = New-Object System.Random 77
    Add-Noise -Buf $b -Amp 0.55 -Decay 9.0 -Rng $rng -LowPass 0.22 -Attack 0.002
    Add-Partial -Buf $b -Freq 58 -Amp 1.0 -Decay 4.5
    Add-Partial -Buf $b -Freq 87 -Amp 0.5 -Decay 6.0
    Add-Partial -Buf $b -Freq 131 -Amp 0.28 -Decay 8.0
    Limit-Peak $b 0.95
    return $b
}

function Build-Deathblow {
    $b = New-Buffer 0.85
    $rng = New-Object System.Random 88
    Add-Noise -Buf $b -Amp 0.5 -Decay 30.0 -Rng $rng -LowPass 0.8 -Attack 0.0004
    Add-Partial -Buf $b -Freq 72 -Amp 1.0 -Decay 5.0
    Add-Partial -Buf $b -Freq 149 -Amp 0.45 -Decay 7.0
    Add-Partial -Buf $b -Freq 1830 -Amp 0.16 -Decay 12.0 -Delay 0.02
    Limit-Peak $b 0.92
    return $b
}

function Build-WhooshLight {
    $b = New-Buffer 0.26
    $rng = New-Object System.Random 99
    Add-Swell -Buf $b -Amp 0.9 -Rng $rng -LowPass 0.75 -PeakAt 0.45
    Limit-Peak $b 0.7
    return $b
}

function Build-WhooshHeavy {
    $b = New-Buffer 0.46
    $rng = New-Object System.Random 111
    Add-Swell -Buf $b -Amp 0.9 -Rng $rng -LowPass 0.35 -PeakAt 0.5
    Limit-Peak $b 0.78
    return $b
}

function Build-DodgeWhoosh {
    $b = New-Buffer 0.24
    $rng = New-Object System.Random 122
    Add-Swell -Buf $b -Amp 0.85 -Rng $rng -LowPass 0.9 -PeakAt 0.32
    Limit-Peak $b 0.62
    return $b
}

# The three "perilous" cues differ ONLY in pitch (07 doc 2.2: pitch is the
# dimension people can actually tell apart, not volume).
function Build-Perilous {
    param([double]$BaseFreq, [int]$Seed)
    $b = New-Buffer 0.42
    $rng = New-Object System.Random $Seed
    Add-Partial -Buf $b -Freq $BaseFreq -Amp 1.0 -Decay 7.0
    Add-Partial -Buf $b -Freq ($BaseFreq * 2.0) -Amp 0.45 -Decay 10.0
    Add-Partial -Buf $b -Freq ($BaseFreq * 3.0) -Amp 0.2 -Decay 14.0
    Add-Noise -Buf $b -Amp 0.18 -Decay 60.0 -Rng $rng -LowPass 0.5 -Attack 0.0005
    Limit-Peak $b 0.86
    return $b
}

# Soul absorb: a dark "pulled in" tone. Pitch is raised by code per chain
# count, so this file only provides the base note.
function Build-SoulAbsorb {
    $b = New-Buffer 0.40
    $rng = New-Object System.Random 301
    Add-Noise -Buf $b -Amp 0.30 -Decay 12.0 -Rng $rng -LowPass 0.35 -Attack 0.004
    Add-Partial -Buf $b -Freq 330 -Amp 0.55 -Decay 8.0
    Add-Partial -Buf $b -Freq 495 -Amp 0.30 -Decay 10.0
    Add-Partial -Buf $b -Freq 742 -Amp 0.16 -Decay 14.0
    Limit-Peak $b 0.72
    return $b
}

# T53: deflect feedback by damage type. Build-Deflect above is the SLASH ring
# (inharmonic partials = metal). The other three keep a clang identity but move
# the spectral weight: blunt sinks low and muddies, thrust goes high and very
# short, dark drops to a hollow low thud.
function Build-DeflectBlunt {
    $b = New-Buffer 0.70
    $rng = New-Object System.Random 401
    Add-Noise -Buf $b -Amp 0.62 -Decay 40.0 -Rng $rng -LowPass 0.32 -Attack 0.0008
    Add-Partial -Buf $b -Freq 196 -Amp 1.00 -Decay 9.0
    Add-Partial -Buf $b -Freq 337 -Amp 0.55 -Decay 11.0
    Add-Partial -Buf $b -Freq 704 -Amp 0.30 -Decay 14.0
    Add-Partial -Buf $b -Freq 1183 -Amp 0.16 -Decay 18.0
    Limit-Peak $b 0.92
    return $b
}

function Build-DeflectThrust {
    $b = New-Buffer 0.34
    $rng = New-Object System.Random 402
    Add-Noise -Buf $b -Amp 0.50 -Decay 190.0 -Rng $rng -LowPass 1.0 -Attack 0.0002
    Add-Partial -Buf $b -Freq 4180 -Amp 0.90 -Decay 22.0
    Add-Partial -Buf $b -Freq 6315 -Amp 0.55 -Decay 30.0
    Add-Partial -Buf $b -Freq 9027 -Amp 0.34 -Decay 42.0
    Add-Partial -Buf $b -Freq 12154 -Amp 0.20 -Decay 55.0
    Limit-Peak $b 0.90
    return $b
}

function Build-DeflectDark {
    $b = New-Buffer 0.80
    $rng = New-Object System.Random 403
    Add-Noise -Buf $b -Amp 0.45 -Decay 11.0 -Rng $rng -LowPass 0.20 -Attack 0.003
    Add-Partial -Buf $b -Freq 92 -Amp 1.00 -Decay 5.5
    Add-Partial -Buf $b -Freq 141 -Amp 0.50 -Decay 7.5
    Add-Partial -Buf $b -Freq 258 -Amp 0.26 -Decay 10.0
    Limit-Peak $b 0.90
    return $b
}

$sounds = [ordered]@{
    'hit_slash'       = (Build-HitSlash)
    'hit_block'       = (Build-HitBlock)
    'deflect'         = (Build-Deflect)
    'clash'           = (Build-Clash)
    'guard_break'     = (Build-GuardBreak)
    'issen_slash'     = (Build-IssenSlash)
    'issen_impact'    = (Build-IssenImpact)
    'deathblow'       = (Build-Deathblow)
    'whoosh_light'    = (Build-WhooshLight)
    'whoosh_heavy'    = (Build-WhooshHeavy)
    'dodge_whoosh'    = (Build-DodgeWhoosh)
    'perilous_thrust' = (Build-Perilous -BaseFreq 1560 -Seed 201)
    'perilous_sweep'  = (Build-Perilous -BaseFreq 980  -Seed 202)
    'perilous_grab'   = (Build-Perilous -BaseFreq 620  -Seed 203)
    'soul_absorb'     = (Build-SoulAbsorb)
    'deflect_blunt'   = (Build-DeflectBlunt)
    'deflect_thrust'  = (Build-DeflectThrust)
    'deflect_dark'    = (Build-DeflectDark)
}

$total = 0
foreach ($name in $sounds.Keys) {
    $path = Join-Path $OutDir ("sfx_{0}.wav" -f $name)
    Save-Wav -Buf $sounds[$name] -Path $path
    $size = (Get-Item -LiteralPath $path).Length
    $total += $size
    Write-Host ("  {0,-18} {1,7} bytes" -f "sfx_$name.wav", $size)
}

Write-Host ("Generated {0} files, {1:N0} bytes total -> {2}" -f $sounds.Count, $total, $OutDir)
