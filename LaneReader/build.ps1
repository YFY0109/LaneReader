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

# ---- release package -------------------------------------------------------
# A zip laid out the way UnityModManager wants it: the mod folder at the root, so it can be
# dropped straight into Mods\ or handed to UMM's own installer. Rebuilt from the same files the
# -Install path copies, including lang\ (language files are part of the release, not of the
# player's saved settings).
$stage = Join-Path $OutDir "zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$modDir = Join-Path $stage "LaneReader"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item $dll $modDir -Force
Copy-Item (Join-Path $here "Mod\Info.json") $modDir -Force
Get-ChildItem (Join-Path $here "Mod") -Filter "*.txt" | Copy-Item -Destination $modDir -Force
foreach ($demo in @("note.png", "hold.png")) {
    $demoSrc = Join-Path $here "Mod\$demo"
    if (Test-Path $demoSrc) { Copy-Item $demoSrc $modDir -Force }
}
$langSrc = Join-Path $here "Mod\lang"
if (Test-Path $langSrc) {
    $langDst = Join-Path $modDir "lang"
    New-Item -ItemType Directory -Force -Path $langDst | Out-Null
    Get-ChildItem $langSrc -Filter "*.txt" | Copy-Item -Destination $langDst -Force
}
$version = (Get-Content (Join-Path $here "Mod\Info.json") | Select-String '"Version"').Line.Split('"')[3]
$zip = Join-Path $OutDir ("LaneReader-" + $version + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item $stage -Recurse -Force
Write-Host ("packaged {0} ({1:N0} bytes)" -f $zip, (Get-Item $zip).Length)

if ($Install) {
    $dest = Join-Path $GameDir "Mods\LaneReader"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll $dest -Force
    Copy-Item (Join-Path $here "Mod\Info.json") $dest -Force
    # Everything else in Mod\ that is not the DLL/Info.json - the install notes, whose name is
    # not ASCII, are matched by wildcard so this script never has to hold a non-ASCII literal
    # (it is saved without a BOM, and PowerShell 5.1 would read one as GBK mojibake).
    Get-ChildItem (Join-Path $here "Mod") -Filter "*.txt" | Copy-Item -Destination $dest -Force
    # Language files: en.txt doubles as the template for every other language. Copied only when
    # missing, so a translation the player edited - or added - is never overwritten.
    $langSrc = Join-Path $here "Mod\lang"
    if (Test-Path $langSrc) {
        $langDst = Join-Path $dest "lang"
        New-Item -ItemType Directory -Force -Path $langDst | Out-Null
        Get-ChildItem $langSrc -Filter "*.txt" | ForEach-Object {
            $target = Join-Path $langDst $_.Name
            if (-not (Test-Path $target)) { Copy-Item $_.FullName $target -Force }
        }
    }
    # The demo sprites are only staged when there is not one already: they are meant to be replaced
    # by the player's own images, and -Install must not overwrite that.
    foreach ($demo in @("note.png", "hold.png")) {
        $staged = Join-Path $dest $demo
        if (-not (Test-Path $staged)) { Copy-Item (Join-Path $here "Mod\$demo") $staged -Force }
    }
    Write-Host "installed -> $dest"
}
