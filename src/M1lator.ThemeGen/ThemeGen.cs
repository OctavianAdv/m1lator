using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace M1lator.ThemeGen;

/// <summary>A theme preset (scripts\themes\*.json), passed on the command line by scripts\theme.ps1.</summary>
sealed class Preset
{
    public string Name = "m1lator Dark";
    public string Accent = "#0A84FF";   // selection, links, highlights
    public string Tint = "#1C1C1E";     // base surface colour; greys take its hue and lightness
    public double Glass = 1.0;          // opacity of window chrome (top bar, sidebar, backgrounds); < 1 = see-through glass
    public int Radius = 12;             // corner radius of windows/popups; buttons and tabs scale from it
}

/// <summary>
/// Builds the m1lator theme from the stock BlueStacks theme (Client\Assets):
///  - navy backgrounds / greys -> neutral greys tinted by Preset.Tint (Apple-style #1C1C1E/#2C2C2E/#3A3A3C by default)
///  - blue/orange accents      -> Preset.Accent; other vivid colours snap to Apple system red/green/yellow/purple
///  - chrome surfaces          -> Preset.Glass opacity (M1lator.Client blurs the desktop behind them: Liquid Glass)
///  - corners                  -> Preset.Radius
/// The same colour transform is applied to the XAML ThemeFile, ThemeConfig.ini and every PNG icon,
/// so icons and brushes stay consistent. Photos/artwork are left untouched.
/// </summary>
static class ThemeGen
{
    public const string ThemeName = "m1lator";
    const int PhotoColorThreshold = 6000;

    static readonly double[] SystemRed = { 3, 1.00, 0.61 };       // #FF453A
    static readonly double[] SystemGreen = { 135, 0.64, 0.50 };   // #30D158
    static readonly double[] SystemYellow = { 50, 1.00, 0.52 };   // #FFD60A
    static readonly double[] SystemPurple = { 291, 0.86, 0.65 };  // #BF5AF2

    // window chrome that becomes translucent glass (popups stay solid: they are separate windows without blur)
    static readonly string[] GlassBrushes =
    {
        "ApplicationBackgroundBrush", "TopBarColor", "SidebarBackground", "SidebarElementNormal", "SidebarElementHover",
        "SidebarElementClick", "BottomBarColor", "HomeAppTabButtonBaseColor",
        "SettingsWindowBackground", "SettingsWindowTabMenuBackground", "SettingsWindowTitleBarBackground",
    };

    static Preset P = new Preset();
    static double[] AccentHsl = { 211, 1.00, 0.52 };
    static double NeutralHue = 240, NeutralSat = 0.03, NeutralOffset = 0.017;

