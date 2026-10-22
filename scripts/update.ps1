<#
  m1lator: update to the newest version published on GitHub, then reinstall.
  - newest version = highest vX.Y.Z tag (release or plain tag)
  - downloads that tag's zip (it contains the prebuilt bin), replaces this folder's files, runs install.ps1
  Needs the GitHub repo to be public (anonymous download).
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Repo = 'OctavianAdv/m1lator'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'm1lator-updater' }

function Get-LocalVersion {
    $f = Join-Path $Root 'VERSION'
    if (Test-Path $f) { return [version](Get-Content $f -Raw).Trim() }
    return [version]'0.0.0'
}

try {
    $local = Get-LocalVersion
    Write-Host "versiunea instalata / installed: $local"

    try {
        $tags = Invoke-RestMethod "https://api.github.com/repos/$Repo/tags?per_page=100" -Headers $headers
    }
    catch {
        throw "Nu pot accesa GitHub (fara internet sau repo privat). / Cannot reach GitHub (offline or private repo)."
    }
    $latest = $tags | Where-Object { $_.name -match '^v\d+\.\d+\.\d+$' } |
        ForEach-Object { [pscustomobject]@{ Tag = $_.name; Version = [version]$_.name.TrimStart('v') } } |
        Sort-Object Version -Descending | Select-Object -First 1
    if (-not $latest) { throw 'Nu exista nicio versiune publicata. / No published version found.' }
    Write-Host "ultima versiune / latest:   $($latest.Version)"

    if ($latest.Version -le $local -and -not $Force) {
        Write-Host ''
        Write-Host 'Ai deja ultima versiune. / Already up to date.' -ForegroundColor Green
        Read-Host 'Apasa Enter pentru a inchide / Press Enter to close'
        return
    }

    # download + extract into a fresh temp folder
    $work = Join-Path $env:TEMP "m1lator-update-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $work | Out-Null
    $zip = Join-Path $work 'update.zip'
    Write-Host "descarc / downloading $($latest.Tag) ..."
    Invoke-WebRequest "https://github.com/$Repo/archive/refs/tags/$($latest.Tag).zip" -OutFile $zip -Headers $headers -UseBasicParsing
    Expand-Archive $zip (Join-Path $work 'x')
    $src = Get-ChildItem (Join-Path $work 'x') -Directory | Select-Object -First 1
    if (-not (Test-Path (Join-Path $src.FullName 'bin\M1lator.Tool.exe'))) { throw 'Arhiva descarcata e incompleta. / Downloaded archive is incomplete.' }

    # replace program files in this folder (backups live in ProgramData and are not touched)
    foreach ($item in 'bin', 'scripts') {
        $dst = Join-Path $Root $item
        if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
        Copy-Item -Recurse (Join-Path $src.FullName $item) $dst
    }
    foreach ($file in 'Install.bat', 'Uninstall.bat', 'Update.bat', 'README.md', 'VERSION') {
        $s = Join-Path $src.FullName $file
        if (Test-Path $s) { Copy-Item $s (Join-Path $Root $file) -Force }
    }
    Remove-Item -Recurse -Force $work
    Write-Host "actualizat la / updated to $($latest.Version). Reinstalez... / Reinstalling..." -ForegroundColor Cyan

    # install.ps1 asks for admin (UAC) and pauses at the end
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'scripts\install.ps1')
}
catch {
    Write-Host ''
    Write-Host "EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host 'Apasa Enter pentru a inchide / Press Enter to close'
}
