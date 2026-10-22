<#
  m1lator: undo apply-settings.ps1 using the oldest backup\settings-*.json snapshot
  (= the values from before m1lator was first applied), and switch back to the stock theme.
#>
param([string]$Snapshot)
. "$PSScriptRoot\common.ps1"

if (-not $Snapshot) { $Snapshot = (Get-ChildItem "$BackupDir\settings-*.json" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -First 1).FullName }
if (-not $Snapshot) { Write-Host 'no settings backup found, nothing to restore'; return }
$data = Get-Content $Snapshot -Raw | ConvertFrom-Json

foreach ($p in $data.PSObject.Properties) {
    if ($p.Name -eq 'locale:STRING_ENABLE_HIGH_FPS') {
        $loc = Join-Path $DataDir 'Locales\i18n.en-US.txt'
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
$theme = Join-Path $Client 'm1lator'
if (Test-Path $theme) { Remove-Item -Recurse -Force $theme }
Write-Host "settings restored from $Snapshot"
