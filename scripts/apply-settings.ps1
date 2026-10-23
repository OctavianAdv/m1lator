<#
  m1lator: settings (no binary patching).
  - generates the m1lator theme (preset chosen with Theme.bat, default Liquid Glass) and selects it
  - per-instance FPS / render / ASTC presets
  - renames the high-FPS checkbox to "High framerate mode"
  - turns off promotions, app recommendations, speed-up tips and timeline stats via bst_config
  Safe to re-run. Every value it overwrites is first saved to backup\settings-<timestamp>.json
  so restore-settings.ps1 can put things back.
#>
param(
    [int]$Fps = 30,              # starting FPS (also what HD-Player resets to on close); >60 turns High framerate mode on
    [switch]$ShowFps,            # native FPS overlay
    [int]$AstcOption = -1,       # -1 auto (3 if GPU supports it, else 2), 0 off, 1 software, 2 software+cache, 3 hardware
    [switch]$SkipTheme
)
. "$PSScriptRoot\common.ps1"
# needs admin (creates HKLM\...\m1lator). Parameters only apply when already started elevated.
if (Invoke-Elevated $PSCommandPath) { return }
New-Item -ItemType Directory -Force $BackupDir | Out-Null

if (Get-Process -Name 'Bluestacks', 'HD-Player' -ErrorAction SilentlyContinue) {
    throw 'Inchide BlueStacks mai intai / Close BlueStacks first.'
}

# snapshot of every value we overwrite, saved after each change so an interrupted run never loses originals
$backup = [ordered]@{}
$snapshot = Join-Path $BackupDir "settings-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
function Save-Snapshot { $backup | ConvertTo-Json | Set-Content $snapshot -Encoding UTF8 }
function Set-Reg($path, $name, $value, $type = 'DWord') {
    $old = (Get-ItemProperty $path -Name $name -ErrorAction SilentlyContinue).$name
    $backup["$path|$name"] = $old
    Save-Snapshot
    Set-ItemProperty -Path $path -Name $name -Value $value -Type $type
}

# ---------------------------------------------------------------- m1lator settings key
if (-not (Test-Path "$BsKey\m1lator")) { New-Item "$BsKey\m1lator" | Out-Null }
# Theme.bat (no admin) and HD-Player write values here: let Users set values (not create subkeys)
# (SID S-1-5-32-545 = BUILTIN\Users, works on any Windows language)
$acl = Get-Acl "$BsKey\m1lator"
$users = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-545'
$acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule($users, 'ReadKey, SetValue', 'Allow')))
Set-Acl "$BsKey\m1lator" $acl

# ---------------------------------------------------------------- theme (preset from Theme.bat, default Liquid Glass)
if (-not $SkipTheme) {
    Set-Reg "$BsKey\Client" 'ClientThemeName' 'm1lator' 'String'      # records the original for uninstall
    $preset = Get-M1Value 'ThemePreset'
    if (-not $preset -or -not (Test-Path (Join-Path $PSScriptRoot "themes\$preset.json"))) { $preset = '1-liquid-glass' }
    & (Join-Path $PSScriptRoot 'theme.ps1') -Preset $preset -NoPause
}

