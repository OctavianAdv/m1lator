<#
  m1lator: performance diagnosis. Writes m1lator-diagnoza-<date>.txt on the Desktop and opens it.
  Every check prints OK / PROBLEMA / INFO with what to do. No admin needed, changes nothing.
#>
. "$PSScriptRoot\common.ps1"
$ErrorActionPreference = 'Continue'

$out = New-Object System.Collections.Generic.List[string]
$problems = 0
function Line($s = '') { $out.Add($s) }
function Ok($s) { $out.Add("  [OK]       $s") }
function Info($s) { $out.Add("  [INFO]     $s") }
function Bad($s, $fix) { $out.Add("  [PROBLEMA] $s"); $out.Add("             -> $fix"); $script:problems++ }
function Section($s) { $out.Add(''); $out.Add("=== $s") }
function Reg($path, $name) { (Get-ItemProperty $path -Name $name -ErrorAction SilentlyContinue).$name }

$version = if (Test-Path "$Root\VERSION") { (Get-Content "$Root\VERSION" -Raw).Trim() } else { '?' }
Line "m1lator $version - diagnoza $(Get-Date -Format 'yyyy-MM-dd HH:mm')"

# ---------------------------------------------------------------- virtualization
Section 'Virtualizare'
$cs = Get-CimInstance Win32_ComputerSystem
$dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
if ($cs.HypervisorPresent) {
    $why = if ($dg -and $dg.SecurityServicesRunning -contains 2) { 'Memory Integrity (Core Isolation) e pornit' } else { 'Hyper-V / WSL2 / Virtual Machine Platform' }
    Bad "Hypervisor-ul Windows ruleaza ($why). BlueStacks 4 nu porneste / merge foarte greu." 'Ruleaza Hypervisor-Off.bat si reporneste PC-ul.'
} else { Ok 'Hypervisor-ul Windows e oprit' }

$caps = Reg "$BsKey\Config" 'DeviceCaps'
$engine = Reg "$BsKey\Config" 'CurrentEngine'
try { $capsJson = $caps | ConvertFrom-Json } catch { $capsJson = $null }
if ($capsJson -and $capsJson.bios_hvm -eq 'False') {
    Bad 'Virtualizarea (VT-x / AMD-V / SVM) e OPRITA in BIOS.' 'Intra in BIOS si porneste Intel Virtualization Technology (VT-x) sau SVM Mode (AMD).'
}
if ($engine -eq 'raw') {
    Bad 'BlueStacks ruleaza in modul "raw": 1 singur nucleu, fara virtualizare hardware -> FPS mic, scaderi mari in joc.' 'Porneste VT-x/SVM in BIOS si opreste hypervisor-ul Windows (Hypervisor-Off.bat).'
} elseif ($engine) { Ok "Engine BlueStacks: $engine (virtualizare hardware)" }

# ---------------------------------------------------------------- CPU / RAM / power
Section 'Procesor / memorie / alimentare'
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$pcores = try { [int]((& (Join-Path $Root 'bin\M1lator.Tool.exe') pcores) | Select-Object -First 1) } catch { 0 }
$pphys = try { [int]((& (Join-Path $Root 'bin\M1lator.Tool.exe') pcores-physical) | Select-Object -First 1) } catch { 0 }
$recVcpu = if ($pphys -gt 0) { [Math]::Max(2, [Math]::Min(6, $pphys - $(if ($pcores -gt $pphys) { 1 } else { 2 }))) } else { 0 }
Info "$($cpu.Name.Trim()) - $($cpu.NumberOfCores) nuclee, $($cpu.NumberOfLogicalProcessors) thread-uri, P-cores: $pcores"
$os = Get-CimInstance Win32_OperatingSystem
$totalGb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1); $freeGb = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
if ($totalGb -lt 8) { Bad "RAM total: $totalGb GB" 'Minim 8 GB pentru Standoff 2 pe emulator; inchide alte programe (browser, Discord).' } else { Ok "RAM total: $totalGb GB (liber acum: $freeGb GB)" }
Info "Plan de alimentare: $((powercfg /getactivescheme) -replace '.*\((.*)\).*', '$1')"
$bat = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue
if ($bat -and $bat.BatteryStatus -eq 1) { Bad 'Laptopul merge pe baterie: procesorul si placa video sunt limitate.' 'Baga laptopul in priza.' }

