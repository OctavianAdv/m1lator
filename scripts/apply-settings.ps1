<#
  m1lator: settings (no binary patching).
  - generates the m1lator Midnight theme from the local stock theme, installs and selects it
  - per-instance FPS / render / ASTC presets
  - renames the high-FPS checkbox to "High framerate mode"
  - turns off promotions, app recommendations, speed-up tips and timeline stats via bst_config
  Safe to re-run. Every value it overwrites is first saved to backup\settings-<timestamp>.json
  so restore-settings.ps1 can put things back.
#>
param(
    [int]$Fps = 240,             # default cap per instance; the slider goes to 1000 once patches are installed
    [switch]$ShowFps,            # native FPS overlay
    [int]$AstcOption = 2,        # 0 off, 1 software, 2 software+cache, 3 hardware
    [switch]$SkipTheme
)
. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force $BackupDir | Out-Null

if (Get-Process -Name 'Bluestacks', 'HD-Player' -ErrorAction SilentlyContinue) {
    throw 'Inchide BlueStacks mai intai / Close BlueStacks first.'
}

$backup = [ordered]@{}
function Set-Reg($path, $name, $value, $type = 'DWord') {
    $old = (Get-ItemProperty $path -Name $name -ErrorAction SilentlyContinue).$name
    $backup["$path|$name"] = $old
    Set-ItemProperty -Path $path -Name $name -Value $value -Type $type
}

# ---------------------------------------------------------------- theme
if (-not $SkipTheme) {
    $tools = Get-M1latorTools
    $themeDst = Join-Path $Client 'm1lator'
    & $tools.ThemeGen (Join-Path $Client 'Assets') $themeDst
    if ($LASTEXITCODE) { throw 'theme generation failed' }
    Set-Reg "$BsKey\Client" 'ClientThemeName' 'm1lator' 'String'
}

# ---------------------------------------------------------------- per-instance performance presets
foreach ($vm in (Get-ItemProperty "$BsKey\Config").VmList) {
    $g = "$BsKey\Guests\$vm"
    if (-not (Test-Path "$g\Config")) { continue }
    Set-Reg "$g\Config" 'EnableHighFPS' 1
    Set-Reg "$g\Config" 'FPS' $Fps
    Set-Reg "$g\Config" 'ShowFPS' ([int][bool]$ShowFps)
    Set-Reg "$g\Config" 'GlRenderMode' 1          # OpenGL
    Set-Reg $g 'ASTCOption' $AstcOption

    # boot params carry fps= to the guest (BlueStacks adds it the same way)
    $bp = (Get-ItemProperty $g).BootParameters
    $new = if ($bp -match '(^| )fps=\d+') { $bp -replace '(^| )fps=\d+', "`$1fps=$Fps" } else { "$bp fps=$Fps" }
    Set-Reg $g 'BootParameters' $new 'String'
    Write-Host "$vm : FPS=$Fps, high framerate mode on, ASTC=$AstcOption, OpenGL"
}
Set-Reg "$BsKey\Config" 'ForceDedicatedGPU' 1
Set-Reg "$BsKey\Config" 'UpdateBstConfig' 0      # keep our local bst_config instead of re-downloading

# ---------------------------------------------------------------- UI label
$loc = Join-Path $DataDir 'Locales\i18n.en-US.txt'
if (Test-Path $loc) {
    $text = [IO.File]::ReadAllText($loc)
    $backup['locale:STRING_ENABLE_HIGH_FPS'] = ([regex]::Match($text, '(?m)^STRING_ENABLE_HIGH_FPS = (.*)$').Groups[1].Value).TrimEnd("`r")
    $text = [regex]::Replace($text, '(?m)^STRING_ENABLE_HIGH_FPS = .*?(\r?)$', 'STRING_ENABLE_HIGH_FPS = High framerate mode$1')
    [IO.File]::WriteAllText($loc, $text, (New-Object Text.UTF8Encoding $true))
}

# ---------------------------------------------------------------- de-bloat via official feature flags
$cfgPath = Join-Path $Client 'bst_config'
if (Test-Path $cfgPath) {
    if (-not (Test-Path "$BackupDir\bst_config.orig")) { Copy-Item $cfgPath "$BackupDir\bst_config.orig" }
    [xml]$cfg = Get-Content $cfgPath -Raw
    $flags = [ordered]@{
        IsPromotionDisabled           = 'true'
        IsShowAppRecommendations      = 'false'
        IsShowSpeedUpTips             = 'false'
        IsTimelineStatsEnabled        = 'false'
        IsShowPostOTSScreen           = 'false'
        IsShowGamingSummaryPreference = 'false'
    }
    foreach ($k in $flags.Keys) {
        $node = $cfg.FeatureManager.SelectSingleNode($k)
        if (-not $node) { $node = $cfg.FeatureManager.AppendChild($cfg.CreateElement($k)) }
        $node.InnerText = $flags[$k]
    }
    $cfg.Save($cfgPath)
    Write-Host 'promotions, recommendations, tips, timeline stats: off'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup | ConvertTo-Json | Set-Content (Join-Path $BackupDir "settings-$stamp.json") -Encoding UTF8
Write-Host "settings applied (backup: backup\settings-$stamp.json)"
