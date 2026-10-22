<#
  m1lator: undo apply-settings.ps1 and switch back to the stock theme.
  Every apply writes a settings-*.json snapshot; for each value the OLDEST snapshot that recorded it holds the
  original BlueStacks value (newer versions of m1lator may touch values older versions did not).
#>
. "$PSScriptRoot\common.ps1"

$snapshots = Get-ChildItem "$BackupDir\settings-*.json" -ErrorAction SilentlyContinue | Sort-Object Name
if (-not $snapshots) { Write-Host 'no settings backup found, nothing to restore'; return }

$original = [ordered]@{}
foreach ($s in $snapshots) {
    foreach ($p in (Get-Content $s.FullName -Raw | ConvertFrom-Json).PSObject.Properties) {
        if (-not $original.Contains($p.Name)) { $original[$p.Name] = $p.Value }
    }
}

foreach ($name in $original.Keys) {
    $value = $original[$name]
    if ($name -eq 'locale:STRING_ENABLE_HIGH_FPS') {
        $loc = Join-Path $DataDir 'Locales\i18n.en-US.txt'
        $text = [IO.File]::ReadAllText($loc)
        $text = [regex]::Replace($text, '(?m)^STRING_ENABLE_HIGH_FPS = .*?(\r?)$', "STRING_ENABLE_HIGH_FPS = $value`$1")
        [IO.File]::WriteAllText($loc, $text, (New-Object Text.UTF8Encoding $true))
        continue
    }
    $path, $prop = $name -split '\|', 2
    if ($null -eq $value) { Remove-ItemProperty $path -Name $prop -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $path -Name $prop -Value $value }
}
if (Test-Path "$BackupDir\bst_config.orig") { Copy-Item "$BackupDir\bst_config.orig" (Join-Path $Client 'bst_config') -Force }
# power plan created by m1lator: switch back to the user's plan (or Balanced) and delete ours
$m1 = Get-ItemProperty "$BsKey\m1lator" -ErrorAction SilentlyContinue
if ($m1 -and $m1.PowerScheme) {
    if ((powercfg /getactivescheme | Out-String) -match [regex]::Escape($m1.PowerScheme)) {
        $back = if ($m1.PreviousPowerScheme) { $m1.PreviousPowerScheme } else { '381b4222-f694-41f0-9685-ff5bb260df2e' }
        powercfg /setactive $back | Out-Null
    }
    powercfg /delete $m1.PowerScheme 2>$null | Out-Null
}
if (Test-Path "$BsKey\m1lator") { Remove-Item "$BsKey\m1lator" -Recurse -Force }

foreach ($dir in 'm1lator', 'm1lator-home', 'm1lator-fonts') {
    $p = Join-Path $Client $dir
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
}
Write-Host "settings restored ($($original.Count) values from $($snapshots.Count) snapshot(s))"
