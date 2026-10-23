<#
  m1lator: install the IL patches (HD-Common x2, Bluestacks.exe, HD-Agent.exe, HD-Player.exe, M1lator.Boost.dll).
  - first run backs up the pristine signed files to C:\ProgramData\m1lator\backup\original\ (never overwritten afterwards)
  - patches are always rebuilt from those pristine copies, so re-running is safe
  - needs admin (one UAC prompt). Undo with uninstall-patches.ps1.
#>
. "$PSScriptRoot\common.ps1"
if (Invoke-Elevated $PSCommandPath) { return }

$Orig  = Join-Path $BackupDir 'original'
$Out   = Join-Path $Root 'build\patched'
$Files = @{ ProgramFiles = @('HD-Common.dll', 'HD-Player.exe', 'HD-Agent.exe'); Client = @('HD-Common.dll', 'Bluestacks.exe') }
$Roots = @{ ProgramFiles = $PF; Client = $Client }

# 1. back up pristine originals once; refuse anything that is no longer the signed BlueStacks file
foreach ($sub in $Files.Keys) {
    New-Item -ItemType Directory -Force (Join-Path $Orig $sub) | Out-Null
    foreach ($f in $Files[$sub]) {
        $dst = Join-Path $Orig "$sub\$f"
        $src = Join-Path $Roots[$sub] $f
        if (-not (Test-Path $dst)) {
            if ((Get-AuthenticodeSignature $src).Status -ne 'Valid') { throw "$src is not the original signed file; reinstall BlueStacks first" }
            Copy-Item $src $dst
            Write-Host "backup: $sub\$f"
        }
    }
}

# 2. produce the patched set from the pristine copies
$tools = Get-M1latorTools
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
& $tools.Tool patch $Orig $Out $tools.Boost
if ($LASTEXITCODE) { throw 'patching failed' }

# 3. stop BlueStacks, copy into place
Stop-BlueStacks
foreach ($sub in $Files.Keys) {
    foreach ($f in $Files[$sub]) {
        Copy-Item (Join-Path $Out "$sub\$f") (Join-Path $Roots[$sub] $f) -Force
        Write-Host "installed: $sub\$f"
    }
}
Copy-Item (Join-Path $Out 'ProgramFiles\M1lator.Boost.dll') (Join-Path $PF 'M1lator.Boost.dll') -Force
Write-Host 'installed: ProgramFiles\M1lator.Boost.dll'
Copy-Item (Join-Path $Out 'Client\M1lator.Client.dll') (Join-Path $Client 'M1lator.Client.dll') -Force
Copy-Item (Join-Path $Out 'Client\M1lator.Boost.dll') (Join-Path $Client 'M1lator.Boost.dll') -Force   # client HD-Common logger
Write-Host 'installed: Client\M1lator.Client.dll, Client\M1lator.Boost.dll'