# ---------------------------------------------------------------- fonts + branding (read by M1lator.Client.dll)
$fontDst = Join-Path $Client 'm1lator-fonts'
if (Test-Path $fontDst) { Remove-Item -Recurse -Force $fontDst }
Copy-Item -Recurse (Join-Path $PSScriptRoot 'fonts') $fontDst
$version = if (Test-Path (Join-Path $Root 'VERSION')) { (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim() } else { '' }
Set-ItemProperty "$BsKey\m1lator" -Name 'Version' -Value $version -Type String
if (-not (Get-ItemProperty "$BsKey\m1lator" -Name 'Font' -ErrorAction SilentlyContinue)) {
    Set-ItemProperty "$BsKey\m1lator" -Name 'Font' -Value 'Geist' -Type String   # keep a font the user picked
}
Write-Host "branding: Geist font, RGB M1LATOR title, m1lator $version in About"

# ---------------------------------------------------------------- remove the power plan from v1.8 / v1.9
# (users run Ultimate Performance themselves; m1lator no longer switches plans)
$oldPlan = Get-M1Value 'PowerScheme'
if ($oldPlan) {
    if ((powercfg /getactivescheme | Out-String) -match [regex]::Escape($oldPlan)) {
        # back to the user's plan: the one saved by v1.8/v1.9, else their own Ultimate Performance, else any custom
        # (non built-in) plan, else Windows' High performance
        $plans = @(powercfg /list | Select-String 'GUID: ([0-9a-fA-F-]{36})\s+\((.*)\)' | ForEach-Object {
            [pscustomobject]@{ Guid = $_.Matches[0].Groups[1].Value; Name = $_.Matches[0].Groups[2].Value.TrimEnd(' ', '*', ')') } } |
            Where-Object { $_.Guid -ne $oldPlan })
        $builtin = '381b4222-f694-41f0-9685-ff5bb260df2e', '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', 'a1841308-3541-4fab-bc81-f71556f20b4a'
        $prev = Get-M1Value 'PreviousPowerScheme'
        $back = ($plans | Where-Object { $_.Guid -eq $prev } | Select-Object -First 1).Guid
        if (-not $back) { $back = ($plans | Where-Object { $_.Name -match 'Ultimate' } | Select-Object -First 1).Guid }
        if (-not $back) { $back = ($plans | Where-Object { $builtin -notcontains $_.Guid } | Select-Object -First 1).Guid }
        if (-not $back) { $back = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' }
        powercfg /setactive $back | Out-Null
    }
    powercfg /delete $oldPlan 2>$null | Out-Null
    Remove-ItemProperty "$BsKey\m1lator" -Name 'PowerScheme', 'PreviousPowerScheme' -ErrorAction SilentlyContinue
    Write-Host 'power plan: removed the m1lator plan (your own plan stays active)'
}

# ---------------------------------------------------------------- BlueStacks cloud off
# Every BlueStacks server call (stats, usage, GRM rules, post-boot, promotions, country lookups...) is built from
# Config\Host. A closed local port makes them all fail instantly. Play Store / games are unaffected (they go
# through Android). Lost: cloud device-profile list, help pages. Undo: Uninstall.bat restores the original Host.
Set-Reg "$BsKey\Config" 'Host' 'http://127.0.0.1:9' 'String'

# ---------------------------------------------------------------- remaining telemetry switches (registry-gated)
Set-Reg "$BsKey\Config" 'SystemInfoStats2' 0          # device capabilities report at every boot
Set-Reg "$BsKey\Config" 'SystemStats' 0
Set-Reg "$BsKey\Config" 'IsTimelineStats4Enabled' 0
if (-not (Get-ItemProperty "$BsKey\m1lator" -Name 'Dpi' -ErrorAction SilentlyContinue)) {
    Set-ItemProperty "$BsKey\m1lator" -Name 'Dpi' -Value 440 -Type DWord   # M1lator.Boost forces this at every start
}
$dpi = (Get-ItemProperty "$BsKey\m1lator").Dpi

# ---------------------------------------------------------------- home page (Library only, no Game center)
# BlueStacks' own JS bridge/images/translations come from the local install; our page + style go on top.
# The patched client loads Client\m1lator-home\index.html instead of the cloud home when it exists.
$offline = Join-Path $Client 'OfflineHtmlHome'
$homeDst = Join-Path $Client 'm1lator-home'
if (Test-Path "$offline\js\index.js") {
    if (Test-Path $homeDst) { Remove-Item -Recurse -Force $homeDst }
    New-Item -ItemType Directory $homeDst | Out-Null
    foreach ($d in 'js', 'images', 'localization') { Copy-Item -Recurse (Join-Path $offline $d) $homeDst }
    Copy-Item (Join-Path $PSScriptRoot 'home\*') $homeDst -Force    # lives under scripts\ so every updater version ships it
    Write-Host 'home: Library only (Game center off)'
}

# ---------------------------------------------------------------- per-instance performance presets
# vCPUs run inside HD-Player next to the renderer and input threads: leave 2 performance cores free for them
$tools = Get-M1latorTools
# Instances start in the same state the patched HD-Player leaves them in when it closes:
# FPS 30, High framerate mode off (turn it on per session in Settings -> Engine, slider up to 1000).
$highFps = [int]($Fps -gt 60)
$pcores = [int]((& $tools.Tool pcores) | Select-Object -First 1)
$pphys = [int]((& $tools.Tool pcores-physical) | Select-Object -First 1)
# with Hyper-Threading one whole P-core stays free (render + input threads don't share it with a vCPU),
# without it two P-cores stay free. e.g. 6P+HT -> 5, 8P (HT off) -> 6, 8P+HT -> 6 (cap)
$reserve = if ($pcores -gt $pphys) { 1 } else { 2 }
$vcpus = [Math]::Max(2, [Math]::Min(6, $pphys - $reserve))
Write-Host "performance cores: $pphys physical / $pcores threads -> $vcpus vCPUs per instance"

foreach ($vm in (Get-ItemProperty "$BsKey\Config").VmList) {
    $g = "$BsKey\Guests\$vm"
    if (-not (Test-Path "$g\Config")) { continue }
    Set-Reg "$g\Config" 'VCPUs' $vcpus
    Set-Reg "$g\Config" 'EnableHighFPS' $highFps
    Set-Reg "$g\Config" 'FPS' $Fps
    Set-Reg "$g\Config" 'ShowFPS' ([int][bool]$ShowFps)
    Set-Reg "$g\Config" 'GlRenderMode' 1          # OpenGL
    # ASTC textures (Unity games): hardware decode when BlueStacks detected GPU support (e.g. Intel iGPU),
    # otherwise software decode + cache so each texture is decoded only once per session
    $astc = if ($AstcOption -ge 0) { $AstcOption }
            elseif ((Get-ItemProperty $g -Name 'IsHardwareAstcSupported' -ErrorAction SilentlyContinue).IsHardwareAstcSupported -eq 1) { 3 }
            else { 2 }
    Set-Reg $g 'ASTCOption' $astc

    # boot params carry fps= to the guest (BlueStacks adds it the same way)
    $bp = (Get-ItemProperty $g).BootParameters
    $new = if ($bp -match '(^| )fps=\d+') { $bp -replace '(^| )fps=\d+', "`$1fps=$Fps" } else { "$bp fps=$Fps" }
    if ($dpi -gt 0) { $new = if ($new -match '(^| )DPI=\d+') { $new -replace '(^| )DPI=\d+', "`$1DPI=$dpi" } else { "$new DPI=$dpi" } }
    Set-Reg $g 'BootParameters' $new 'String'
    Write-Host "$vm : $vcpus vCPUs, DPI=$dpi, FPS=$Fps, high framerate mode $(if ($highFps) { 'on' } else { 'off' }), ASTC=$astc, OpenGL"
}
Set-Reg "$BsKey\Config" 'ForceDedicatedGPU' 1

# Windows per-app GPU preference: laptops with iGPU + dGPU otherwise often render BlueStacks on the weak iGPU
$gpuKey = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
if (-not (Test-Path $gpuKey)) { New-Item $gpuKey -Force | Out-Null }
foreach ($exe in (Join-Path $PF 'HD-Player.exe'), (Join-Path $Client 'Bluestacks.exe')) {
    Set-Reg $gpuKey $exe 'GpuPreference=2;' 'String'
}
Write-Host 'GPU: high performance for HD-Player / Bluestacks'

# ---------------------------------------------------------------- shortcuts: only fullscreen (F11)
# BlueStacks loads UserDefinedShortcuts instead of DefaultShortcuts when it is set; anything missing is simply inactive
$shortcuts = @{
    Shortcut        = @(@{ ShortcutCategory = 'STRING_WINDOW_ACTIONS'; ShortcutName = 'STRING_UPDATED_FULLSCREEN_BUTTON_TOOLTIP'; ShortcutKey = 'F11' })
    DefaultModifier = 'LeftCtrl,LeftShift'
} | ConvertTo-Json -Depth 4
Set-Reg "$BsKey\Config" 'UserDefinedShortcuts' $shortcuts 'String'
Set-Reg "$BsKey\Client" 'BossKey' '' 'String'          # global hide-window hotkey off
Write-Host 'shortcuts: only F11 (fullscreen)'

# ---------------------------------------------------------------- fullscreen stretch (patched HD-Player reads this)
if (-not (Test-Path "$BsKey\m1lator")) { New-Item "$BsKey\m1lator" | Out-Null }
Set-Reg "$BsKey\m1lator" 'StretchFullscreen' 1
Set-Reg "$BsKey\Config" 'UpdateBstConfig' 0      # keep our local bst_config instead of re-downloading

# ---------------------------------------------------------------- UI label
$loc = Join-Path $DataDir 'Locales\i18n.en-US.txt'
if (Test-Path $loc) {
    $text = [IO.File]::ReadAllText($loc)
    $backup['locale:STRING_ENABLE_HIGH_FPS'] = ([regex]::Match($text, '(?m)^STRING_ENABLE_HIGH_FPS = (.*)$').Groups[1].Value).TrimEnd("`r")
    Save-Snapshot
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
        IsShowNotificationCentre      = 'false'   # bell icon + notification drawer
        IsAppCenterTabVisible         = 'false'   # App Center / Game center tab
        IsShowDiscordPreference       = 'false'   # Preferences clutter
        IsShowDesktopShortcutPreference = 'false'
        IsShowHelpCenter              = 'false'
        ShowBeginnersGuidePreference  = 'false'
        IsTopbarHelpEnabled           = 'false'   # "?" button in the top bar
        IsUserAccountBtnEnabled       = 'false'   # account button in the top bar
        IsCustomResolutionInputAllowed = 'true'   # lets you type e.g. 1280x960 in Settings -> Display
    }
    foreach ($k in $flags.Keys) {
        $node = $cfg.FeatureManager.SelectSingleNode($k)
        if (-not $node) { $node = $cfg.FeatureManager.AppendChild($cfg.CreateElement($k)) }
        $node.InnerText = $flags[$k]
    }
    $cfg.Save($cfgPath)
    Write-Host 'promotions, recommendations, tips, timeline stats, notification centre: off'
}

Save-Snapshot
Write-Host "settings applied (backup: $snapshot)"
