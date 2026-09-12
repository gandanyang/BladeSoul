#!/usr/bin/env pwsh
# 一键验证：编译 + 纯逻辑单测 + 资源自检。
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
        Write-Host '--- 0/4 import new assets ---' -ForegroundColor Yellow
        & $godot --headless --path $root --import | Out-Null
    }

    Write-Host '--- 1/8 build ---' -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

    Write-Host '--- 2/8 unit tests (pure logic) ---' -ForegroundColor Cyan
    dotnet test tests\Oniblade.Tests\Oniblade.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host '--- 3/8 resource self-test (headless engine) ---' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $godot)) {
        Write-Warning "Godot not found: $godot (resource self-test skipped)"
    }
    else {
        & $godot --headless --path $root res://scenes/tests/SelfTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "resource self-test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 4/8 combat smoke test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatSmoke.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat smoke test failed (exit $LASTEXITCODE)" }

        # T6/T7：会还手的假人 + 弹开窗，端到端跑一遍（480 帧 ≈ 4 次攻防）。
        Write-Host '--- 5/8 deflect training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DeflectTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "deflect training test failed (exit $LASTEXITCODE)" }

        # T10：同一套链路，把机器人换成"完全不看时机的连打"。
        # 期望拿不到弹开收益，但也一次都不挨打（惩罚只惩罚效率，不惩罚存活）。
        Write-Host '--- 6/8 guard-spam contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/GuardSpam.tscn
        if ($LASTEXITCODE -ne 0) { throw "guard-spam contrast failed (exit $LASTEXITCODE)" }

        # T13：闪避无敌帧。机器人在判定帧前一帧按下闪避，
        # 期望 8 帧无敌完整盖住 4 帧判定 → 全 Miss、不掉血、拿到避一闪 buff。
        Write-Host '--- 7/8 dodge training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge training test failed (exit $LASTEXITCODE)" }

        # T13 对照组：同一套链路，闪早了（提前 12 帧 > 8 帧无敌）。
        # 期望无敌帧在刀落下前结束 → 必须挨打。没有这一步，
        # "无敌帧生效"和"无敌帧永远开着"是分不出来的。
        Write-Host '--- 8/8 dodge-too-early contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge-too-early contrast failed (exit $LASTEXITCODE)" }
    }

    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
}
finally {
    Pop-Location
}
