# m1lator: shared paths and helpers, dot-sourced by every script.
# Works in two layouts:
#   release zip : <root>\bin\M1lator.Tool.exe, M1lator.ThemeGen.exe, M1lator.Boost.dll (prebuilt)
#   git repo    : <root>\src\... (built on demand with the .NET SDK)

$ErrorActionPreference = 'Stop'
$Root      = Split-Path $PSScriptRoot -Parent
$BsKey     = 'HKLM:\SOFTWARE\BlueStacks_bgp64'
$PF        = 'C:\Program Files\BlueStacks_bgp64'
$BackupDir = Join-Path $Root 'backup'

if (-not (Test-Path "$BsKey\Client")) { throw 'BlueStacks 4 (bgp64) nu este instalat / is not installed.' }
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
    $bin = Join-Path $Root 'bin'
    if (Test-Path (Join-Path $bin 'M1lator.Tool.exe')) {
        return @{
            Tool     = { & (Join-Path $bin 'M1lator.Tool.exe') @args }.GetNewClosure()
            ThemeGen = { & (Join-Path $bin 'M1lator.ThemeGen.exe') @args }.GetNewClosure()
            Boost    = Join-Path $bin 'M1lator.Boost.dll'
        }
    }
    foreach ($p in 'M1lator.Boost', 'M1lator.Tool', 'M1lator.ThemeGen') {
        dotnet build -c Release (Join-Path $Root "src\$p") | Out-Null
        if ($LASTEXITCODE) { throw "build failed: $p" }
    }
    $toolDll  = Join-Path $Root 'src\M1lator.Tool\bin\Release\net9.0-windows\M1lator.Tool.dll'
    $themeDll = Join-Path $Root 'src\M1lator.ThemeGen\bin\Release\net9.0-windows\M1lator.ThemeGen.dll'
    return @{
        Tool     = { dotnet $toolDll @args }.GetNewClosure()
        ThemeGen = { dotnet $themeDll @args }.GetNewClosure()
        Boost    = Join-Path $Root 'src\M1lator.Boost\bin\Release\net40\M1lator.Boost.dll'
    }
}
