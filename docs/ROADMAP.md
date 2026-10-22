# m1lator roadmap

Target: BlueStacks 4.220.0.4001 (OEM `bgp64`, Nougat). Engine in `C:\Program Files\BlueStacks_bgp64`,
UI client in `C:\ProgramData\BlueStacks_bgp64\Client`. Config lives in `HKLM\SOFTWARE\BlueStacks_bgp64`.

## What the analysis found

| Component | Type | Notes |
|---|---|---|
| `Client\Bluestacks.exe` | .NET 4 WPF | Main UI: tabs, sidebar, settings, skin picker, hidden Theme Editor |
| `HD-Common.dll` | .NET 4 | Shared: registry, theme engine (`BluestacksUIColor`), engine settings VM |
| `HD-Player.exe` | .NET 4 | Android display window; P/Invokes the native renderer |
| `HD-MultiInstanceManager.exe` | .NET 4 | Instance manager |
| `HD-Frontend-Native.dll`, `HD-Plus-Frontend-Native.dll`, `libOpenglRender.dll`, `HD-Opengl-Native.dll`, `libGLES*`, `libEGL*` | native C++ | Renderer (PGA / GGA, OpenGL or DirectX via ANGLE) |
| `Bstk*` | native | VirtualBox-based hypervisor + kernel driver (signed; do not touch) |

- No .NET assembly has a strong name. Patched assemblies load. Their Authenticode signatures break, and .NET doesn't enforce those on load.
- The code isn't obfuscated, and ilspycmd decompiles all of it cleanly (it needs `DOTNET_ROLL_FORWARD=Major`, because only .NET 9 is installed).
- Themes are data-driven. Any folder in `Client\` that contains a `ThemeFile` (XAML) shows up in the skin picker
  (`BlueStacksUIColorManager.GetThemes`). The active theme is `HKLM\...\Client\ClientThemeName`.

## 1. Custom theme (no binary patching)

- [ ] `themes/m1lator-dark/`: copy of `Assets/`, with a new `ThemeFile` palette (≈430 `SolidColorBrush` keys,
      plus `DictCornerRadius`, `DictTransform`, `DictGeometry`) and `ThemeConfig.ini`
- [ ] Recolour or replace about 810 PNG icons with a script (hue shift or flat recolour)
- [ ] Set `ThemeDisplayName` in `DictThemeAvailable`
- [ ] Turn on the built-in Theme Editor: set `HKLM\...\Client\OpenThemeEditor=1` (DWORD), then GET
      `http://127.0.0.1:<PartnerServerPort=2871>/openThemeEditor`. It edits colors live and can save to the theme.
- [ ] Custom cursor (`Cursor.cur`, `Mouse_cursor*.cur`), font (`Roboto-Medium.ttf`), loading gifs

## 2. Framerate / rendering

Values that are already exposed through the registry (`Guests\<vm>\Config`):

| Key | Now | Meaning |
|---|---|---|
| `FPS` | 60 | Guest FPS cap. Also passed as the boot param `fps=` and set live with `setfpsvalue?fps=` |
| `EnableHighFPS` | (absent) | Unlocks a slider maximum of 240 |
| `ShowFPS` | (absent) | Native FPS overlay (`PgaShowFps`) |
| `GlRenderMode` | 1 | 1 = OpenGL, 4 = DirectX |
| `GlMode` | 1 | 1 = PGA, 2 = AGA/GGA (advanced graphics) |
| `ASTCOption` | 0 | 0 off / 1 SW / 2 SW+cache / 3 HW texture decoding |
| `FenceSyncType` | 1 | GL fence sync strategy |
| `DisableDWM` | 0 | |
| `FrameBuffer\0\Depth` | 16 | Framebuffer bit depth. 16 causes colour banding, so test 32 |
| `ForceDedicatedGPU` (Config) | 0 | Force the dGPU on hybrid laptops |

- [ ] `registry/perf-profile.reg` + `tools/apply-perf.ps1` with per-instance presets
- [ ] Patch `HD-Common.dll` `EngineSettingBaseViewModel.EnableHighFrameRates` (`MaxFPS = 240` → 360 or uncapped)
- [ ] Expose the hidden options in the settings UI: ShowFPS, ASTC, FenceSyncType, Depth, GlMode
- [ ] Process priority / CPU affinity for `HD-Player.exe` + `BstkSVC.exe` (High priority, keep the E-cores off the render thread)
- [ ] Timer resolution: call `timeBeginPeriod(1)` in HD-Player for smoother frame pacing
- [ ] Native renderer (`libOpenglRender.dll`, `HD-*-Frontend-Native.dll`): vsync / swap-interval patching. Needs
      Ghidra/x64dbg, so it's high risk and comes last

## 3. Compatibility

- [ ] Device-profile spoofing (`DeviceProfileFromCloud`, `pcode`), letting games unlock higher graphics tiers
- [ ] Turn on `HardwareDecoding` ASTC when `IsHardwareAstcSupported`
- [ ] DPI / resolution presets (boot param `DPI=`, `GuestWidth/GuestHeight`)
- [ ] Update the bundled `d3dcompiler_47.dll` and ANGLE (`libEGL.dll`, `libGLESv2.dll`). Risky, because of ABI coupling
- [ ] Fix `Android` (N-64) vs `Android_1` (N-32) defaults: VCPUs/RAM per the host

## 4. Debloat / privacy (optional)

- [ ] Stub out stats (`ClientStats.*`, 66 call sites), promotions, and the CEF home page (`/page/bgp-home-html`)
- [ ] Block the update nag (`LastUpdateSkippedVersion`, the 4.280 installer is already downloaded)
- [ ] `bst_config` feature flags: `IsBTVDisabled`, `IsWallpaperChangeAllowed=true`

## Patching workflow

1. Decompile with `ilspycmd -p` → edit the C# → recompile one assembly. Alternatively, patch the IL with dnSpyEx (it's easier when only a few methods change).
2. Back up the originals into `backup/` (git-ignored) before replacing them.
3. Each patch is tracked as a script or diff in `patches/`. Original and patched binaries are **never** committed.
