<#
  install-adofalling.ps1 — 部署 AdoFalling（BepInEx 加载器）

  重要：Doorstop 的 target_assembly 只有一个位置，本脚本会把它改成 BepInEx。
        你的 UnityModManager mods（Mods\ 里那 5 个）会随之失效。
        想切回去就运行 uninstall-adofalling.ps1。

  用法：
    pwsh -File install-adofalling.ps1
    pwsh -File install-adofalling.ps1 -GameDir "D:\path\to\game"
#>
param(
    [string]$GameDir = 'D:\Steam\steamapps\common\A Dance of Fire and Ice'
)

$ErrorActionPreference = 'Stop'

$dll         = Join-Path $PSScriptRoot 'AdoFalling\build\AdoFalling.dll'
$bepArchive  = Join-Path $env:TEMP 'bepinex5\BepInEx'
$doorstopCfg = Join-Path $GameDir 'doorstop_config.ini'

if (-not (Test-Path $dll))        { throw "找不到构建产物: $dll`n先编译：cd AdoFalling; .\build.ps1" }
if (-not (Test-Path $GameDir))    { throw "找不到游戏目录: $GameDir" }
if (-not (Test-Path $bepArchive)) { throw "找不到已解压的 BepInEx: $bepArchive`n请先下载并解压 BepInEx 5.4.23.5 到该位置。" }
if (-not (Test-Path $doorstopCfg)){ throw "找不到 $doorstopCfg" }

Write-Host '=== 1/4 部署 BepInEx 运行时 ==='
Copy-Item $bepArchive $GameDir -Recurse -Force
Write-Host '  BepInEx\ 已写入'

Write-Host '=== 2/4 部署 AdoFalling 插件 ==='
$plugins = Join-Path $GameDir 'BepInEx\plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $dll $plugins -Force
Write-Host "  -> $plugins\AdoFalling.dll"

Write-Host '=== 3/4 切换 Doorstop 入口到 BepInEx ==='
$cfg = Get-Content $doorstopCfg -Raw
if ($cfg -notmatch '(?m)^target_assembly=') { throw "doorstop_config.ini 里没有 target_assembly 行，请手动检查。" }
$cfg = $cfg -replace '(?m)^target_assembly=.*$', 'target_assembly=BepInEx\core\BepInEx.Preloader.dll'
Set-Content $doorstopCfg -Value $cfg -NoNewline
Write-Host '  target_assembly -> BepInEx\core\BepInEx.Preloader.dll'

Write-Host '=== 4/4 校验 ==='
$ok = (Test-Path (Join-Path $plugins 'AdoFalling.dll')) -and
      (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.Preloader.dll')) -and
      (Select-String -Path $doorstopCfg -Pattern 'BepInEx\\core\\BepInEx\.Preloader\.dll' -Quiet)
if (-not $ok) { throw '部署校验失败。' }

Write-Host ''
Write-Host '完成。Mods\ 未被改动，但里面的 mods 现在不会加载（入口被 BepInEx 占用了）。'
Write-Host '想切回 UnityModManager：pwsh -File uninstall-adofalling.ps1'
