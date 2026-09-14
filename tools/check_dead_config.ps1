# "定义了但没人读"的棘轮（P0 清理项）。
#
# 为什么要有它：这个项目已经栽过五次同一类问题——
# PerfectDodgeGraceFrames / ReviveCount / PlayerPostureRegenScale / HalfAutoGuard，
# 以及"重攻击"的 Charged1~3。共同点是：**数据层一切正常、代码里一次都没读**，
# 所以不会报任何错，只会悄悄和设计分家（蓄力斩的位移与破防标记就是这么漂的）。
#
# 工作方式：扫描 src/ 里所有 [Export] 字段，统计**声明处之外**的代码引用（注释不算）。
# 已知的记进 tools/dead_config_baseline.txt 作为基线——基线里允许存在，
# **基线之外一律报错**。修好一个就从基线删一行。
#
# 用法：powershell -NoProfile -File tools\check_dead_config.ps1

$ErrorActionPreference = 'Stop'

# ★ 先把控制台输出编码设成 UTF-8，否则下面 rg 的输出会被 PS 5.1 按 ANSI 解码。
#   rg 吐的是 UTF-8，而一行只要**以中文结尾**（例如 `... // #C8323A 血红`），
#   误读会把这一行末尾的**换行一起吃掉**，下一行被并进来 ——
#   于是"真正的引用那一行"看不见，字段被误判成零引用。
#   实测：两个字段的声明行恰好都以中文注释结尾，所以只有它们两个误报。
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

# ★ rg（ripgrep）在**某些 agent 宿主里不在 PATH 上**（实测：DSH 的 pwsh 会话没有），
#   于是这一步直接 CommandNotFoundException → 整个 check.ps1 在第 2b 步挂掉。
#   这不是代码问题，是环境问题；所以这里自己找一遍，找不到再报错。
$rg = (Get-Command rg -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $rg) {
    $cand = @(
        (Join-Path $env:USERPROFILE 'scoop\shims\rg.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\rg.exe')
    )
    if ($env:DSH_RG_PATH) { $cand = @($env:DSH_RG_PATH) + $cand }
    foreach ($c in $cand) { if ($c -and (Test-Path $c)) { $rg = $c; break } }
}
if (-not $rg) {
    $c = Get-ChildItem -Path (Join-Path $env:APPDATA 'TRAE SOLO CN') -Recurse -Filter 'rg.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($c) { $rg = $c.FullName }
}
if (-not $rg) {
    Write-Host '[死配置] X 找不到 ripgrep（rg），无法扫描 —— 请把 rg 放进 PATH（或设 $env:DSH_RG_PATH）' -ForegroundColor Red
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$baselinePath = Join-Path $PSScriptRoot 'dead_config_baseline.txt'
$srcPath = Join-Path $root 'src'

$baseline = @()
if (Test-Path $baselinePath) {
    $baseline = Get-Content $baselinePath |
        Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') } |
        ForEach-Object { $_.Trim() }
}

$dead = @()
$total = 0

foreach ($file in Get-ChildItem $srcPath -Recurse -File -Filter '*.cs') {
    $names = Select-String -Path $file.FullName `
        -Pattern '\[Export[^\]]*\]\s*public\s+[\w<>\[\]\.\?]+\s+(\w+)' -AllMatches |
        ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }

    foreach ($name in $names) {
        $total++
        $lines = & $rg --no-heading -n "\b$name\b" $srcPath 2>$null
        $code = @($lines | Where-Object {
            # 用正则剥掉 `路径:行号:` 前缀，别用 -split ':'：
            # 盘符那个冒号会让结果随 rg 打印绝对/相对路径而变（CWD 相关）。
            $body = $_ -replace '^.*?:\d+:', ''
            ($body -ne $_) -and ($body -notmatch '^\s*(//|///|\*)')
        })
        $uses = @($code | Where-Object {
            $_ -notmatch '\[Export' -and $_ -notmatch ('public\s+[\w<>\[\]\.\?]+\s+' + $name)
        })

        if ($uses.Count -eq 0) { $dead += "$($file.Name)::$name" }
    }
}

$new = @($dead | Where-Object { $baseline -notcontains $_ })
$fixed = @($baseline | Where-Object { $dead -notcontains $_ })

Write-Host "[死配置] 扫描 $total 个 [Export]，零引用 $($dead.Count) 个（基线 $($baseline.Count) 个）"

if ($fixed.Count -gt 0) {
    Write-Host "[死配置] 已修好 $($fixed.Count) 个 —— 请从 dead_config_baseline.txt 删掉对应行：" -ForegroundColor Yellow
    foreach ($f in $fixed) { Write-Host "         - $f" -ForegroundColor Yellow }
}

if ($new.Count -gt 0) {
    Write-Host "[死配置] X 新增 $($new.Count) 个零引用字段（要么接上，要么写进基线并说明理由）：" -ForegroundColor Red
    foreach ($n in $new) { Write-Host "         - $n" -ForegroundColor Red }
    exit 1
}

Write-Host '[死配置] OK 没有新增' -ForegroundColor Green
exit 0
