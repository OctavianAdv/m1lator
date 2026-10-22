<#
  m1lator: undo apply-settings.ps1 using a backup\settings-*.json snapshot (latest by default).
#>
param([string]$Snapshot)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot -Parent
$BackupDir = Join-Path $Repo 'backup'
if (-not $Snapshot) { $Snapshot = (Get-ChildItem "$BackupDir\settings-*.json" | Sort-Object Name | Select-Object -Last 1).FullName }
$data = Get-Content $Snapshot -Raw | ConvertFrom-Json
$Client = (Get-ItemProperty 'HKLM:\SOFTWARE\BlueStacks_bgp64\Client').ClientInstallDir

foreach ($p in $data.PSObject.Properties) {
    if ($p.Name -eq 'locale:STRING_ENABLE_HIGH_FPS') {
        $loc = Join-Path (Split-Path $Client -Parent) 'Locales\i18n.en-US.txt'
        $text = [IO.File]::ReadAllText($loc)
        $text = [regex]::Replace($text, '(?m)^STRING_ENABLE_HIGH_FPS = .*?(\r?)$', "STRING_ENABLE_HIGH_FPS = $($p.Value)`$1")
        [IO.File]::WriteAllText($loc, $text, (New-Object Text.UTF8Encoding $true))
        continue
    }
    $path, $name = $p.Name -split '\|', 2
    if ($null -eq $p.Value) { Remove-ItemProperty $path -Name $name -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $path -Name $name -Value $p.Value }
}
if (Test-Path "$BackupDir\bst_config.orig") { Copy-Item "$BackupDir\bst_config.orig" (Join-Path $Client 'bst_config') -Force }
Write-Host "restored from $Snapshot (theme folder Client\m1lator left in place; pick another skin in the UI or delete it)"
