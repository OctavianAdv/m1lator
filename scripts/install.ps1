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
    Write-Host ''
    Write-Host 'GATA! Porneste BlueStacks.  /  DONE! Start BlueStacks.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host 'Apasa Enter pentru a inchide / Press Enter to close'
