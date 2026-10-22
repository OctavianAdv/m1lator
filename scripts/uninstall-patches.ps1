<#
  m1lator: restore the pristine BlueStacks binaries from backup\original\ and remove M1lator.Boost.dll.
  Re-launches itself elevated (one UAC prompt).
#>
$ErrorActionPreference = 'Stop'
$Repo   = Split-Path $PSScriptRoot -Parent
$PF     = 'C:\Program Files\BlueStacks_bgp64'
$Client = (Get-ItemProperty 'HKLM:\SOFTWARE\BlueStacks_bgp64\Client').ClientInstallDir
$Orig   = Join-Path $Repo 'backup\original'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

Get-Process -Name 'Bluestacks','HD-Player','HD-Agent','HD-MultiInstanceManager','BstkSVC' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Get-ChildItem "$Orig\ProgramFiles" | ForEach-Object { Copy-Item $_.FullName (Join-Path $PF $_.Name) -Force }
Get-ChildItem "$Orig\Client"       | ForEach-Object { Copy-Item $_.FullName (Join-Path $Client $_.Name) -Force }
Remove-Item (Join-Path $PF 'M1lator.Boost.dll') -ErrorAction SilentlyContinue
