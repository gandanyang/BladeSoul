#!/usr/bin/env pwsh
# 一键验证：编译 + 纯逻辑单测 + 资源自检 + 无头端到端。
# 任何一步失败都会返回非 0 退出码。
#
#   powershell -File tools\check.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$godot = 'G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'

Push-Location $root
try {
    # Assets added outside the editor have no .import file, and then ResourceLoader
    # cannot see them (res:// paths won't resolve). Only pay for the import pass
    # when something is actually missing.
    $importExtensions = '.wav', '.ogg', '.mp3', '.png', '.jpg', '.jpeg', '.svg', '.glb', '.gltf', '.ttf', '.otf'
    $needsImport = Get-ChildItem -Path (Join-Path $root 'assets') -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $importExtensions -contains $_.Extension.ToLower() -and
                       -not (Test-Path -LiteralPath ($_.FullName + '.import')) }

    if ($needsImport) {
        Write-Host '--- 0/12 import new assets ---' -ForegroundColor Yellow
        & $godot --headless --path $root --import | Out-Null
    }

    Write-Host '--- 1/12 build ---' -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

    Write-Host '--- 2/12 unit tests (pure logic) ---' -ForegroundColor Cyan
    dotnet test tests\Oniblade.Tests\Oniblade.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host '--- 3/12 resource self-test (headless engine) ---' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $godot)) {
        Write-Warning "Godot not found: $godot (resource self-test skipped)"
    }
    else {
        & $godot --headless --path $root res://scenes/tests/SelfTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "resource self-test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 4/12 combat smoke test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatSmoke.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat smoke test failed (exit $LASTEXITCODE)" }

        # T6/T7：会还手的假人 + 弹开窗，端到端跑一遍（480 帧 ≈ 4 次攻防）。
        Write-Host '--- 5/12 deflect training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DeflectTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "deflect training test failed (exit $LASTEXITCODE)" }

        # T10：同一套链路，把机器人换成"完全不看时机的连打"。
        # 期望拿不到弹开收益，但也一次都不挨打（惩罚只惩罚效率，不惩罚存活）。
        Write-Host '--- 6/12 guard-spam contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/GuardSpam.tscn
        if ($LASTEXITCODE -ne 0) { throw "guard-spam contrast failed (exit $LASTEXITCODE)" }

        # T13：闪避无敌帧。机器人在判定帧前一帧按下闪避，
        # 期望 8 帧无敌完整盖住 4 帧判定 → 全 Miss、不掉血、拿到避一闪 buff。
        Write-Host '--- 7/12 dodge training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge training test failed (exit $LASTEXITCODE)" }

        # T13 对照组：同一套链路，闪早了（提前 12 帧 > 8 帧无敌）。
        # 期望无敌帧在刀落下前结束 → 必须挨打。没有这一步，
        # "无敌帧生效"和"无敌帧永远开着"是分不出来的。
        Write-Host '--- 8/12 dodge-too-early contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge-too-early contrast failed (exit $LASTEXITCODE)" }

        # T12：这是一对**同条件**对照实验（同一套机器人、同一帧数、同一距离与间隔），
        # 唯一差别是靶子指向的 AttackData。它是 02 §3「一闪应对一切、弹开只应对一般攻击」
        # 那条裁定的可执行证明：一般攻击弹得开（4/0/0），危攻击弹不开但挡得住（0/4/0）。
        Write-Host '--- 9/12 perilous baseline: normal slash (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousGuard.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous baseline (normal slash) failed (exit $LASTEXITCODE)" }

        Write-Host '--- 10/12 perilous contrast: threat thrust (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousThrust.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous contrast (threat thrust) failed (exit $LASTEXITCODE)" }

        # T18：喝血的"不背板但要付代价"。刀必须落在**饮用段**里，
        # 三条断言缺一不可：回血生效 / 动作没被打断 / 伤害照常扣。
        Write-Host '--- 11/12 heal mid-drink test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/HealMidDrink.tscn
        if ($LASTEXITCODE -ne 0) { throw "heal mid-drink test failed (exit $LASTEXITCODE)" }

        # T14：死亡与原地重开。核心输出是**实测帧数**（不许估），
        # 并验证敌人侧的重生计时也被复位（否则重开后假人会卡在死亡等待里）。
        Write-Host '--- 12/12 battle reset test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/BattleReset.tscn
        if ($LASTEXITCODE -ne 0) { throw "battle reset test failed (exit $LASTEXITCODE)" }
    }

    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
}
finally {
    Pop-Location
}