    public static int Run(string srcDir, string outDir, Preset preset)
    {
        P = preset;
        AccentHsl = Hsl(P.Accent);
        double[] tint = Hsl(P.Tint);
        NeutralHue = tint[0];
        NeutralSat = Math.Min(tint[1], 0.25);
        NeutralOffset = tint[2] - 0.115 * 0.85;     // stock window background (l = 0.115) lands exactly on the tint

        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        int png = 0;
        string srcRoot = Path.GetFullPath(srcDir).TrimEnd('\\') + "\\";
        foreach (var src in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(outDir, src.Substring(srcRoot.Length));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (src.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { RecolorPng(src, dst); png++; }
            else File.Copy(src, dst);
        }

        string themeFile = Path.Combine(outDir, "ThemeFile");
        string xaml = RecolorText(File.ReadAllText(themeFile));
        foreach (var kv in CornerRadii())
            xaml = SetKeyed(xaml, kv.Key, kv.Value);
        foreach (var kv in SettingsBrushes())
            xaml = SetBrush(xaml, kv.Key, kv.Value);
        foreach (var key in GlassBrushes)
            xaml = WithAlpha(xaml, key, P.Glass);
        xaml = SetKeyed(xaml, "ThemeDisplayName", P.Name);
        xaml = SetKeyed(xaml, "themeName", ThemeName);
        int icon = (int)Math.Round(68 * 0.225);   // iOS icon corner ≈ 22.5% of 68px
        xaml = Regex.Replace(xaml, "RadiusX=\"0\"(\\s+)RadiusY=\"0\"(\\s+Rect=\"0,0,68,68\")", $"RadiusX=\"{icon}\"$1RadiusY=\"{icon}\"$2");
        File.WriteAllText(themeFile, xaml);

        string ini = Path.Combine(outDir, "ThemeConfig.ini");
        if (File.Exists(ini)) File.WriteAllText(ini, RecolorText(File.ReadAllText(ini)));

        Console.WriteLine($"theme '{P.Name}' (accent {P.Accent}, tint {P.Tint}, glass {P.Glass:0.00}, radius {P.Radius}) -> {outDir} ({png} png recoloured)");
        return 0;
    }

    static Dictionary<string, string> CornerRadii()
    {
        int big = P.Radius, mid = Math.Max(2, (int)Math.Round(P.Radius * 0.67)), small = Math.Max(2, (int)Math.Round(P.Radius * 0.5));
        string all(int r) => $"{r},{r},{r},{r}";
        return new Dictionary<string, string>
        {
            ["BeginnerGuideRadius"] = all(big),
            ["ButtonCornerRadius"] = all(mid),
            ["MessageWindowRadius"] = all(big),
            ["PopupRadius"] = all(big),
            ["PreferenceDropDownRadius"] = all(mid),
            ["SettingsWindowRadius"] = all(big),
            ["SidebarElementCornerRadius"] = all(mid),
            ["SpeedUpTipsRadius"] = all(big),
            ["TabRadius"] = $"{mid},{mid},0,0",
            ["TextBoxRadius"] = all(small + 2),
        };
    }

    // Settings window as macOS System Settings: lighter sidebar, darker content, accent pill for the selected tab
    static Dictionary<string, string> SettingsBrushes()
    {
        string accent = Hex(AccentHsl[0], AccentHsl[1], AccentHsl[2], 0xFF);
        return new Dictionary<string, string>
        {
            ["SettingsWindowBackground"] = Surface(0.008),
            ["SettingsWindowTitleBarBackground"] = Surface(0.055),
            ["SettingsWindowTitleBarForeGround"] = "#FFF5F5F7",
            ["SettingsWindowTabMenuBackground"] = Surface(0.055),
            ["SettingsWindowTabMenuItemBackground"] = "#00000000",
            ["SettingsWindowTabMenuItemHoverBackground"] = Surface(0.12),
            ["SettingsWindowTabMenuItemSelectedBackground"] = accent,
            ["SettingsWindowTabMenuItemSelectedForeground"] = "#FFFFFFFF",
            ["SettingsWindowTabMenuItemForeground"] = "#FFEBEBF0",
            ["SettingsWindowTabMenuItemLegendForeground"] = "#FFF5F5F7",
            ["SettingsWindowTabMenuItemUnderline"] = "#00" + accent.Substring(3),
            ["SettingsWindowTextBoxBackgroundColor"] = Surface(0.06),
            ["SettingsWindowTextBoxBorderColor"] = Surface(0.12),
            ["SettingsWindowBorderColor"] = Surface(0.10),
            ["SettingsWindowBorderBrushColor"] = Surface(0.10),
        };
    }

    // tint lightened by dl (0 = the tint itself)
    static string Surface(double dl)
    {
        double[] t = Hsl(P.Tint);
        return Hex(t[0], Math.Min(t[1], 0.25), Math.Min(t[2] + dl, 0.95), 0xFF);
    }

    static string SetKeyed(string xaml, string key, string value)
    {
        var re = new Regex($"(x:Key=\"{Regex.Escape(key)}\">)[^<]*(<)");
        if (!re.IsMatch(xaml)) throw new Exception("theme key not found: " + key);
        return re.Replace(xaml, m => m.Groups[1].Value + value + m.Groups[2].Value);
    }

    // only the SolidColorBrush entry (the same key also appears in the category dictionary as plain text)
    static string SetBrush(string xaml, string key, string argb)
    {
        var re = new Regex($"(<av:SolidColorBrush\\s+x:Key=\"{Regex.Escape(key)}\">)[^<]*(<)");
        if (!re.IsMatch(xaml)) throw new Exception("theme brush not found: " + key);
        return re.Replace(xaml, m => m.Groups[1].Value + argb + m.Groups[2].Value);
    }

    // multiply an existing brush's alpha by `opacity` (glass)
    static string WithAlpha(string xaml, string key, double opacity)
    {
        if (opacity >= 0.999) return xaml;
        var re = new Regex($"(<av:SolidColorBrush\\s+x:Key=\"{Regex.Escape(key)}\">)#([0-9A-Fa-f]{{8}})(<)");
        return re.Replace(xaml, m =>
        {
            uint argb = Convert.ToUInt32(m.Groups[2].Value, 16);
            uint a = (uint)Math.Round((argb >> 24) * Math.Max(0.05, Math.Min(1, opacity)));
            return m.Groups[1].Value + "#" + ((a << 24) | (argb & 0xFFFFFF)).ToString("X8") + m.Groups[3].Value;
        });
    }

    static string RecolorText(string text) =>
        Regex.Replace(text, "#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})(?![0-9A-Fa-f])", m =>
        {
            string hex = m.Groups[1].Value;
            uint argb = Convert.ToUInt32(hex.Length == 6 ? "FF" + hex : hex, 16);
            uint mapped = Map(argb);
            return hex.Length == 6 ? "#" + (mapped & 0xFFFFFF).ToString("X6") : "#" + mapped.ToString("X8");
        });

    static void RecolorPng(string src, string dst)
    {
        using var input = new Bitmap(src);
        using var bmp = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(input, 0, 0, input.Width, input.Height);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        if (new HashSet<int>(px).Count > PhotoColorThreshold)
        {
            // screenshots / artwork: recolouring would tint faces and game art, keep as-is
            bmp.UnlockBits(data);
            File.Copy(src, dst, true);
            return;
        }
        var cache = new Dictionary<int, int>();
        for (int i = 0; i < px.Length; i++)
        {
            if ((px[i] >>> 24) == 0) continue;
            if (!cache.TryGetValue(px[i], out int v)) cache[px[i]] = v = (int)Map((uint)px[i]);
            px[i] = v;
        }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        bmp.Save(dst, ImageFormat.Png);
    }

    /// <summary>ARGB -> ARGB colour transform shared by brushes and images.</summary>
    static uint Map(uint argb)
    {
        uint a = argb >> 24;
        double r = ((argb >> 16) & 0xFF) / 255.0, g = ((argb >> 8) & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        RgbToHsl(r, g, b, out double h, out double s, out double l);

        bool vivid = s >= 0.45 && l > 0.25 && l < 0.85;
        double[]? system = !vivid ? null
            : h >= 180 && h < 260 ? AccentHsl
            : h >= 15 && h < 48 ? AccentHsl     // stock orange is the selection highlight: one accent, Apple style
            : h >= 48 && h < 75 ? SystemYellow
            : h >= 75 && h < 180 ? SystemGreen
            : h >= 260 && h < 330 ? SystemPurple
            : SystemRed;

        if (system != null)
        {
            // snap to the target colour, keep a little of the original lightness so hover/pressed states stay distinct
            h = system[0]; s = system[1];
            l = Math.Min(Math.Max(system[2] + (l - 0.5) * 0.4, 0.40), 0.75);
        }
        else
        {
            // greys and navy backgrounds -> tinted neutrals; darks compressed toward the tint
            h = NeutralHue; s = NeutralSat;
            if (l < 0.5) l = Math.Max(0, l * 0.85 + NeutralOffset);
        }

        HslToRgb(h, s, l, out r, out g, out b);
        return (a << 24) | ((uint)Math.Round(r * 255) << 16) | ((uint)Math.Round(g * 255) << 8) | (uint)Math.Round(b * 255);
    }

    static double[] Hsl(string hex)
    {
        uint rgb = Convert.ToUInt32(hex.TrimStart('#').Substring(hex.TrimStart('#').Length - 6), 16);
        RgbToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, out double h, out double s, out double l);
        return new[] { h, s, l };
    }

    static string Hex(double h, double s, double l, uint a)
    {
        HslToRgb(h, s, l, out double r, out double g, out double b);
        return "#" + ((a << 24) | ((uint)Math.Round(r * 255) << 16) | ((uint)Math.Round(g * 255) << 8) | (uint)Math.Round(b * 255)).ToString("X8");
    }

    static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max == min) { h = s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
    }

    static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
    {
        if (s == 0) { r = g = b = l; return; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = h / 360;
        r = Hue(p, q, hk + 1 / 3.0); g = Hue(p, q, hk); b = Hue(p, q, hk - 1 / 3.0);
    }

    static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1 / 2.0) return q;
        if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
        return p;
    }
}
