# Builds NEXDECKHub.exe with the C# compiler that ships with Windows (.NET Framework 4.x, C# 5).
#   powershell -ExecutionPolicy Bypass -File hub\build.ps1
#   powershell -ExecutionPolicy Bypass -File hub\build.ps1 -Apps C:\my\apps -Icons C:\my\icons -Out C:\my\NEXDECKHub.exe
# -Apps:  folder(s) with your app modules (*.cs, classes that extend HubApp; see docs/hub-modules.md)
# -Icons: folder(s) with <app id>.png (88x88) shown on the app cards
# -Resources: extra files embedded as "res.<file name>" (read them with Assembly.GetManifestResourceStream)
param([string[]]$Apps = @(), [string[]]$Icons = @(), [string[]]$Resources = @(), [string]$Out = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = Join-Path $here "NEXDECKHub.exe" }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$gac = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL"
$winmd = "$env:WINDIR\System32\WinMetadata"
$refs = @(
  "$gac\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll",
  "$gac\System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll",
  "$gac\System.IO.Compression.FileSystem\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.FileSystem.dll",
  "$winmd\Windows.Foundation.winmd",
  "$winmd\Windows.Media.winmd",
  "$winmd\Windows.Storage.winmd"
) | ForEach-Object { "-r:$_" }
$src = @(Get-ChildItem "$here\NexDeckHub.*.cs" | ForEach-Object { $_.FullName })  # core, window, QR, sample
foreach ($d in $Apps) { $src += (Get-ChildItem "$d\*.cs" | ForEach-Object { $_.FullName }) }
$res = @("-resource:$here\nexdeck.ico,NexDeck.ico")
foreach ($d in @("$here\icons") + $Icons) {
  if (Test-Path $d) { $res += (Get-ChildItem "$d\*.png" | ForEach-Object { "-resource:$($_.FullName),app.$($_.Name)" }) }
}
foreach ($f in $Resources) { $res += "-resource:$f,res.$(Split-Path -Leaf $f)" }
& "$fw\csc.exe" -nologo -target:winexe -optimize+ -codepage:65001 "-out:$Out" "-win32icon:$here\nexdeck.ico" @res @refs @src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "ok: $Out"
