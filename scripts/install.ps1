<#
  m1lator: full install = settings + theme + patches. One UAC prompt.
#>
. "$PSScriptRoot\common.ps1"
if (Invoke-Elevated $PSCommandPath) { return }

try {
    Write-Host '== m1lator: inchid BlueStacks / closing BlueStacks' -ForegroundColor Cyan
    Stop-BlueStacks
    Write-Host '== m1lator: tema + setari / theme + settings' -ForegroundColor Cyan
    & "$PSScriptRoot\apply-settings.ps1"
    Write-Host '== m1lator: patch-uri / patches' -ForegroundColor Cyan
    & "$PSScriptRoot\install-patches.ps1"

    # BlueStacks 4 cannot start while the Windows hypervisor runs (Memory Integrity / VBS / Hyper-V)
    $restart = $false
    if ((Get-CimInstance Win32_ComputerSystem).HypervisorPresent) {
        Write-Host ''
        Write-Host 'ATENTIE: Hypervisor-ul Windows este activ (Memory Integrity / Core Isolation / Hyper-V).' -ForegroundColor Yellow
        Write-Host 'Cu el pornit, BlueStacks 4 NU porneste si performanta scade mult.' -ForegroundColor Yellow
        Write-Host 'Dezactivarea scade putin securitatea Windows (protectia kernel-ului) si cere restart.'
        Write-Host 'Se poate reactiva oricand cu Hypervisor-On.bat.'
        Write-Host '(WARNING: the Windows hypervisor is on; BlueStacks 4 will not start. Turn it off? Needs a restart.)'
        $answer = Read-Host 'Il dezactivez? / Turn it off? (D/N)'
        if ($answer -match '^(d|da|y|yes)$') {
            & "$PSScriptRoot\hypervisor.ps1" -Mode Off -NoPause
            $restart = $true
        }
    }
    Write-Host ''
    if ($restart) { Write-Host 'GATA! Reporneste PC-ul, apoi porneste BlueStacks.  /  DONE! Restart your PC, then start BlueStacks.' -ForegroundColor Green }
    else { Write-Host 'GATA! Porneste BlueStacks.  /  DONE! Start BlueStacks.' -ForegroundColor Green }
}
catch {
    Write-Host ''
    Write-Host "EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host 'Apasa Enter pentru a inchide / Press Enter to close'
