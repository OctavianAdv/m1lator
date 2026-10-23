<#
  m1lator: pick or customise the UI theme. No admin needed.
    Theme.bat                       interactive menu (presets from scripts\themes\*.json, or a custom accent colour)
    theme.ps1 -Preset 1-liquid-glass   non-interactive (used by the installer)
  Preset JSON fields: name, accent (#RRGGBB), tint (#RRGGBB base surface), glass (0..1 chrome opacity; 1 = solid),
  radius (px), glassMode ("glass" = blurred-wallpaper base layer, "off"), dim (0..1 tint over the wallpaper).
  The theme takes effect the next time BlueStacks starts.
#>
param([string]$Preset, [switch]$NoPause)
. "$PSScriptRoot\common.ps1"

$themesDir = Join-Path $PSScriptRoot 'themes'
$presets = @(Get-ChildItem $themesDir -Filter *.json | Sort-Object Name | ForEach-Object {
    $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
    $j | Add-Member -NotePropertyName id -NotePropertyValue $_.BaseName -PassThru
})

function Show-Color([string]$hex) {
    # closest console colour for a quick preview swatch
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    $best = 'White'; $bestD = [double]::MaxValue
    foreach ($n in [Enum]::GetNames([ConsoleColor])) {
        $cc = [System.Drawing.Color]::FromName($(if ($n -eq 'DarkYellow') { 'Olive' } else { $n }))
        $d = [Math]::Pow($c.R - $cc.R, 2) + [Math]::Pow($c.G - $cc.G, 2) + [Math]::Pow($c.B - $cc.B, 2)
        if ($d -lt $bestD) { $bestD = $d; $best = $n }
    }
    Write-Host '  ##  ' -ForegroundColor $best -NoNewline
}

try {
    Add-Type -AssemblyName System.Drawing
    $chosen = $null
    if ($Preset) {
        $chosen = $presets | Where-Object { $_.id -eq $Preset } | Select-Object -First 1
        if (-not $chosen) { $chosen = $presets[0] }
    }
    else {
        $current = Get-M1Value 'ThemePreset'
        Write-Host ''
        Write-Host '  m1lator - teme / themes' -ForegroundColor Cyan
        Write-Host ''
        for ($i = 0; $i -lt $presets.Count; $i++) {
            $p = $presets[$i]
            Write-Host ("  {0}." -f ($i + 1)) -NoNewline
            Show-Color $p.accent
            $glass = if ($p.glassMode -ne 'off' -and $p.glass -lt 1) { 'glass' } else { 'solid' }
            Write-Host ("{0,-18} {1}  {2}" -f $p.name, $p.accent, $glass) -NoNewline
            if ($p.id -eq $current) { Write-Host '   <- actuala / current' -ForegroundColor DarkGray } else { Write-Host '' }
        }
        Write-Host ''
        Write-Host '  C. Culoare proprie / custom accent colour'
        Write-Host ''
        $answer = (Read-Host '  Alege / choose').Trim()
        if ($answer -match '^[cC]$') {
            $hex = (Read-Host '  Culoare accent (ex. #FF2D55)').Trim()
            if ($hex -notmatch '^#?[0-9A-Fa-f]{6}$') { throw "Culoare invalida: $hex (format #RRGGBB)" }
            if (-not $hex.StartsWith('#')) { $hex = '#' + $hex }
            $glassAns = (Read-Host '  Liquid Glass? (D/N)').Trim()
            $chosen = [pscustomobject]@{ id = 'custom'; name = 'Custom'; accent = $hex.ToUpper(); tint = '#1C1C1E'
                glass = $(if ($glassAns -match '^(d|da|y|yes)$') { 0.62 } else { 1.0 }); radius = 14
                glassMode = $(if ($glassAns -match '^(d|da|y|yes)$') { 'glass' } else { 'off' }); dim = 0.5 }
        }
        else {
            $n = 0
            if (-not [int]::TryParse($answer, [ref]$n) -or $n -lt 1 -or $n -gt $presets.Count) { throw "Alegere invalida: $answer" }
            $chosen = $presets[$n - 1]
        }
    }

    # generate the theme into Client\m1lator and select it
    $tools = Get-M1latorTools
    $inv = [Globalization.CultureInfo]::InvariantCulture
    & $tools.ThemeGen (Join-Path $Client 'Assets') (Join-Path $Client 'm1lator') `
        --name $chosen.name --accent $chosen.accent --tint $chosen.tint `
        --glass ([double]$chosen.glass).ToString($inv) --radius ([int]$chosen.radius).ToString($inv)
    if ($LASTEXITCODE) { throw 'generarea temei a esuat / theme generation failed' }
    Set-ItemProperty "$BsKey\Client" -Name 'ClientThemeName' -Value 'm1lator' -ErrorAction SilentlyContinue

    $glassOn = ($chosen.glassMode -ne 'off') -and ([double]$chosen.glass -lt 1)
    $dim = if ($chosen.PSObject.Properties['dim']) { [double]$chosen.dim } else { 0.5 }
    # doubles on purpose: [Math]::Min(1, 0.52) would pick the Int32 overload and round 0.52 up to 1
    $alpha = '{0:X2}' -f [int][Math]::Round(255.0 * [Math]::Max([double]0.2, [Math]::Min([double]1.0, [double]$dim)))
    Set-M1Value 'Glass' ([int]$glassOn) 'DWord'
    Set-M1Value 'GlassTint' ("#$alpha" + $chosen.tint.TrimStart('#'))
    Set-M1Value 'Accent' $chosen.accent
    Set-M1Value 'ThemePreset' $chosen.id

    Write-Host ''
    Write-Host "  Tema '$($chosen.name)' aplicata. Reporneste BlueStacks. / Theme applied, restart BlueStacks." -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "  EROARE / ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
if (-not $NoPause -and -not $Preset) { Read-Host '  Apasa Enter pentru a inchide / Press Enter to close' }
