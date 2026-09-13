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
        $lines = rg --no-heading -n "\b$name\b" $srcPath 2>$null
        $code = @($lines | Where-Object {
            $parts = $_ -split ':', 3
            $parts.Count -ge 3 -and ($parts[2] -notmatch '^\s*(//|///|\*)')
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
