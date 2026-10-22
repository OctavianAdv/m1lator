<#
  m1lator: build + install the IL patches (HD-Common x2, Bluestacks.exe, HD-Agent.exe, HD-Player.exe, M1lator.Boost.dll).
  - first run backs up the pristine files to backup\original\ (never overwritten afterwards)
  - patches are always rebuilt from those pristine copies, so re-running is safe
  - re-launches itself elevated (one UAC prompt) because Program Files is admin-only
  Undo with uninstall-patches.ps1.
#>
$ErrorActionPreference = 'Stop'
$Repo   = Split-Path $PSScriptRoot -Parent
$PF     = 'C:\Program Files\BlueStacks_bgp64'
$Client = (Get-ItemProperty 'HKLM:\SOFTWARE\BlueStacks_bgp64\Client').ClientInstallDir
$Orig   = Join-Path $Repo 'backup\original'
$Out    = Join-Path $Repo 'build\patched'
$Log    = Join-Path $Repo 'build\install.log'
$Files  = @{ ProgramFiles = @('HD-Common.dll', 'HD-Player.exe', 'HD-Agent.exe'); Client = @('HD-Common.dll', 'Bluestacks.exe') }
$Roots  = @{ ProgramFiles = $PF; Client = $Client }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $admin) {
    # 1. back up pristine originals once; refuse anything that is no longer the signed BlueStacks file
    foreach ($sub in $Files.Keys) {
        New-Item -ItemType Directory -Force (Join-Path $Orig $sub) | Out-Null
        foreach ($f in $Files[$sub]) {
            $dst = Join-Path $Orig "$sub\$f"
            $src = Join-Path $Roots[$sub] $f
            if (-not (Test-Path $dst)) {
                if ((Get-AuthenticodeSignature $src).Status -ne 'Valid') { throw "$src is not a pristine signed original; restore it before backing up" }
                Copy-Item $src $dst
                Write-Host "backed up $sub\$f"
            }
        }
    }

    # 2. build Boost + patcher, produce the patched set from the pristine copies
    dotnet build -c Release (Join-Path $Repo 'src\M1lator.Boost') | Out-Null
    if ($LASTEXITCODE) { throw 'Boost build failed' }
    dotnet build -c Release (Join-Path $Repo 'src\M1lator.Tool') | Out-Null
    if ($LASTEXITCODE) { throw 'patcher build failed' }
    $boost = Join-Path $Repo 'src\M1lator.Boost\bin\Release\net40\M1lator.Boost.dll'
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    dotnet (Join-Path $Repo 'src\M1lator.Tool\bin\Release\net9.0-windows\M1lator.Tool.dll') patch $Orig $Out $boost
    if ($LASTEXITCODE) { throw 'patching failed' }

    # 3. copy into place (elevated)
    Remove-Item $Log -ErrorAction SilentlyContinue
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if (-not (Test-Path $Log)) { throw 'elevated install did not run (UAC declined?)' }
    Get-Content $Log
    return
}

# ---- elevated part: stop BlueStacks, copy patched files
$lines = @()
try {
    Get-Process -Name 'Bluestacks', 'HD-Player', 'HD-Agent', 'HD-MultiInstanceManager', 'BstkSVC' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    foreach ($sub in $Files.Keys) {
        foreach ($f in $Files[$sub]) {
            Copy-Item (Join-Path $Out "$sub\$f") (Join-Path $Roots[$sub] $f) -Force
            $lines += "installed $sub\$f"
        }
    }
    Copy-Item (Join-Path $Out 'ProgramFiles\M1lator.Boost.dll') (Join-Path $PF 'M1lator.Boost.dll') -Force
    $lines += 'installed ProgramFiles\M1lator.Boost.dll'
}
catch { $lines += "ERROR: $($_.Exception.Message)" }
$lines | Set-Content $Log
