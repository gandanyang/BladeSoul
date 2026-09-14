# check_async_void.ps1 —— 棘轮：禁止新增 `async void` 承载可失败流程
#
# 为什么立这条（项目已撞三次同类问题）：
#   `async void` 里的异常**不会传播给任何人**——它被丢进 SynchronizationContext，
#   在 Godot 里表现为某次 FrameCallback 打一行 ERROR 就没了。于是：
#
#     · T49/T50：755 次 ObjectDisposedException —— 异常被吞，日志里堆了 2 万行没人看见
#     · T52：EnemyDeathblowTest 在 `QueueFree()` 一个已释放敌人时炸掉，
#             整个 _Ready 静默中断 → "测试跑到一半不动、零错误输出、最后超时"
#
#   这三次都不是"某张卡的 bug"，而是**同一个工程级陷阱**。
#   所以它现在像死配置一样，属于代码审查红旗。
#
# 正确写法（Godot 生命周期方法必须是 void，所以要在方法内部兜住）：
#
#     public override void _Ready() => _ = RunAsync();
#
#     private async Task RunAsync()
#     {
#         try { ...全部 await...; Report(); }
#         catch (Exception ex) { GD.PrintErr($"[xx] ✗ 未捕获异常：{ex}"); GetTree().Quit(1); }
#     }
#
# 本脚本只做**棘轮**（不许新增），不强制立刻改完既有的 29 处——
# 一次性重写 29 个测试文件的收益低于风险，逐步迁移即可。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 扫描范围：逻辑层 + 测试层
$dirs = @('src', 'tests')

# 命中判定：访问修饰符（可带 override / partial / static / virtual / sealed 等）之后紧跟 async void
$pattern = '(public|private|protected|internal)[\w\s]*\basync\s+void\b'

$hits = New-Object System.Collections.Generic.List[object]

foreach ($dir in $dirs) {
    $path = Join-Path $root $dir
    if (-not (Test-Path -LiteralPath $path)) { continue }

    Get-ChildItem -LiteralPath $path -Recurse -Filter *.cs -File | ForEach-Object {
        $file = $_
        $lineNo = 0
        foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
            $lineNo++

            # ★ 必须排除注释行。否则解释"为什么禁 async void"的注释本身会被算成违规，
            #   而这条规则的全部价值就在于**能被写进注释解释**。
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')) {
                continue
            }

            if ($line -match $pattern) {
                $rel = $file.FullName.Substring($root.Length + 1) -replace '\\', '/'
                $hits.Add([pscustomobject]@{
                    File = $rel
                    Line = $lineNo
                    Text = $line.Trim()
                })
            }
        }
    }
}

# 生产代码（src/）**零容忍**：那里没有"测试挂住"这种灰色失败，只有真 bug。
# 测试/dev（src/Dev/、tests/）允许存在基线，但不许新增。
#
# ★ 用 `@(...)` 强制成数组：`Where-Object` 只返回一个对象时结果是**标量**，
#   标量的 `.Count` 在 PS 5.1 里会是 $null（不是 1），`-f` 格式化出来是空字符串——
#   于是"有 1 处"会被印成"生产  处"，看起来像什么都没扫到。
$prodHits = @($hits | Where-Object { $_.File -like 'src/*' -and $_.File -notlike 'src/Dev/*' })
$testHits = @($hits | Where-Object { $_.File -like 'tests/*' -or $_.File -like 'src/Dev/*' })

# 生产基线 = 0：唯一那处（AudioDirector.PlayIssenSequence）已经改成 `async Task` + try/catch。
# 它当初不是"风格问题"而是真 bug —— 一闪那一击若异常，会变成
# "切割声已响、轰鸣永远不来、日志里什么都没有"，玩家只听到半截音效。
#
# 测试/dev 基线 28：一次性重写 28 个测试文件的收益低于风险（回归面太大），
# 逐步迁移到 `void _Ready() => _ = RunAsync()` 即可，每迁一个就把基线减一。
$prodBaseline = 0
$testBaseline = 28

Write-Host ("[async void] 扫描 src/ + tests/：生产 {0} 处（基线 {1}）/ 测试·dev {2} 处（基线 {3}）" -f `
    $prodHits.Count, $prodBaseline, $testHits.Count, $testBaseline)

$failed = $false

if ($prodHits.Count -gt $prodBaseline) {
    Write-Host "[async void] X 生产代码新增 async void（零容忍）：" -ForegroundColor Red
    $prodHits | ForEach-Object { Write-Host ("           - {0}:{1}  {2}" -f $_.File, $_.Line, $_.Text) }
    $failed = $true
}

if ($testHits.Count -gt $testBaseline) {
    Write-Host "[async void] X 新增 async void（测试/dev 也不许新增）：" -ForegroundColor Red
    $testHits | ForEach-Object { Write-Host ("           - {0}:{1}  {2}" -f $_.File, $_.Line, $_.Text) }
    Write-Host "           改法：public override void _Ready() => _ = RunAsync();" -ForegroundColor Yellow
    Write-Host "                 并在 RunAsync() 里 try/catch，catch 中 PrintErr + Quit(1)" -ForegroundColor Yellow
    $failed = $true
}

if ($failed) { exit 1 }

# 数量下降是好事，提示可以收紧基线
if ($prodHits.Count -lt $prodBaseline -or $testHits.Count -lt $testBaseline) {
    Write-Host "  OK 数量低于基线，可把基线收紧到 $($prodHits.Count) / $($testHits.Count)" -ForegroundColor Yellow
}

Write-Host "  OK 没有新增"
exit 0
