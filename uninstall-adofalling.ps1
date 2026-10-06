<#
  uninstall-adofalling.ps1 — 撤销 AdoFalling 部署，还原 UnityModManager

  把 Doorstop 的 target_assembly 改回 UMM 自己的写法
  （值取自 UMM 源码 UnityModManagerApp\Form.cs:901），并移除 BepInEx 目录。

  用法：
    pwsh -File uninstall-adofalling.ps1
    pwsh -File uninstall-adofalling.ps1 -GameDir "D:\path\to\game"
#>
param(
    [string]$GameDir = 'D:\Steam\steamapps\common\A Dance of Fire and Ice'
)

$ErrorActionPreference = 'Stop'

$doorstopCfg = Join-Path $GameDir 'doorstop_config.ini'
$bepDir      = Join-Path $GameDir 'BepInEx'

# UMM 源码 Form.cs:436  managerAssemblyPath = Path.Combine(managerPath, typeof(UnityModManager).Module.Name)
# UMM 源码 Form.cs:900  target_assembly = 该路径去掉游戏根目录前缀
$ummTarget = 'A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll'
$ummDll    = Join-Path $GameDir $ummTarget

if (-not (Test-Path $GameDir))    { throw "找不到游戏目录: $GameDir" }
if (-not (Test-Path $doorstopCfg)){ throw "找不到 $doorstopCfg" }
if (-not (Test-Path $ummDll))     { throw "找不到 UMM 主程序集: $ummDll" }

Write-Host '=== 1/3 校验 UMM 主程序集 ==='
Write-Host ("  {0} ({1:N0} bytes)" -f $ummTarget, (Get-Item $ummDll).Length)

Write-Host '=== 2/3 切换 Doorstop 入口回 UnityModManager ==='
$cfg = Get-Content $doorstopCfg -Raw
if ($cfg -notmatch '(?m)^target_assembly=') { throw "doorstop_config.ini 里没有 target_assembly 行，请手动检查。" }
$cfg = $cfg -replace '(?m)^target_assembly=.*$', "target_assembly=$ummTarget"
Set-Content $doorstopCfg -Value $cfg -NoNewline
Write-Host "  target_assembly -> $ummTarget"

Write-Host '=== 3/3 移除 BepInEx ==='
if (Test-Path $bepDir) {
    # 安全检查：确认解析出的绝对路径确实位于游戏目录下
    $resolved = (Resolve-Path $bepDir).Path
    if ($resolved -ne (Join-Path (Resolve-Path $GameDir).Path 'BepInEx')) {
        throw "拒绝删除，路径不匹配: $resolved"
    }
    Remove-Item $resolved -Recurse -Force
    Write-Host '  BepInEx\ 已移除'
} else {
    Write-Host '  BepInEx\ 不存在，跳过'
}

Write-Host ''
Write-Host '完成。启动游戏即可恢复 UnityModManager 与 Mods\ 里的 mods。'
