# Builds LaneReader and stages it into the game's Mods folder.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Install
#
# LaneReader is a plain UnityModManager mod (Info.json + EntryMethod), so it loads through
# the same loader as AdofaiTweaks. It does not need BepInEx, and it never touches
# doorstop_config.ini.

param(
    [switch]$Install,
    [string]$GameDir = "D:\Steam\steamapps\common\A Dance of Fire and Ice"
)

$ErrorActionPreference = "Stop"

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $here "src\LaneReader"
$OutDir = Join-Path $here "build"
$dll = Join-Path $OutDir "LaneReader.dll"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$managed = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"
$ummDll = Join-Path $managed "UnityModManager\UnityModManager.dll"

foreach ($p in @($csc, $managed, $ummDll)) {
    if (-not (Test-Path $p)) { throw "missing required path: $p" }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# References: netstandard facade, every UnityEngine module, the game assembly, and
# UnityModManager itself (for ModEntry / ModSettings / the Draw attributes).
$refs = New-Object System.Collections.Generic.List[string]
$refs.Add((Join-Path $managed "netstandard.dll"))
$refs.Add((Join-Path $managed "System.Runtime.dll"))
Get-ChildItem $managed -Filter "UnityEngine*.dll" | ForEach-Object { $refs.Add($_.FullName) }
$refs.Add((Join-Path $managed "Assembly-CSharp.dll"))
$refs.Add($ummDll)
# Harmony ships inside UnityModManager's folder and is what JipperResourcePack links
# against, so the exact same assembly is guaranteed to resolve at runtime.
$refs.Add((Join-Path $managed "UnityModManager\0Harmony.dll"))
$refs = $refs | Where-Object { Test-Path $_ } | Select-Object -Unique

$sources = Get-ChildItem $SrcDir -Filter *.cs | Select-Object -ExpandProperty FullName
if (-not $sources) { throw "no sources found in $SrcDir" }

$refArg = ($refs | ForEach-Object { '"' + $_ + '"' }) -join ","
Write-Host "compiling $($sources.Count) source file(s) -> $dll"
$cscLog = Join-Path $OutDir "csc.log"
& $csc /nologo /target:library /langversion:5 /optimize+ `
    /out:"$dll" `
    /reference:$refArg `
    $sources 2>&1 | Tee-Object -FilePath $cscLog
if ($LASTEXITCODE -ne 0) { throw "compilation failed ($LASTEXITCODE), see $cscLog" }
if (-not (Test-Path $dll)) { throw "compiler produced no output" }
Write-Host ("built {0} ({1:N0} bytes)" -f $dll, (Get-Item $dll).Length)

if ($Install) {
    $dest = Join-Path $GameDir "Mods\LaneReader"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll $dest -Force
    Copy-Item (Join-Path $here "Mod\Info.json") $dest -Force
    # Everything else in Mod\ that is not the DLL/Info.json - the install notes, whose name is
    # not ASCII, are matched by wildcard so this script never has to hold a non-ASCII literal
    # (it is saved without a BOM, and PowerShell 5.1 would read one as GBK mojibake).
    Get-ChildItem (Join-Path $here "Mod") -Filter "*.txt" | Copy-Item -Destination $dest -Force
    # The demo sprite is only staged when there is not one already: it is meant to be replaced
    # by the player's own image, and -Install must not overwrite that.
    $demo = Join-Path $dest "note.png"
    if (-not (Test-Path $demo)) { Copy-Item (Join-Path $here "Mod\note.png") $demo -Force }
    Write-Host "installed -> $dest"
}
