<#
  launch-adofai.ps1 — 切换 Doorstop 入口后通过 Steam 启动 ADOFAI

  用法：
    pwsh -File launch-adofai.ps1 -Mode UMM
    pwsh -File launch-adofai.ps1 -Mode AdoFalling

  UMM        : target_assembly -> UnityModManager（你原本的 5 个 mod）
  AdoFalling : target_assembly -> BepInEx（下落式叠加层，需要先跑 install-adofalling.ps1）

  始终通过 Steam 启动（steam://rungameid），不会离线启动。

  行为：
    - 启动前把 doorstop_config.ini 切成对应模式
    - 等游戏退出后，自动把 doorstop_config.ini 还原成启动前的样子
    - 每次切换都会留一份 .bak 备份
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('UMM', 'AdoFalling')]
    [string]$Mode,

    [string]$GameDir = 'D:\Steam\steamapps\common\A Dance of Fire and Ice',
    [string]$SteamExe = 'D:\Steam\steam.exe',
    [int]$AppId = 977950
)

$ErrorActionPreference = 'Stop'

$cfgPath = Join-Path $GameDir 'doorstop_config.ini'
$exeName = 'A Dance of Fire and Ice.exe'

# 各模式的 target_assembly 取值
#   UMM        : 取自 UMM 源码 UnityModManagerApp\Form.cs:900-901
#   AdoFalling : BepInEx 5.4.23.5 自带 doorstop_config.ini 的默认值
$targets = @{
    'UMM'        = 'A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll'
    'AdoFalling' = 'BepInEx\core\BepInEx.Preloader.dll'
}

if (-not (Test-Path $GameDir)) { throw "找不到游戏目录: $GameDir" }
if (-not (Test-Path $cfgPath)) { throw "找不到 $cfgPath" }
if (-not (Test-Path $SteamExe)) { throw "找不到 Steam: $SteamExe" }

$target = $targets[$Mode]
$targetAbs = Join-Path $GameDir $target
if (-not (Test-Path $targetAbs)) {
    throw "模式 $Mode 的入口程序集不存在:`n  $targetAbs`n`n如果是 AdoFalling，请先运行 install-adofalling.ps1。"
}

# ---- 备份当前配置，然后切换到目标模式 ----
$original = Get-Content $cfgPath -Raw
$backup = "$cfgPath.adofai-launcher.bak"
Set-Content -Path $backup -Value $original -NoNewline

if ($original -notmatch '(?m)^target_assembly=') {
    throw "doorstop_config.ini 里没有 target_assembly 行，请手动检查。"
}
$patched = $original -replace '(?m)^target_assembly=.*$', "target_assembly=$target"
Set-Content -Path $cfgPath -Value $patched -NoNewline

Write-Host "模式: $Mode"
Write-Host "  target_assembly = $target"
Write-Host '  旧配置已备份 -> doorstop_config.ini.adofai-launcher.bak'

# ---- 记录启动前已在运行的进程，避免误判 ----
$before = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Id)

# ---- 通过 Steam 启动 ----
Write-Host "通过 Steam 启动 (appid $AppId)..."
Start-Process "steam://rungameid/$AppId"

# ---- 等游戏进程出现 ----
$game = $null
for ($i = 0; $i -lt 240; $i++) {
    Start-Sleep -Milliseconds 500
    $now = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue)
    $game = $now | Where-Object { $before -notcontains $_.Id } | Select-Object -First 1
    if ($game) { break }
}
if (-not $game) {
    Write-Warning '未检测到游戏进程启动。配置保持为当前模式，未还原。'
    Write-Host '如需手动还原: 把 doorstop_config.ini.adofai-launcher.bak 复制回 doorstop_config.ini'
    exit 1
}

Write-Host "游戏已启动 (PID $($game.Id))，等待退出..."
$game.WaitForExit()

# ---- 游戏退出后还原 ----
Set-Content -Path $cfgPath -Value $original -NoNewline
Remove-Item $backup -Force -ErrorAction SilentlyContinue
Write-Host '游戏已退出，doorstop_config.ini 已还原。'
