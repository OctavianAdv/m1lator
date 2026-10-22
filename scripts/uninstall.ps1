<#
  m1lator: full uninstall = original binaries + original settings + stock theme. One UAC prompt.
#>
. "$PSScriptRoot\common.ps1"
if (Invoke-Elevated $PSCommandPath) { return }

try {
    & "$PSScriptRoot\uninstall-patches.ps1"
    & "$PSScriptRoot\restore-settings.ps1"
    Write-Host ''
    Write-Host 'BlueStacks a revenit la original.  /  BlueStacks restored to original.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host 'Apasa Enter pentru a inchide / Press Enter to close'
