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

## Teme

Dublu-click pe **`Theme.bat`** (nu cere administrator). Alegi un număr, apoi repornești BlueStacks.

| | Temă | Accent | Stil |
|---|---|---|---|
| 1 | **Liquid Glass** (implicit) | albastru `#0A84FF` | sticlă |
| 2 | Graphite | albastru `#0A84FF` | solid |
| 3 | Midnight Violet | violet `#8B5CF6` | sticlă |
| 4 | Sunset | portocaliu `#FF6B3D` | sticlă |
| 5 | Aurora | verde `#30D158` | sticlă |
| 6 | Rose | roz `#FF375F` | sticlă |
| C | Culoare proprie | orice `#RRGGBB` | sticlă sau solid |

**Sticla** = wallpaper-ul tău de Windows, blurat puternic și nuanțat, văzut prin bara de sus și cea laterală.
Fără imagine de wallpaper (culoare simplă sau slideshow), apar pete de lumină colorate, ca la wallpaper-ele iOS. Nu consumă nimic în timpul jocului: imaginea se calculează o singură dată, la pornire.

Temă proprie, permanentă: copiază un fișier din `scripts\themes\`, schimbă valorile (nume, accent, tint, glass 0–1, radius, dim) și apare automat în listă.

## Hyper-V / Memory Integrity (important)

BlueStacks 4 **nu pornește** cât timp hypervisor-ul Windows e activ (Memory Integrity / Core Isolation, Hyper-V, WSL2). `Install.bat` te întreabă dacă vrei să-l oprești.
Poți folosi și direct **`Hypervisor-Off.bat`**, iar ca să-l repornești, **`Hypervisor-On.bat`**. Ambele cer restart.
Oprirea scade puțin securitatea Windows (protecția kernel-ului împotriva driverelor malițioase).

## FPS mic / sacadări? → `Diagnose.bat`

Dublu-click pe **`Diagnose.bat`**. Pe Desktop apare `m1lator-diagnoza-....txt`, cu probleme marcate **[PROBLEMA]** și ce trebuie făcut pentru fiecare. Nu modifică nimic.
Verifică: hypervisor-ul, virtualizarea din BIOS, modul „raw” (1 nucleu), dacă se folosește placa video integrată în loc de cea dedicată, HDD vs SSD, RAM, laptop pe baterie, Game DVR, setările fiecărei instanțe.
Trimite fișierul ăsta când ceri ajutor.

## Google Play: „authentication failed”

Logarea Google se face în Android, nu în m1lator. Blocarea cloud-ului BlueStacks nu o afectează: Android are propria conexiune.
BlueStacks 4 are însă un Android System WebView din 2016 (versiunea 52), iar Google poate refuza logarea din browsere atât de vechi.

1. În Android: **Settings → Date & time** → pornește „Automatic date & time”. O oră greșită strică logarea.
2. Încearcă logarea din **Settings → Accounts → Add account → Google**, nu din pop-up-ul BlueStacks.
3. Dacă tot nu merge: lasă instanța pornită, încearcă logarea, apoi rulează **`Diagnose.bat`**. Secțiunea „Google Play / cont Google” arată eroarea exactă din Android. Trimite fișierul.

## Standoff 2: „SignIn (CryptographicException)”

Eroarea vine din joc, la logare: nu poate decripta datele de logare salvate, sau verificarea de securitate eșuează.

1. În Android: **Settings → Date & time** → pornește „Automatic date & time”.
2. Repornește instanța și mai încearcă o dată. Uneori e o problemă de server la Standoff 2.
3. Dacă tot apare: **Settings → Apps → Standoff 2 → Storage → Clear cache**. Apoi încearcă din nou.
4. Ultima variantă: **Clear data**.
   ⚠️ **Înainte, verifică dacă contul e legat** de Google / Facebook / e-mail. Un cont de tip „guest” nelegat se poate pierde la Clear data.
5. Dacă nu se rezolvă: cu instanța pornită, după eroare, rulează **`Diagnose.bat`** și trimite fișierul. Secțiunea „erori din jocuri (Unity)” arată excepția completă.

## Dezinstalare

Dublu-click pe **`Uninstall.bat`**. BlueStacks revine exact cum era.

## Ce face

| | |
|---|---|
| **Temă** | 6 teme gata făcute, plus culoare proprie, toate alese din **`Theme.bat`**. Implicit: **Liquid Glass**, adică bara de sus și cea laterală translucide peste wallpaper-ul tău blurat (ca Mica / Liquid Glass de la Apple), accent albastru, colțuri mari. |
| **Branding** | Sus în stânga apare **M1LATOR** cu efect RGB animat (în loc de logo-ul și versiunea BlueStacks). În Setări → About apare versiunea m1lator. |
| **Font** | Toată interfața folosește **Geist**. Logo-ul folosește **Space Grotesk**. Ambele fonturi sunt open source, cu licență OFL. |
| **Pagina Home** | Pagina proprie în stil macOS: doar **Library** (aplicațiile tale), cu căutare. **Game center** e scos de tot: fără magazin din cloud, fără reclame, se încarcă instant. |
| **Fullscreen** | La F11, imaginea umple tot ecranul, fără benzi negre. Ex.: dacă joci la **1280x960** (4:3), se întinde pe tot monitorul 16:9. Click-urile și tastele rămân la locul lor. |
| **Rezoluție** | Poți scrie orice rezoluție: Setări → Display → Custom (de ex. 1280x960) |
| **Scurtături** | Toate oprite, mai puțin **F11** (fullscreen) |
| **Notificări** | Toate oprite: popup-uri Windows, notificări din cloud, iconița de notificări (clopoțelul), mesajele „apasă F11 ca să ieși” |
| **FPS** | Setări → Engine → bifează **High framerate mode**. Slider-ul merge până la **1000 FPS**. La închiderea emulatorului, FPS revine la **30** și High framerate mode se oprește singur. |
| **Latență** | Timer de sistem de 1 ms, prioritate High, fără throttling de energie, rulează doar pe P-cores (la procesoarele Intel 12th gen sau mai noi). Thread-ul de input are prioritate MMCSS „Games”. |
| **Input** | Mișcările de mouse merg spre Android una câte una, ca în BlueStacks original, dar fără alocare de memorie la fiecare eveniment. Tastele ținute apăsate nu mai trimit repetări inutile. Opțional (`CoalesceMouse` = 1): mișcările care vin în rafală se adună într-una singură. Poate ajuta la mouse-uri de 4000–8000 Hz, dar în Standoff 2 a făcut ochirea sacadată. |
| **Android curățat** | O singură dată per instanță, după prima pornire: animațiile Android sunt oprite (interfață instantanee), iar aplicațiile de fundal inutile sunt dezactivate (magazinul și contul BlueStacks, Feedback, Calendar, Print, TTS, Ceas, Telefon). Rămân: Google Play / Play Games (logare), BS-Services, camera, ghidul de controale. Se poate anula: `AndroidTune` = 0, apoi pornești instanța o dată. |
| **Log-uri** | BlueStacks scria pe disc la fiecare linie de log, din orice fir, inclusiv cele de input. Acum log-urile se scriu cel mult de 2 ori pe secundă; erorile se scriu imediat. |
| **GC** | HD-Player rulează cu „low latency garbage collection”: mai puține pauze în codul care procesează mouse-ul și tastatura. |
| **Interfața în timpul jocului** | Logo-ul RGB curge doar la pornire și când treci cu mouse-ul peste el. Animația continuă forța redesenarea întregii ferestre de 30 de ori pe secundă, chiar în timpul jocului. |
| **Măsurători** | La fiecare minut, `m1lator.log` notează cât procesor folosește HD-Player și care sunt cele mai încărcate fire. Totul rămâne pe PC, nimic nu pleacă. `Diagnose.bat` le afișează. |
| **Senzori** | Android cere senzori pe care BlueStacks 4 nu îi are (magnetometru, orientare). Cererile sunt ignorate în liniște, fără să mai fie scrise erori în log de mai multe ori pe secundă. |
| **Nuclee** | vCPU = nuclee P fizice − 2 (fără Hyper-Threading) sau − 1 (cu Hyper-Threading), maximum 6, ca randarea și input-ul să aibă mereu nuclee libere. Toate nucleele pentru Android ar însemna mai puține FPS și input lag, pentru că firele de randare și de input ar aștepta după vCPU. |
| **Procese BlueStacks** | BstkSVC (disc/rețea pentru Android) rulează la prioritate mai mare și fără throttling de energie. Nici interfața nu mai are throttling. |
| **DPI** | Android pornește mereu cu **DPI 440**. Recomandat la 1280x960 sau mai mare: la 1280x720, DPI 440 lasă prea puțin spațiu și unele aplicații arată ciudat. |
| **Setări** | Stil macOS: bară laterală cu selecție albastră, titlu mare. Scoase tab-urile inutile: Notificări, Game settings (doar PUBG/Free Fire/COD), Scurtături. Scoase din Preferințe și din bara de sus: Discord, scurtături pe desktop, Help, ghidul de început, butonul de cont. |
| **Fără sacadări** | Oprită curățarea forțată a memoriei, care rula la fiecare 15 secunde și dădea lag în interfață |
| **Randare** | OpenGL, GPU dedicat forțat (și din setările Windows, pentru laptopuri). Texturi ASTC (jocuri Unity, ex. Standoff 2): decodare hardware dacă placa video o suportă, altfel software cu cache |
| **Fără telemetrie** | **Cloud-ul BlueStacks e blocat complet**: nicio cerere nu mai ajunge la serverele BlueStacks. Play Store și jocurile merg normal, pentru că trec prin Android. Se pierd doar lista de profiluri de dispozitiv din cloud și paginile de help. Pe lângă asta, sunt oprite direct în cod: raportul de utilizare a aplicațiilor, info despre sistem la fiecare boot, timeline, scanarea proceselor (SecurityMetrics). Oprit și browserul ascuns de notificări Firebase. |
| **Fără reclame** | Promoții, recomandări și sfaturi oprite. Actualizările automate sunt blocate, ca să nu șteargă mod-ul. |
| **Securitate** | Blocat canalul prin care BlueStacks putea descărca și rula programe de la distanță |

**De ce se resetează la închidere:** pentru Android, valoarea FPS din BlueStacks este frecvența ecranului (Hz).
Dacă emulatorul repornește cu 1000, unele jocuri împart greșit frecvența (de ex. 30 FPS în joc devine ~15).
De aceea, la închidere, FPS revine la 30 și High framerate mode se oprește. Valoarea mare o pui din nou când vrei, în fiecare sesiune.
Notă: dacă schimbi în Setări ceva care cere repornirea instanței (RAM, nuclee etc.), repornirea contează tot ca o închidere, deci FPS revine și atunci la 30.

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
| `Dpi` | DWORD | 440 | DPI-ul Android la fiecare pornire. 0 = nu-l schimba |
| `AndroidTune` | DWORD | 1 | 0 = anulează curățarea Android la următoarea pornire a instanței (aplicațiile reactivate, animațiile 1x) |
| `PerfLog` | DWORD | 1 | 0 = fără măsurători CPU în `m1lator.log` |
| `CoalesceMouse` | DWORD | 0 | 1 = mișcările de mouse venite în rafală se adună (experimental; în Standoff 2 dă ochire sacadată) |
| `SuppressKeyRepeat` | DWORD | 1 | 0 = tastele ținute apăsate trimit repetări (ca în original) |
| `BoostCompanions` | DWORD | 1 | 0 = nu umbla la prioritatea BstkSVC / interfață |
| `HiddenSettings` | text | (3 tab-uri) | Tab-urile ascunse din Setări, separate prin virgulă (ex. `STRING_NOTIFICATION`). Gol = toate vizibile |
| `Font` | text | `Geist` | Fontul interfeței: `Geist`, `Space Grotesk` sau `System` (fontul Windows) |
| `ResetFpsOnExit` | DWORD | 1 | 0 = FPS și High framerate mode rămân cum le-ai lăsat la închidere |
| `FpsOnExit` | DWORD | 30 | FPS-ul setat la închiderea emulatorului |

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
