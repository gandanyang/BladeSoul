# refresh.ps1 —— 一键刷新制作人工作台（双击即可）
# 作用：重跑数据生成器，把 docs/TASKS.md、git log、HANDOFF、排期的最新状态
#       灌进 tools/dashboard/dashboard.html，然后用默认浏览器打开。
# 注意：本项目 .ps1 文件需要 UTF-8 BOM（PS 5.1 按 ANSI 读中文会乱码），
#       本文件内容刻意保持纯 ASCII，避免踩 AGENTS.md §6 记的那个坑。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$py = Get-Command python -ErrorAction SilentlyContinue

if (-not $py) {
    Write-Host '未找到 python，请先安装或把 python 加进 PATH'
    Read-Host '按回车退出'
    exit 1
}

Push-Location $root
try {
    python tools\dashboard\generate_dashboard.py
    if ($LASTEXITCODE -ne 0) { throw "生成失败（exit $LASTEXITCODE）" }
    Start-Process (Join-Path $root 'tools\dashboard\dashboard.html')
}
finally {
    Pop-Location
}
