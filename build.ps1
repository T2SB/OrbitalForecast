# Builds OrbitalForecast.dll straight against KSP's own assemblies using the
# .NET Framework C# compiler that ships with Windows. No Visual Studio / SDK
# required. Output goes to GameData\OrbitalForecast\Plugins and, if -Install is
# passed, is copied into the live KSP GameData folder.
param(
	[string]$KspPath = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program",
	[switch]$Install
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$managed = Join-Path $KspPath "KSP_x64_Data\Managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
if (-not (Test-Path $managed)) { throw "KSP Managed folder not found at $managed" }

$outDir = Join-Path $root "GameData\OrbitalForecast\Plugins"
New-Item -ItemType Directory -Force $outDir | Out-Null
$outDll = Join-Path $outDir "OrbitalForecast.dll"

$refNames = @(
	"mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll",
	"UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.IMGUIModule.dll",
	"UnityEngine.InputLegacyModule.dll", "UnityEngine.PhysicsModule.dll",
	"UnityEngine.TextRenderingModule.dll", "UnityEngine.AnimationModule.dll",
	"UnityEngine.UI.dll", "UnityEngine.UIModule.dll",
	"Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll"
)

$cscArgs = @("/target:library", "/nostdlib+", "/noconfig", "/optimize+", "/warn:2", "/out:$outDll")
foreach ($r in $refNames) {
	$p = Join-Path $managed $r
	if (-not (Test-Path $p)) { throw "Reference assembly missing: $p" }
	$cscArgs += "/r:$p"
}
$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs -Recurse
if ($sources.Count -eq 0) { throw "No .cs source files found under src\" }
foreach ($f in $sources) { $cscArgs += $f.FullName }

Write-Host "Compiling OrbitalForecast.dll ..."
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }
Write-Host "Built: $outDll"

if ($Install) {
	$dest = Join-Path $KspPath "GameData\OrbitalForecast"
	# Wipe any previous install so stale files / nested copies can't linger
	# (Copy-Item nests the source inside an existing destination folder).
	if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
	Copy-Item (Join-Path $root "GameData\OrbitalForecast") $dest -Recurse -Force
	Write-Host "Installed to: $dest"
}
