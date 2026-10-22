# m1lator: shared paths and helpers, dot-sourced by every script.
# Tools live prebuilt in <root>\bin (rebuild with: dotnet build -c Release src\<project>).

$ErrorActionPreference = 'Stop'
$Root      = Split-Path $PSScriptRoot -Parent
$BsKey     = 'HKLM:\SOFTWARE\BlueStacks_bgp64'
$PF        = 'C:\Program Files\BlueStacks_bgp64'
# Fixed location so uninstall works from any copy of the folder (and survives deleting it)
$BackupDir = Join-Path $env:ProgramData 'm1lator\backup'

# Running straight from inside a zip: Explorer extracts to a temp folder that disappears afterwards
if ($Root -like "$env:TEMP*" -or $Root -match '\\Temp\d*_[^\\]*\.zip') {
    throw 'Dezarhiveaza mai intai zip-ul (click dreapta -> Extract All), apoi ruleaza Install.bat din folderul dezarhivat. / Extract the zip first.'
}
if (-not (Test-Path "$BsKey\Client")) { throw 'BlueStacks 4 (bgp64) nu este instalat / is not installed.' }

# One-time move of backups made by older versions (kept next to the scripts)
$legacyBackup = Join-Path $Root 'backup'
if ((Test-Path $legacyBackup) -and -not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Force (Split-Path $BackupDir) | Out-Null
    Copy-Item -Recurse $legacyBackup $BackupDir
}
$Client  = (Get-ItemProperty "$BsKey\Client").ClientInstallDir
$DataDir = Split-Path $Client -Parent

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Re-runs the calling script elevated (one UAC prompt) and returns $true if the caller should stop.
function Invoke-Elevated([string]$ScriptPath) {
    if (Test-Admin) { return $false }
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""
    return $true
}

function Stop-BlueStacks {
    Get-Process -Name 'Bluestacks', 'HD-Player', 'HD-Agent', 'HD-MultiInstanceManager', 'BstkSVC' -ErrorAction SilentlyContinue |
        Stop-Process -Force
    Start-Sleep -Milliseconds 1500
}

# Returns @{ Tool = {..}; ThemeGen = {..}; Boost = <path> }; Tool/ThemeGen are scriptblocks taking args.
function Get-M1latorTools {
    # Prebuilt .NET Framework 4.8 tools are committed in <root>\bin: no SDK or runtime install needed.
    $bin = Join-Path $Root 'bin'
    foreach ($f in 'M1lator.Tool.exe', 'M1lator.ThemeGen.exe', 'M1lator.Boost.dll', 'dnlib.dll') {
        if (-not (Test-Path (Join-Path $bin $f))) {
            throw "Lipseste bin\$f. Descarca din nou proiectul de pe GitHub (Code -> Download ZIP) si dezarhiveaza-l. / Missing bin\$f."
        }
    }
    return @{
        Tool     = { & (Join-Path $bin 'M1lator.Tool.exe') @args }.GetNewClosure()
        ThemeGen = { & (Join-Path $bin 'M1lator.ThemeGen.exe') @args }.GetNewClosure()
        Boost    = Join-Path $bin 'M1lator.Boost.dll'
    }
}
