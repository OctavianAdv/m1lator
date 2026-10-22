<#
  m1lator: settings that need NO admin rights and NO binary patching.
  - installs the m1lator Midnight theme into the client and selects it
  - per-instance FPS / render / ASTC presets
  - renames the high-FPS checkbox to "High framerate mode"
  - turns off promotions, app recommendations, speed-up tips and timeline stats via bst_config feature flags
  Run with BlueStacks closed. Safe to re-run. Every value it overwrites is first saved to
  backup\settings-<timestamp>.json, so restore-settings.ps1 can put things back.
#>
param(
    [int]$Fps = 240,             # default cap per instance; the slider can go higher once the patch is installed
    [switch]$ShowFps,            # native FPS overlay
    [int]$AstcOption = 2,        # 0 off, 1 software, 2 software+cache, 3 hardware (RTX 20xx has no HW ASTC)
    [switch]$SkipTheme
)
$ErrorActionPreference = 'Stop'
$Repo     = Split-Path $PSScriptRoot -Parent
$BsKey    = 'HKLM:\SOFTWARE\BlueStacks_bgp64'
$Client   = (Get-ItemProperty "$BsKey\Client").ClientInstallDir
$DataDir  = Split-Path $Client -Parent
$BackupDir = Join-Path $Repo 'backup'
New-Item -ItemType Directory -Force $BackupDir | Out-Null

if (Get-Process -Name 'Bluestacks','HD-Player' -ErrorAction SilentlyContinue) {
    throw 'Close BlueStacks (client and all instances) first.'
}

$backup = [ordered]@{}
function Set-Reg($path, $name, $value, $type = 'DWord') {
    $old = (Get-ItemProperty $path -Name $name -ErrorAction SilentlyContinue).$name
    $backup["$path|$name"] = $old
    Set-ItemProperty -Path $path -Name $name -Value $value -Type $type
}

# ---------------------------------------------------------------- theme
if (-not $SkipTheme) {
    $themeSrc = Join-Path $Repo 'build\theme\m1lator'
    if (-not (Test-Path "$themeSrc\ThemeFile")) {
        dotnet run -c Release --project (Join-Path $Repo 'src\M1lator.ThemeGen') -- (Join-Path $Client 'Assets') $themeSrc
    }
    $themeDst = Join-Path $Client 'm1lator'
    if (Test-Path $themeDst) { Remove-Item -Recurse -Force $themeDst }
    Copy-Item -Recurse $themeSrc $themeDst
    Set-Reg "$BsKey\Client" 'ClientThemeName' 'm1lator' 'String'
    Write-Host "theme installed -> $themeDst"
}

# ---------------------------------------------------------------- per-instance performance presets
foreach ($vm in (Get-ItemProperty "$BsKey\Config").VmList) {
    $g = "$BsKey\Guests\$vm"
    if (-not (Test-Path "$g\Config")) { continue }
    Set-Reg "$g\Config" 'EnableHighFPS' 1
    Set-Reg "$g\Config" 'FPS' $Fps
    Set-Reg "$g\Config" 'ShowFPS' ([int][bool]$ShowFps)
    Set-Reg "$g\Config" 'GlRenderMode' 1          # OpenGL: best path on NVIDIA
    Set-Reg $g 'ASTCOption' $AstcOption

    # boot params carry fps= to the guest (BlueStacks adds it the same way)
    $bp = (Get-ItemProperty $g).BootParameters
    $new = if ($bp -match '(^| )fps=\d+') { $bp -replace '(^| )fps=\d+', "`$1fps=$Fps" } else { "$bp fps=$Fps" }
    Set-Reg $g 'BootParameters' $new 'String'
    Write-Host "$vm : FPS=$Fps high-fps=on ASTC=$AstcOption OpenGL"
}
Set-Reg "$BsKey\Config" 'ForceDedicatedGPU' 1
Set-Reg "$BsKey\Config" 'UpdateBstConfig' 0      # keep our local bst_config instead of re-downloading

# ---------------------------------------------------------------- UI label
$loc = Join-Path $DataDir 'Locales\i18n.en-US.txt'
$text = [IO.File]::ReadAllText($loc)
$backup['locale:STRING_ENABLE_HIGH_FPS'] = ([regex]::Match($text, '(?m)^STRING_ENABLE_HIGH_FPS = (.*)$').Groups[1].Value).TrimEnd("`r")
$text = [regex]::Replace($text, '(?m)^STRING_ENABLE_HIGH_FPS = .*?(\r?)$', 'STRING_ENABLE_HIGH_FPS = High framerate mode$1')
[IO.File]::WriteAllText($loc, $text, (New-Object Text.UTF8Encoding $true))

# ---------------------------------------------------------------- de-bloat via official feature flags
$cfgPath = Join-Path $Client 'bst_config'
Copy-Item $cfgPath (Join-Path $BackupDir 'bst_config.orig') -ErrorAction SilentlyContinue
[xml]$cfg = Get-Content $cfgPath -Raw
$flags = [ordered]@{
    IsPromotionDisabled       = 'true'
    IsShowAppRecommendations  = 'false'
    IsShowSpeedUpTips         = 'false'
    IsTimelineStatsEnabled    = 'false'
    IsShowPostOTSScreen       = 'false'
    IsShowGamingSummaryPreference = 'false'
}
foreach ($k in $flags.Keys) {
    $node = $cfg.FeatureManager.SelectSingleNode($k)
    if (-not $node) { $node = $cfg.FeatureManager.AppendChild($cfg.CreateElement($k)) }
    $node.InnerText = $flags[$k]
}
$cfg.Save($cfgPath)
Write-Host "bst_config: promotions/recommendations/tips/timeline stats off"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup | ConvertTo-Json | Set-Content (Join-Path $BackupDir "settings-$stamp.json") -Encoding UTF8
Write-Host "done. previous values saved to backup\settings-$stamp.json"
