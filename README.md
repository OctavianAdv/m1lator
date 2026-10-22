# m1lator

Mod pentru **BlueStacks 4 (versiunea Nougat 64-bit, `bgp64`)**: temă nouă, FPS până la 1000, latență mai mică, fără telemetrie și reclame.

## Instalare (3 pași)

1. Descarcă `m1lator-vX.Y.Z.zip` de la **[Releases](../../releases/latest)** și dezarhivează-l oriunde (de ex. pe Desktop).
2. Închide BlueStacks.
3. Dublu-click pe **`Install.bat`** → apasă **Da** la fereastra Windows (drepturi de administrator) → așteaptă mesajul **GATA!**

Pornește BlueStacks. Gata.

> Nu șterge folderul după instalare. Folderul `backup` din el conține fișierele originale, necesare pentru dezinstalare.

## Dezinstalare

Dublu-click pe **`Uninstall.bat`**. BlueStacks revine exact cum era.

## Ce face

| | |
|---|---|
| **Temă** | „m1lator Midnight”: fundal grafit, accente violet, colțuri rotunjite |
| **FPS** | Setări → Engine → bifează **High framerate mode**. Slider-ul merge până la **1000 FPS**. Implicit e 240. |
| **Latență** | Timer de sistem de 1 ms, prioritate High, fără throttling de energie, rulează doar pe P-cores (la procesoarele Intel 12th gen sau mai noi) |
| **Randare** | OpenGL, decodare ASTC cu cache, GPU dedicat forțat |
| **Fără telemetrie** | Toate statisticile trimise către cloud sunt oprite, la fel și scanarea proceselor (SecurityMetrics) |
| **Fără reclame** | Promoții, recomandări și sfaturi oprite. Actualizările automate sunt blocate, ca să nu șteargă mod-ul. |
| **Securitate** | Blocat canalul prin care BlueStacks putea descărca și rula programe de la distanță |

FPS-ul real e limitat și de Android, de joc și de monitor (de ex. la un monitor de 240 Hz vezi maximum ~240 FPS).

## Opțiuni avansate

Le setezi în registry, la `HKLM\SOFTWARE\BlueStacks_bgp64\m1lator` (toate sunt opționale):

| Valoare | Tip | Implicit | Ce face |
|---|---|---|---|
| `Priority` | text | `High` | `High` / `AboveNormal` / `Normal` |
| `PCoresOnly` | DWORD | 1 | 0 = folosește și E-cores |
| `TimerResolution` | DWORD | 1 | 0 = fără timer de 1 ms |
| `NoPowerThrottle` | DWORD | 1 | 0 = lasă Windows să facă throttling |

Log: `C:\ProgramData\BlueStacks_bgp64\Logs\m1lator.log`

---

## Pentru dezvoltatori

- `src/M1lator.Tool`: aplică patch-urile IL pe fișierele originale (cu dnlib)
- `src/M1lator.ThemeGen`: generează tema din tema originală BlueStacks
- `src/M1lator.Boost`: optimizările de latență, încărcate în HD-Player
- `scripts/`: instalare și dezinstalare. Din repo merg direct, dacă ai .NET 9 SDK.
- Release nou: `git tag v1.2.3 && git push origin v1.2.3`. GitHub Actions face zip-ul și release-ul automat.

Repo-ul nu conține fișiere BlueStacks. Totul se generează local, din instalarea ta.
