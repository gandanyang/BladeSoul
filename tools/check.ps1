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
        Write-Host '--- 0/30 import new assets ---' -ForegroundColor Yellow
        & $godot --headless --path $root --import | Out-Null
    }

    Write-Host '--- 1/30 build ---' -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

    Write-Host '--- 2/30 unit tests (pure logic) ---' -ForegroundColor Cyan
    dotnet test tests\Oniblade.Tests\Oniblade.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host '--- 3/30 resource self-test (headless engine) ---' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $godot)) {
        Write-Warning "Godot not found: $godot (resource self-test skipped)"
    }
    else {
        & $godot --headless --path $root res://scenes/tests/SelfTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "resource self-test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 4/30 combat smoke test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatSmoke.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat smoke test failed (exit $LASTEXITCODE)" }

        # T6/T7：会还手的假人 + 弹开窗，端到端跑一遍（480 帧 ≈ 4 次攻防）。
        Write-Host '--- 5/30 deflect training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DeflectTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "deflect training test failed (exit $LASTEXITCODE)" }

        # T10：同一套链路，把机器人换成"完全不看时机的连打"。
        # 期望拿不到弹开收益，但也一次都不挨打（惩罚只惩罚效率，不惩罚存活）。
        Write-Host '--- 6/30 guard-spam contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/GuardSpam.tscn
        if ($LASTEXITCODE -ne 0) { throw "guard-spam contrast failed (exit $LASTEXITCODE)" }

        # T13：闪避无敌帧。机器人在判定帧前一帧按下闪避，
        # 期望 8 帧无敌完整盖住 4 帧判定 → 全 Miss、不掉血、拿到避一闪 buff。
        Write-Host '--- 7/30 dodge training test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTraining.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge training test failed (exit $LASTEXITCODE)" }

        # T13 对照组：同一套链路，闪早了（提前 12 帧 > 8 帧无敌）。
        # 期望无敌帧在刀落下前结束 → 必须挨打。没有这一步，
        # "无敌帧生效"和"无敌帧永远开着"是分不出来的。
        Write-Host '--- 8/30 dodge-too-early contrast (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/DodgeTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "dodge-too-early contrast failed (exit $LASTEXITCODE)" }

        # T12：这是一对**同条件**对照实验（同一套机器人、同一帧数、同一距离与间隔），
        # 唯一差别是靶子指向的 AttackData。它是 02 §3「一闪应对一切、弹开只应对一般攻击」
        # 那条裁定的可执行证明：一般攻击弹得开（4/0/0），危攻击弹不开但挡得住（0/4/0）。
        Write-Host '--- 9/30 perilous baseline: normal slash (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousGuard.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous baseline (normal slash) failed (exit $LASTEXITCODE)" }

        Write-Host '--- 10/30 perilous contrast: threat thrust (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PerilousThrust.tscn
        if ($LASTEXITCODE -ne 0) { throw "perilous contrast (threat thrust) failed (exit $LASTEXITCODE)" }

        # T18：喝血的"不背板但要付代价"。刀必须落在**饮用段**里，
        # 三条断言缺一不可：回血生效 / 动作没被打断 / 伤害照常扣。
        Write-Host '--- 11/30 heal mid-drink test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/HealMidDrink.tscn
        if ($LASTEXITCODE -ne 0) { throw "heal mid-drink test failed (exit $LASTEXITCODE)" }

        # T14：死亡与原地重开。核心输出是**实测帧数**（不许估），
        # 并验证敌人侧的重生计时也被复位（否则重开后假人会卡在死亡等待里）。
        Write-Host '--- 12/30 battle reset test (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/BattleReset.tscn
        if ($LASTEXITCODE -ne 0) { throw "battle reset test failed (exit $LASTEXITCODE)" }

        # T20：一闪。四个场景 = 一组**同条件对照实验**（同一套机器人、同一帧数、
        # 同一距离与敌人招式），唯一差别是按攻击的那一帧：
        #   窗口内 4 次一闪 / 安全窗 5 次格挡且不挨打 / 太早与太晚各挨 5 次。
        # 安全窗单独一个场景，因为它是防劝退核心（按早了不挨打），不能混在能打出闪里过。
        Write-Host '--- 13/30 issen: in window (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenInWindow.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen in-window test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 14/30 issen: safe window (防劝退核心) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenSafeWindow.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen safe-window test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 15/30 issen: too early (whiff) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenTooEarly.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen too-early test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 16/30 issen: too late (whiff) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IssenTooLate.tscn
        if ($LASTEXITCODE -ne 0) { throw "issen too-late test failed (exit $LASTEXITCODE)" }

        # T23：ActorId 唯一性。手工填 id 已经**静默撞号两次**（道场两个 100；
        # SpearDummy 与 RespawnDummy 都是 102），后果不是崩溃而是同帧结算顺序不可复现。
        # 这一步的价值不在今天是对的，而在于**以后会替我们抓住第三次**。
        Write-Host '--- 17/30 actor id uniqueness (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ActorIdUniqueness.tscn
        if ($LASTEXITCODE -ne 0) { throw "actor id uniqueness failed (exit $LASTEXITCODE)" }

        # T22：复活。连杀 ExpectedRevives 次都该当场站起来，之后那次才交给 T14 的原地重开。
        # 两个难度档各跑一遍：武士 1 次、見習 3 次 —— 这同时证明复活次数来自难度档，
        # 而不是代码里写死的 1。
        Write-Host '--- 18/30 revive: samurai (1 revive) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ReviveTest.tscn
        if ($LASTEXITCODE -ne 0) { throw "revive test (samurai) failed (exit $LASTEXITCODE)" }

        Write-Host '--- 19/30 revive: migoto (3 revives) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ReviveMigoto.tscn
        if ($LASTEXITCODE -ne 0) { throw "revive test (migoto) failed (exit $LASTEXITCODE)" }

        # T28：战斗特效。卡片要的四张截图做不到（无渲染输出），但可验证的硬指标全在这里：
        # 四种 Verdict 各生成对应特效、**弹开火花 ≤8 帧内消失**（10 §4 第一原则：不许盖住判定）、
        # 一闪全屏闪 6 帧、特效层不许碰战斗逻辑、降级顺序是先砍雾再砍粒子。
        Write-Host '--- 20/30 combat vfx (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/CombatVfx.tscn
        if ($LASTEXITCODE -ne 0) { throw "combat vfx test failed (exit $LASTEXITCODE)" }

        # T30：同伴对话系统。卡片要的真实对话跑起来的样子截图做不到（无渲染输出），
        # 所以这里走**真实系统**（autoload 的 DialogueBox，不是只测 POCO）并逐句打出玩家会看到的东西：
        # 80/20 纪律、数据自净、一组对话能推完、以及★侵蚀三档的台词确实不同。
        Write-Host '--- 21/30 companion dialogue (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/Dialogue.tscn
        if ($LASTEXITCODE -ne 0) { throw "dialogue test failed (exit $LASTEXITCODE)" }

        # T32：关卡白盒。这是**唯一**能自动抓住关卡走不通的东西——
        # 它在第一版白盒上抓到了三个真实缺陷：自检路径压在柱子上、
        # 遭遇战 B 没有出口、BOSS 房四面全封。三个都会让玩家卡死。
        # 顺便从场景几何算出俯视平面图（不是截屏，白盒看平面图比看透视更好用）。
        Write-Host '--- 22/30 whitebox: dojo (walkability + clearance) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/LevelWhiteboxDojo.tscn
        if ($LASTEXITCODE -ne 0) { throw "whitebox dojo test failed (exit $LASTEXITCODE)" }

        Write-Host '--- 23/30 whitebox: gifu chapter 1 ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/LevelWhiteboxGifu.tscn
        if ($LASTEXITCODE -ne 0) { throw "whitebox gifu test failed (exit $LASTEXITCODE)" }

        # T34：氛围与灯光。截图与帧率实测都要有显示的环境，所以这里验的是**能自动化的部分**：
        # 档位落到 WorldEnvironment、雨跟着摄像机、灯笼是唯一暖色且受上限约束、
        # 降级顺序（① 雾 → ② 粒子）、以及雾的透光率（读招保底，代理量）。
        Write-Host '--- 24/30 atmosphere: rain/fog/lanterns (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/Atmosphere.tscn
        if ($LASTEXITCODE -ne 0) { throw "atmosphere test failed (exit $LASTEXITCODE)" }

        # T34 的帧率基准。**无头下它只打印警告并退出 0**——dummy renderer 不渲染，
        # 数字不可信。这一步只是为了场景能不能起来的冒烟；
        # 真正的 60fps 实测请在编辑器里 F6 跑 AtmosphereBenchmark.tscn。
        Write-Host '--- 25/30 atmosphere benchmark smoke (headless, 数字不可信) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/AtmosphereBenchmark.tscn
        if ($LASTEXITCODE -ne 0) { throw "atmosphere benchmark failed (exit $LASTEXITCODE)" }

        # T31：战斗 HUD。它让**早就做完了的架势机制变得可读**——11 §4.3 说得很清楚：
        # 没有架势槽，弹开只是"打中了"；有了它，弹开才是"我在推进"。
        # 这里验四件事：玩家条可读（含体干回落）、架势槽有跳动且弹开增量更大、
        # 四种结算各触发提示、以及★HUD 只读（灌 Damage=999 战斗数值一个都不许变）。
        Write-Host '--- 26/30 combat hud (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/Hud.tscn
        if ($LASTEXITCODE -ne 0) { throw "hud test failed (exit $LASTEXITCODE)" }

        # T37：架势系统收口。三个缺口都是"定义了但没人读"那一类：
        # ① 难度档的玩家架势恢复旋钮（接上了；但量化把它吃掉，日志里有实测数字与修法证据）
        # ② 玩家破防的表现（姿态 + HUD 把 GuardBreak 与 Block 拆开 + 快满预警）
        # ③ 简单档的半自动防御（四组对照：見習一般攻击触发 / 危不触发 / 额度 3 次 / 其他档不触发）
        Write-Host '--- 27/30 stance closeout (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/Stance.tscn
        if ($LASTEXITCODE -ne 0) { throw "stance test failed (exit $LASTEXITCODE)" }

        # T40：室内照明。道场有天花板、四面墙、没有窗，太阳被挡死，环境光只有 0.3 ——
        # 房间最深处（魔骸站的那片）实测全黑。修法按卡片的优先级：
        # ① 屋顶真的开洞引天光（冷色，暖色仍然只属于灯笼）→ ② 室内灯笼锚点盖住战斗区
        # → ③ 室内外**光照分层**堵住穿墙假亮（灯笼不投影，所以只能靠 cull mask）。
        # 这条自检把"亮不亮"变成断言：战斗区四角 + 中心每个点都要有光；
        # 另外验室外灯笼照不进室内几何、天光与坏境光都是冷色。
        Write-Host '--- 28/30 indoor light (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/IndoorLight.tscn
        if ($LASTEXITCODE -ne 0) { throw "indoor light test failed (exit $LASTEXITCODE)" }

        # T45：掉落保护。玩家掉出世界时要"淡出 → 回安全点 → 淡入"，
        # 而且**不许误触发**（站在平台上、贴着边站着都不该被送回去）。
        # 这条自检同时充当"试玩缺口体检"：它还会打印动画覆盖表
        # （每个动作与 idle 的最大骨角差，0.0° = 根本没有专属动画）。
        Write-Host '--- 29/30 fall recovery + animation coverage gaps (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/PlayerGaps.tscn
        if ($LASTEXITCODE -ne 0) { throw "player gaps test failed (exit $LASTEXITCODE)" }

        # T46：重攻击（蓄力斩）。单测只证明"按住 34/48/62 帧该选哪一段"，
        # 证明不了那一记真的打出去了、伤害真的等于 .tres 里的数——
        # 这个场景量的是输入采集 → 状态切换 → 判定框 → 仲裁器那一整条链。
        Write-Host '--- 30/30 charged attack (headless engine) ---' -ForegroundColor Cyan
        & $godot --headless --path $root res://scenes/tests/ChargedAttack.tscn
        if ($LASTEXITCODE -ne 0) { throw "charged attack test failed (exit $LASTEXITCODE)" }
    }

    Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
}
finally {
    Pop-Location
}
