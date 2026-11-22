# Builds the PC side of one app as a module .dll for NEXDECK HUB (C# 5, csc of .NET Framework 4).
# The .dll goes into the app's .nexapp ("pc" in manifest.json); installing it puts /apps/<id>.dll on the SD
# card, and any HUB that sees the card copies and loads it (no HUB rebuild needed).
#   powershell -ExecutionPolicy Bypass -File hub\build-module.ps1 -Src my_app\pc -Out dist\my_app.dll
# -Src:       .cs files or folders with classes that extend HubApp (see docs/hub-modules.md)
# -Hub:       NEXDECKHub.exe to compile against (default: hub\NEXDECKHub.exe; build it first)
# -Icons:     <app id>.png (88x88) embedded as "app.<id>.png" (card icon in the HUB)
# -Resources: extra files embedded as "res.<file name>" (read with Assembly.GetExecutingAssembly())
param([string[]]$Src = @(), [string]$Hub = "", [string[]]$Icons = @(), [string[]]$Resources = @(), [Parameter(Mandatory = $true)][string]$Out)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Hub) { $Hub = Join-Path $here "NEXDECKHub.exe" }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$gac = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL"
$winmd = "$env:WINDIR\System32\WinMetadata"
$refs = @(
  $Hub,
  "System.Windows.Forms.dll", "System.Drawing.dll", "System.Management.dll", "System.Web.Extensions.dll", "System.Xml.dll",
  "$gac\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll",
  "$gac\System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll",
  "$gac\System.IO.Compression.FileSystem\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.FileSystem.dll",
  "$winmd\Windows.Foundation.winmd",
  "$winmd\Windows.Media.winmd",
  "$winmd\Windows.Storage.winmd"
) | ForEach-Object { "-r:$_" }
$files = @()
foreach ($s in $Src) {
  if (Test-Path $s -PathType Container) { $files += (Get-ChildItem "$s\*.cs" | ForEach-Object { $_.FullName }) } else { $files += $s }
}
$res = @()
foreach ($i in $Icons) { $res += "-resource:$i,app.$(Split-Path -Leaf $i)" }
foreach ($f in $Resources) { $res += "-resource:$f,res.$(Split-Path -Leaf $f)" }
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
& "$fw\csc.exe" -nologo -target:library -optimize+ -codepage:65001 "-out:$Out" @res @refs @files
if ($LASTEXITCODE -ne 0) { throw "module build failed: $Out" }
Write-Host "ok: $Out"
