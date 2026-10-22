<#
  m1lator: restore the pristine BlueStacks binaries from backup\original\ and remove M1lator.Boost.dll.
  Needs admin (one UAC prompt).
#>
. "$PSScriptRoot\common.ps1"
if (Invoke-Elevated $PSCommandPath) { return }

$Orig = Join-Path $BackupDir 'original'
if (-not (Test-Path $Orig)) { Write-Host 'no patch backup found, nothing to restore'; return }

Stop-BlueStacks
Get-ChildItem "$Orig\ProgramFiles" | ForEach-Object { Copy-Item $_.FullName (Join-Path $PF $_.Name) -Force; Write-Host "restored: ProgramFiles\$($_.Name)" }
Get-ChildItem "$Orig\Client"       | ForEach-Object { Copy-Item $_.FullName (Join-Path $Client $_.Name) -Force; Write-Host "restored: Client\$($_.Name)" }
Remove-Item (Join-Path $PF 'M1lator.Boost.dll') -ErrorAction SilentlyContinue