# ---------------------------------------------------------------- GPU
Section 'Placa video'
$gpus = @(Get-CimInstance Win32_VideoController)
foreach ($g in $gpus) { Info "$($g.Name) - driver $($g.DriverVersion), $($g.CurrentRefreshRate) Hz" }
$used = Reg "$BsKey\Config" 'AvailableGPUDetails'
if ($used) { Info "BlueStacks a randat ultima data pe: $used" }
$dgpu = $gpus | Where-Object { $_.Name -match 'NVIDIA|GeForce|RTX|GTX|Radeon RX|Radeon Pro|Arc' }
if ($used -match 'Basic Render|GDI Generic|llvmpipe|SwiftShader') {
    Bad 'BlueStacks randeaza in software (fara driver video).' 'Instaleaza driverul placii video de pe site-ul NVIDIA / AMD / Intel.'
} elseif ($dgpu -and $used -and $used -match 'Intel|UHD|Iris|Radeon\(TM\) Graphics|Vega' -and $used -notmatch 'Arc') {
    Bad "BlueStacks foloseste placa video integrata ($used), nu pe cea dedicata ($($dgpu[0].Name))." 'Ruleaza din nou Install.bat (seteaza GPU de performanta) si reporneste BlueStacks.'
} elseif ($used) { Ok 'Placa video folosita de BlueStacks pare corecta' }
$pref = Reg 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' (Join-Path $PF 'HD-Player.exe')
if ($pref -eq 'GpuPreference=2;') { Ok 'Windows: HD-Player setat pe GPU de performanta' } else { Info 'Windows: HD-Player nu are preferinta de GPU setata' }

# ---------------------------------------------------------------- disk
Section 'Disc'
try {
    $letter = (Split-Path $DataDir -Qualifier).TrimEnd(':')
    $disk = Get-Partition -DriveLetter $letter -ErrorAction Stop | Get-Disk -ErrorAction Stop
    $pd = Get-PhysicalDisk -ErrorAction Stop | Where-Object { $_.DeviceId -eq "$($disk.Number)" }
    if ($pd.MediaType -eq 'HDD') { Bad "Datele BlueStacks sunt pe HDD ($($disk.FriendlyName)): incarcarea modelelor/texturilor da sacadari." 'Muta/instaleaza BlueStacks pe un SSD.' }
    else { Ok "Datele BlueStacks sunt pe $($pd.MediaType) ($($disk.FriendlyName))" }
} catch { Info 'Tipul discului nu a putut fi citit' }

# ---------------------------------------------------------------- Windows extras
Section 'Windows'
if ((Reg 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled') -eq 1 -or (Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled') -eq 1) {
    Bad 'Xbox Game Bar / Captures (inregistrare in fundal) e pornit: poate da sacadari.' 'Setari Windows -> Gaming -> Captures -> opreste inregistrarea in fundal.'
} else { Ok 'Inregistrarea in fundal (Game DVR) e oprita' }
$hags = Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'
Info "Hardware-accelerated GPU scheduling: $(if ($hags -eq 2) { 'pornit' } else { 'oprit' })"

# ---------------------------------------------------------------- instances
Section 'Instante BlueStacks'
foreach ($vm in (Reg "$BsKey\Config" 'VmList')) {
    $g = "$BsKey\Guests\$vm"; $c = "$g\Config"
    if (-not (Test-Path $c)) { continue }
    $mem = Reg $g 'Memory'; $vcpu = Reg $c 'VCPUs'
    $w = Reg "$g\FrameBuffer\0" 'GuestWidth'; $h = Reg "$g\FrameBuffer\0" 'GuestHeight'
    $dpi = if ((Reg $g 'BootParameters') -match 'DPI=(\d+)') { $Matches[1] } else { '?' }
    Info ("{0} ({1}): {2} vCPU, {3} MB RAM, {4}x{5} DPI {6}, FPS {7} (high fps {8}), ASTC {9}, render {10}, GL mode {11}" -f $vm, (Reg $c 'DisplayName'), $vcpu, $mem, $w, $h, $dpi, (Reg $c 'FPS'), (Reg $c 'EnableHighFPS'), (Reg $g 'ASTCOption'), $(if ((Reg $c 'GlRenderMode') -eq 4) { 'DirectX' } else { 'OpenGL' }), (Reg $c 'GlMode'))
    if ($mem -and $mem -lt 3072) { Bad "$vm are doar $mem MB RAM." 'Setari BlueStacks -> Engine -> Memory: 4096 MB (daca PC-ul are 8 GB+).' }
    if ($vcpu -and $recVcpu -and $vcpu -gt $recVcpu) { Bad "$vm are $vcpu vCPU (recomandat $recVcpu): fire de randare/input sufocate -> input lag, FPS instabil." "Ruleaza din nou Install.bat, sau Setari -> Engine -> CPU cores: $recVcpu." }
    elseif ($vcpu -and $recVcpu -and $vcpu -lt $recVcpu - 1) { Bad "$vm are doar $vcpu vCPU (recomandat $recVcpu)." 'Ruleaza din nou Install.bat (seteaza automat).' }
    if ($w -and $h -and ([int]$w * [int]$h) -gt 1920 * 1080) { Info "$vm ruleaza peste 1080p: FPS mai mic. Pentru FPS maxim incearca 1280x720 sau 1280x960." }
}

