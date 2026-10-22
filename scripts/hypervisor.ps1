<#
  m1lator: turn the Windows hypervisor off/on.
  BlueStacks 4 (bgp64, legacy VT-x engine) refuses to start while the Windows hypervisor runs
  (Memory Integrity / Core Isolation, VBS, Hyper-V, Virtual Machine Platform, WSL2).
    -Mode Off : Memory Integrity off, VBS off, hypervisorlaunchtype off  (previous values saved)
    -Mode On  : restores the saved values
    -Mode Status : prints the current state
  A restart is required for changes to take effect. Needs admin (one UAC prompt).
#>
param([ValidateSet('Off', 'On', 'Status')][string]$Mode = 'Status', [switch]$NoPause)
. "$PSScriptRoot\common.ps1"

$HvciKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'
$DgKey   = 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard'
$State   = Join-Path $BackupDir 'hypervisor.json'

function Get-HypervisorStatus {
    $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
    [pscustomobject]@{
        HypervisorRunning = [bool](Get-CimInstance Win32_ComputerSystem).HypervisorPresent
        MemoryIntegrity   = [bool]($dg -and $dg.SecurityServicesRunning -contains 2)
        VbsRunning        = [bool]($dg -and $dg.VirtualizationBasedSecurityStatus -eq 2)
    }
}

if ($Mode -eq 'Status') { Get-HypervisorStatus | Format-List; return }

if (-not (Test-Admin)) {
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Mode $Mode"
    return
}

try {
    New-Item -ItemType Directory -Force $BackupDir | Out-Null
    function Get-Val($path, $name) { (Get-ItemProperty $path -Name $name -ErrorAction SilentlyContinue).$name }

    if ($Mode -eq 'Off') {
        if ((Get-Val $HvciKey 'Locked') -eq 1) {
            Write-Host 'Memory Integrity e blocat de UEFI lock; dezactiveaza-l din Windows Security -> Device security -> Core isolation.' -ForegroundColor Yellow
        }
        $launch = ((bcdedit /enum '{current}') | Select-String 'hypervisorlaunchtype\s+(\S+)').Matches.Groups[1].Value
        if (-not (Test-Path $State)) {
            # keep only the first snapshot: it holds the user's original configuration
            [ordered]@{
                HvciEnabled          = Get-Val $HvciKey 'Enabled'
                VbsEnabled           = Get-Val $DgKey 'EnableVirtualizationBasedSecurity'
                HypervisorLaunchType = $launch
            } | ConvertTo-Json | Set-Content $State -Encoding UTF8
        }
        if (-not (Test-Path $HvciKey)) { New-Item $HvciKey -Force | Out-Null }
        Set-ItemProperty $HvciKey -Name 'Enabled' -Value 0 -Type DWord
        Set-ItemProperty $DgKey -Name 'EnableVirtualizationBasedSecurity' -Value 0 -Type DWord
        bcdedit /set '{current}' hypervisorlaunchtype off | Out-Null
        Write-Host 'Hypervisor Windows: OPRIT. Reporneste PC-ul. / Windows hypervisor OFF. Restart your PC.' -ForegroundColor Green
    }
    else {
        $saved = if (Test-Path $State) { Get-Content $State -Raw | ConvertFrom-Json } else { $null }
        $hvci = if ($saved -and $null -ne $saved.HvciEnabled) { $saved.HvciEnabled } else { 1 }
        $vbs = if ($saved -and $null -ne $saved.VbsEnabled) { $saved.VbsEnabled } else { $null }
        $launch = if ($saved -and $saved.HypervisorLaunchType) { $saved.HypervisorLaunchType } else { 'Auto' }
        if (-not (Test-Path $HvciKey)) { New-Item $HvciKey -Force | Out-Null }
        Set-ItemProperty $HvciKey -Name 'Enabled' -Value $hvci -Type DWord
        if ($null -eq $vbs) { Remove-ItemProperty $DgKey -Name 'EnableVirtualizationBasedSecurity' -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $DgKey -Name 'EnableVirtualizationBasedSecurity' -Value $vbs -Type DWord }
        bcdedit /set '{current}' hypervisorlaunchtype $launch | Out-Null
        Remove-Item $State -ErrorAction SilentlyContinue
        Write-Host 'Hypervisor Windows: PORNIT la loc. Reporneste PC-ul. / Windows hypervisor restored. Restart your PC.' -ForegroundColor Green
        Write-Host 'Atentie: BlueStacks 4 nu porneste cat timp e activ. / Note: BlueStacks 4 will not start while it is on.' -ForegroundColor Yellow
    }
}
catch {
    Write-Host "EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
if (-not $NoPause) { Read-Host 'Apasa Enter pentru a inchide / Press Enter to close' }
