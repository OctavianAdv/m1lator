# m1lator

Mod pentru **BlueStacks 4 (versiunea Nougat 64-bit, `bgp64`)**: temă nouă, FPS până la 1000, latență mai mică, fără telemetrie și reclame.

Nu trebuie să instalezi nimic în plus: nici .NET, nici altceva.

## Instalare

1. Descarcă proiectul, în orice mod: **[Releases](../../releases/latest)** (oricare zip), sau butonul verde **Code → Download ZIP**.
2. Click dreapta pe zip → **Extract All** (dezarhivează-l, nu rula direct din zip).
3. Închide BlueStacks.
4. Dublu-click pe **`Install.bat`** → apasă **Da** la fereastra Windows (drepturi de administrator) → așteaptă mesajul **GATA!**

Pornește BlueStacks. Gata.

> Fișierele originale se salvează automat în `C:\ProgramData\m1lator\backup`. Poți șterge folderul descărcat după instalare.

## Actualizare

Dublu-click pe **`Update.bat`**. Verifică dacă a apărut o versiune nouă pe GitHub; dacă da, o descarcă și o instalează singur.

## Hyper-V / Memory Integrity (important)

BlueStacks 4 **nu pornește** cât timp hypervisor-ul Windows e activ (Memory Integrity / Core Isolation, Hyper-V, WSL2). `Install.bat` te întreabă dacă vrei să-l oprești.
Poți folosi și direct **`Hypervisor-Off.bat`**, iar ca să-l repornești, **`Hypervisor-On.bat`**. Ambele cer restart.
Oprirea scade puțin securitatea Windows (protecția kernel-ului împotriva driverelor malițioase).

## FPS mic / sacadări? → `Diagnose.bat`

Dublu-click pe **`Diagnose.bat`**. Pe Desktop apare `m1lator-diagnoza-....txt`, cu probleme marcate **[PROBLEMA]** și ce trebuie făcut pentru fiecare. Nu modifică nimic.
Verifică: hypervisor-ul, virtualizarea din BIOS, modul „raw” (1 nucleu), dacă se folosește placa video integrată în loc de cea dedicată, HDD vs SSD, RAM, laptop pe baterie, Game DVR, setările fiecărei instanțe.
Trimite fișierul ăsta când ceri ajutor.

## Dezinstalare

Dublu-click pe **`Uninstall.bat`**. BlueStacks revine exact cum era.

## Ce face

| | |
|---|---|
| **Temă** | „m1lator Dark”, în stil Apple: gri închis neutru, accent albastru (#0A84FF), colțuri rotunjite |
| **Pagina Home** | Pagina proprie în stil macOS: doar **Library** (aplicațiile tale), cu căutare. **Game center** e scos de tot: fără magazin din cloud, fără reclame, se încarcă instant. |
| **Fullscreen** | La F11, imaginea umple tot ecranul, fără benzi negre. Ex.: dacă joci la **1280x960** (4:3), se întinde pe tot monitorul 16:9. Click-urile și tastele rămân la locul lor. |
| **Rezoluție** | Poți scrie orice rezoluție: Setări → Display → Custom (de ex. 1280x960) |
| **Scurtături** | Toate oprite, mai puțin **F11** (fullscreen) |
| **Notificări** | Toate oprite: popup-uri Windows, notificări din cloud, iconița de notificări (clopoțelul), mesajele „apasă F11 ca să ieși” |
| **FPS** | Setat automat după monitor: 60 / 120 / 180 / 240 (de ex. monitor 144 Hz → 120, 240 Hz → 240). Se poate schimba din Setări → Engine (**High framerate mode**, maximum 240). |
| **Latență** | Timer de sistem de 1 ms, prioritate High, fără throttling de energie, rulează doar pe P-cores (la procesoarele Intel 12th gen sau mai noi). Thread-ul de input are prioritate MMCSS „Games”. |
| **Nuclee** | vCPU = P-cores − 2 (maximum 6), ca randarea și input-ul să aibă mereu 2 nuclee libere |
| **Fără sacadări** | Oprită curățarea forțată a memoriei, care rula la fiecare 15 secunde și dădea lag în interfață |
| **Randare** | OpenGL, GPU dedicat forțat (și din setările Windows, pentru laptopuri). Texturi ASTC (jocuri Unity, ex. Standoff 2): decodare hardware dacă placa video o suportă, altfel software cu cache |
| **Fără telemetrie** | Toate statisticile trimise către cloud sunt oprite, la fel și scanarea proceselor (SecurityMetrics) |
| **Fără reclame** | Promoții, recomandări și sfaturi oprite. Actualizările automate sunt blocate, ca să nu șteargă mod-ul. |
| **Securitate** | Blocat canalul prin care BlueStacks putea descărca și rula programe de la distanță |

**De ce nu 1000 FPS:** pentru Android, valoarea FPS din BlueStacks este frecvența ecranului (Hz), nu o limită de FPS.
Jocurile împart această frecvență la un număr întreg: la 30 FPS în joc pe un ecran de 60 Hz, se afișează un cadru la fiecare 2 refresh-uri.
La 1000 Hz, împărțirea nu iese exactă, așa că 30 FPS în joc devine ~15. În plus, procesorul e întrerupt de 1000 de ori pe secundă degeaba.
De aceea m1lator păstrează valoarea un multiplu de 60, maximum 240. Valorile mai mari deja salvate (de ex. 1000) se corectează singure la pornire.

## Opțiuni avansate

Le setezi în registry, la `HKLM\SOFTWARE\BlueStacks_bgp64\m1lator` (toate sunt opționale):

| Valoare | Tip | Implicit | Ce face |
|---|---|---|---|
| `Priority` | text | `High` | `High` / `AboveNormal` / `Normal` |
| `PCoresOnly` | DWORD | 1 | 0 = folosește și E-cores |
| `TimerResolution` | DWORD | 1 | 0 = fără timer de 1 ms |
| `NoPowerThrottle` | DWORD | 1 | 0 = lasă Windows să facă throttling |
| `StretchFullscreen` | DWORD | 1 | 0 = în fullscreen păstrează benzile negre (proporțiile originale) |
| `MmcssInputThread` | DWORD | 1 | 0 = fără prioritate MMCSS pentru thread-ul de input |

Log: `C:\ProgramData\BlueStacks_bgp64\Logs\m1lator.log`

---

## Pentru dezvoltatori

- `src/M1lator.Tool`: aplică patch-urile IL pe fișierele originale (cu dnlib)
- `src/M1lator.ThemeGen`: generează tema din tema originală BlueStacks
- `src/M1lator.Boost`: optimizările de latență, încărcate în HD-Player
- `scripts/`: instalare și dezinstalare. Folosesc programele gata compilate din `bin/`.
- `bin/`: programele compilate (.NET Framework 4.8, inclus în Windows). Sunt urcate pe git, ca orice download să meargă direct.
  După ce modifici ceva în `src/`, rulează `dotnet build -c Release src/<proiect>` (actualizează `bin/`) și dă commit și la `bin/`.
- Release nou: schimbi numărul din fișierul `VERSION` (de ex. `1.2.3`), dai commit, apoi `git tag v1.2.3 && git push origin main v1.2.3`.
  GitHub Actions face zip-ul și release-ul automat, iar utilizatorii îl primesc cu `Update.bat`.
  Pentru `Update.bat`, repo-ul trebuie să fie **public**.

Repo-ul nu conține fișiere BlueStacks. Totul se generează local, din instalarea ta.
