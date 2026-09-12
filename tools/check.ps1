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

    Write-Host '--- 1/4 build ---' -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

    Write-Host '--- 2/4 unit tests (pure logic) ---' -ForegroundColor Cyan
    dotnet test tests\Oniblade.Tests\Oniblade.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host '--- 3/4 resource self-test (headless engine) ---' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $godot)) {
        Write-Warning "Godot not found: $godot (resource self-test skipped)"
    }
    else {
        & $godot --headless --path $root res://scenes/tests/SelfTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "resource self-test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 4/4 combat smoke test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatSmoke.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat smoke test failed (exit $LASTEXITCODE)" }
    }

    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
}
finally {
    Pop-Location
}
