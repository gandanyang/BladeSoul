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
        Write-Host '--- 0/23 import new assets ---' -ForegroundColor Yellow
        & $godot --headless --path $root --import | Out-Null
    }

    Write-Host '--- 1/23 build ---' -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

    Write-Host '--- 2/23 unit tests (pure logic) ---' -ForegroundColor Cyan
    dotnet test tests\Oniblade.Tests\Oniblade.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host '--- 3/23 resource self-test (headless engine) ---' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $godot)) {
        Write-Warning "Godot not found: $godot (resource self-test skipped)"
    }
    else {
        & $godot --headless --path $root res://scenes/tests/SelfTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "resource self-test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 4/23 combat smoke test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatSmoke.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat smoke test failed (exit $LASTEXITCODE)" }

        # T6/T7：会还手的假人 + 弹开窗，端到端跑一遍（480 帧 ≈ 4 次攻防）。
        Write-Host '--- 5/23 deflect training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DeflectTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "deflect training test failed (exit $LASTEXITCODE)" }

        # T10：同一套链路，把机器人换成"完全不看时机的连打"。
        # 期望拿不到弹开收益，但也一次都不挨打（惩罚只惩罚效率，不惩罚存活）。
        Write-Host '--- 6/23 guard-spam contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/GuardSpam.tscn
        if ($LASTEXITCODE -ne 0) { throw "guard-spam contrast failed (exit $LASTEXITCODE)" }

        # T13：闪避无敌帧。机器人在判定帧前一帧按下闪避，
        # 期望 8 帧无敌完整盖住 4 帧判定 → 全 Miss、不掉血、拿到避一闪 buff。
        Write-Host '--- 7/23 dodge training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge training test failed (exit $LASTEXITCODE)" }

        # T13 对照组：同一套链路，闪早了（提前 12 帧 > 8 帧无敌）。
        # 期望无敌帧在刀落下前结束 → 必须挨打。没有这一步，
        # "无敌帧生效"和"无敌帧永远开着"是分不出来的。
        Write-Host '--- 8/23 dodge-too-early contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge-too-early contrast failed (exit $LASTEXITCODE)" }

        # T12：这是一对**同条件**对照实验（同一套机器人、同一帧数、同一距离与间隔），
        # 唯一差别是靶子指向的 AttackData。它是 02 §3「一闪应对一切、弹开只应对一般攻击」
        # 那条裁定的可执行证明：一般攻击弹得开（4/0/0），危攻击弹不开但挡得住（0/4/0）。
        Write-Host '--- 9/23 perilous baseline: normal slash (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousGuard.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous baseline (normal slash) failed (exit $LASTEXITCODE)" }

        Write-Host '--- 10/23 perilous contrast: threat thrust (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousThrust.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous contrast (threat thrust) failed (exit $LASTEXITCODE)" }

        # T18：喝血的"不背板但要付代价"。刀必须落在**饮用段**里，
        # 三条断言缺一不可：回血生效 / 动作没被打断 / 伤害照常扣。
        Write-Host '--- 11/23 heal mid-drink test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/HealMidDrink.tscn
        if ($LASTEXITCODE -ne 0) { throw "heal mid-drink test failed (exit $LASTEXITCODE)" }

        # T14：死亡与原地重开。核心输出是**实测帧数**（不许估），
        # 并验证敌人侧的重生计时也被复位（否则重开后假人会卡在死亡等待里）。
        Write-Host '--- 12/23 battle reset test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/BattleReset.tscn
        if ($LASTEXITCODE -ne 0) { throw "battle reset test failed (exit $LASTEXITCODE)" }

        # T20：一闪。四个场景 = 一组**同条件对照实验**（同一套机器人、同一帧数、
        # 同一距离与敌人招式），唯一差别是按攻击的那一帧：
        #   窗口内 4 次一闪 / 安全窗 5 次格挡且不挨打 / 太早与太晚各挨 5 次。
        # 安全窗单独一个场景，因为它是防劝退核心（按早了不挨打），不能混在能打出闪里过。
        Write-Host '--- 13/23 issen: in window (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenInWindow.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen in-window test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 14/23 issen: safe window (防劝退核心) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenSafeWindow.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen safe-window test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 15/23 issen: too early (whiff) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen too-early test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 16/23 issen: too late (whiff) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenTooLate.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen too-late test failed (exit $LASTEXITCODE)" }

        # T23：ActorId 唯一性。手工填 id 已经**静默撞号两次**（道场两个 100；
        # SpearDummy 与 RespawnDummy 都是 102），后果不是崩溃而是同帧结算顺序不可复现。
        # 这一步的价值不在今天是对的，而在于**以后会替我们抓住第三次**。
        Write-Host '--- 17/23 actor id uniqueness (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ActorIdUniqueness.tscn
        if ($LASTEXITCODE -ne 0) { throw "actor id uniqueness failed (exit $LASTEXITCODE)" }

        # T22：复活。连杀 ExpectedRevives 次都该当场站起来，之后那次才交给 T14 的原地重开。
        # 两个难度档各跑一遍：武士 1 次、見習 3 次 —— 这同时证明复活次数来自难度档，
        # 而不是代码里写死的 1。
        Write-Host '--- 18/23 revive: samurai (1 revive) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ReviveTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "revive test (samurai) failed (exit $LASTEXITCODE)" }

        Write-Host '--- 19/23 revive: migoto (3 revives) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ReviveMigoto.tscn
        if ($LASTEXITCODE -ne 0) { throw "revive test (migoto) failed (exit $LASTEXITCODE)" }

        # T28：战斗特效。卡片要的四张截图做不到（无渲染输出），但可验证的硬指标全在这里：
        # 四种 Verdict 各生成对应特效、**弹开火花 ≤8 帧内消失**（10 §4 第一原则：不许盖住判定）、
        # 一闪全屏闪 6 帧、特效层不许碰战斗逻辑、降级顺序是先砍雾再砍粒子。
        Write-Host '--- 20/23 combat vfx (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatVfx.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat vfx test failed (exit $LASTEXITCODE)" }

        # T30：同伴对话系统。卡片要的真实对话跑起来的样子截图做不到（无渲染输出），
        # 所以这里走**真实系统**（autoload 的 DialogueBox，不是只测 POCO）并逐句打出玩家会看到的东西：
        # 80/20 纪律、数据自净、一组对话能推完、以及★侵蚀三档的台词确实不同。
        Write-Host '--- 21/23 companion dialogue (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/Dialogue.tscn
        if ($LASTEXITCODE -ne 0) { throw "dialogue test failed (exit $LASTEXITCODE)" }

        # T32：关卡白盒。这是**唯一**能自动抓住关卡走不通的东西——
        # 它在第一版白盒上抓到了三个真实缺陷：自检路径压在柱子上、
        # 遭遇战 B 没有出口、BOSS 房四面全封。三个都会让玩家卡死。
        # 顺便从场景几何算出俯视平面图（不是截屏，白盒看平面图比看透视更好用）。
        Write-Host '--- 22/23 whitebox: dojo (walkability + clearance) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/LevelWhiteboxDojo.tscn
        if ($LASTEXITCODE -ne 0) { throw "whitebox dojo test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 23/23 whitebox: gifu chapter 1 ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/LevelWhiteboxGifu.tscn
        if ($LASTEXITCODE -ne 0) { throw "whitebox gifu test failed (exit $LASTEXITCODE)" }
    }

    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
}
finally {
    Pop-Location
}