# ---------------------------------------------------------------- m1lator install state
Section 'm1lator'
if ((Reg "$BsKey\Config" 'Host') -eq 'http://127.0.0.1:9') { Ok 'Cloud BlueStacks blocat (fara telemetrie)' } else { Bad 'Cloud-ul BlueStacks e activ (telemetrie).' 'Ruleaza Update.bat / Install.bat (versiunea 1.9+).' }
foreach ($f in (Join-Path $PF 'HD-Player.exe'), (Join-Path $PF 'HD-Common.dll'), (Join-Path $Client 'Bluestacks.exe')) {
    if ((Get-AuthenticodeSignature $f).Status -eq 'Valid') { Bad "$(Split-Path $f -Leaf) e originalul (patch-urile nu sunt instalate)." 'Ruleaza Install.bat.' }
}
if (Test-Path (Join-Path $PF 'M1lator.Boost.dll')) { Ok 'M1lator.Boost instalat' } else { Bad 'M1lator.Boost lipseste.' 'Ruleaza Install.bat.' }
$logs = Join-Path $DataDir 'Logs'
$boost = Get-Content (Join-Path $logs 'm1lator.log') -Tail 3 -ErrorAction SilentlyContinue
if ($boost) { $boost | ForEach-Object { Info "boost: $_" } } else { Info 'm1lator.log nu exista inca (BlueStacks nu a pornit de la instalare).' }

# ---------------------------------------------------------------- Google sign-in (runs inside Android)
Section 'Google Play / cont Google'
$playerLog = Join-Path $logs 'Player.log'
$pkgLine = Get-Content $playerLog -Tail 6000 -ErrorAction SilentlyContinue | Select-String 'installed_packages' | Select-Object -Last 1
if ($pkgLine -and $pkgLine.Line -match '"package":"com\.(android|google\.android)\.webview"[^}]*"versionName":"(\d+)[^"]*"') {
    $wv = [int]$Matches[2]
    if ($wv -lt 80) { Bad "Android System WebView $wv (foarte vechi): Google poate refuza autentificarea din el." 'Vezi in README sectiunea "Google Play: authentication failed".' }
    else { Ok "Android System WebView $wv" }
}
$adb = Join-Path $PF 'HD-Adb.exe'
$running = @(Get-Process HD-Player -ErrorAction SilentlyContinue)
if (-not $running) { Info 'Porneste instanta si incearca logarea, apoi ruleaza din nou Diagnose.bat ca sa vad eroarea exacta din Android.' }
elseif (Test-Path $adb) {
    foreach ($vm in (Reg "$BsKey\Config" 'VmList')) {
        $port = Reg "$BsKey\Guests\$vm\Network\Redirect" 'tcp/5555'
        if (-not $port) { continue }
        $target = "127.0.0.1:$port"
        $null = & $adb connect $target 2>&1
        $date = (& $adb -s $target shell date 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $date -match 'error|unable|refused|not found|offline') { Info "$vm : adb indisponibil (Setari BlueStacks -> Preferinte -> Android Debug Bridge)."; continue }
        Info "$vm : data/ora in Android: $date (PC: $(Get-Date -Format 'ddd MMM dd HH:mm:ss yyyy'))"
        $auth = & $adb -s $target logcat -d -v time 2>$null |
            Select-String -Pattern 'GLSUser|GoogleAuth|BadAuthentication|NeedsBrowser|AuthenticationFailed|Auth\w*Exception|MinuteMaid|Webview.*(old|update)|SSLHandshake|CertPathValidator|NetworkError|DeviceManagementRequired|AccountManager.*(error|fail)' |
            Select-Object -Last 25
        if ($auth) { Info "$vm : erori de autentificare din Android:"; $auth | ForEach-Object { Line "      $($_.Line)" } }
        else { Info "$vm : nicio eroare de autentificare in log (incearca logarea, apoi ruleaza din nou Diagnose.bat)." }
    }
}

Section "Rezumat: $problems problem$(if ($problems -eq 1) { 'a' } else { 'e' }) gasit$(if ($problems -eq 1) { 'a' } else { 'e' })"

# ---------------------------------------------------------------- raw log excerpts for debugging
Section 'Player.log (extras)'
Get-Content (Join-Path $logs 'Player.log') -Tail 4000 -ErrorAction SilentlyContinue |
    Select-String -Pattern 'GlVendor|GlRenderer|GlVersion|HyperV|raw|Exiting|VCPUs|memory to|isHwAstc|gl3|ERROR|Exception' |
    Select-Object -Last 60 | ForEach-Object { Line "  $($_.Line)" }

$file = Join-Path ([Environment]::GetFolderPath('Desktop')) "m1lator-diagnoza-$(Get-Date -Format 'yyyyMMdd-HHmm').txt"
$out | Set-Content $file -Encoding UTF8
Write-Host "Diagnoza salvata pe Desktop: $file  ($problems probleme)"
Start-Process notepad.exe $file
