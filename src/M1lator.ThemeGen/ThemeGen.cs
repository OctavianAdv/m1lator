using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace M1lator.ThemeGen;

/// <summary>
/// Builds the "m1lator Dark" theme (Apple dark-mode style) from the stock BlueStacks theme (Client\Assets):
///  - navy backgrounds / greys -> Apple graphite greys (#1C1C1E, #2C2C2E, #3A3A3C ...)
///  - vivid colours            -> snapped to Apple system colours (blue accent #0A84FF, orange, red, green, yellow, purple)
///  - corners                  -> larger, softer radii
/// The same colour transform is applied to the XAML ThemeFile, ThemeConfig.ini and every PNG icon,
/// so icons and brushes stay consistent. Photos/artwork are left untouched.
/// </summary>
static class ThemeGen
{
    public const string ThemeName = "m1lator";
    const string DisplayName = "m1lator Dark";

    const double NeutralHue = 240, NeutralSat = 0.03;   // Apple greys carry a barely visible cool tint
    const int PhotoColorThreshold = 6000;

    // Apple dark-mode system colours as (hue, saturation, lightness)
    static readonly double[] SystemBlue = { 211, 1.00, 0.52 };    // #0A84FF
    static readonly double[] SystemRed = { 3, 1.00, 0.61 };       // #FF453A
    static readonly double[] SystemGreen = { 135, 0.64, 0.50 };   // #30D158
    static readonly double[] SystemYellow = { 50, 1.00, 0.52 };   // #FFD60A
    static readonly double[] SystemPurple = { 291, 0.86, 0.65 };  // #BF5AF2

    static readonly Dictionary<string, string> CornerRadii = new()
    {
        ["BeginnerGuideRadius"] = "12,12,12,12",
        ["ButtonCornerRadius"] = "8,8,8,8",
        ["MessageWindowRadius"] = "12,12,12,12",
        ["PopupRadius"] = "12,12,12,12",
        ["PreferenceDropDownRadius"] = "10,10,10,10",
        ["SettingsWindowRadius"] = "12,12,12,12",
        ["SidebarElementCornerRadius"] = "8,8,8,8",
        ["SpeedUpTipsRadius"] = "12,12,12,12",
        ["TabRadius"] = "8,8,0,0",
        ["TextBoxRadius"] = "8,8,8,8",
    };

    // Settings window as macOS System Settings: lighter sidebar, darker content, solid blue pill for the selected tab
    static readonly Dictionary<string, string> SettingsBrushes = new()
    {
        ["SettingsWindowBackground"] = "#FF1E1E20",
        ["SettingsWindowTitleBarBackground"] = "#FF2A2A2D",
        ["SettingsWindowTitleBarForeGround"] = "#FFF5F5F7",
        ["SettingsWindowTabMenuBackground"] = "#FF2A2A2D",
        ["SettingsWindowTabMenuItemBackground"] = "#002A2A2D",
        ["SettingsWindowTabMenuItemHoverBackground"] = "#FF3A3A3D",
        ["SettingsWindowTabMenuItemSelectedBackground"] = "#FF0A84FF",
        ["SettingsWindowTabMenuItemSelectedForeground"] = "#FFFFFFFF",
        ["SettingsWindowTabMenuItemForeground"] = "#FFEBEBF0",
        ["SettingsWindowTabMenuItemLegendForeground"] = "#FFF5F5F7",
        ["SettingsWindowTabMenuItemUnderline"] = "#000A84FF",
        ["SettingsWindowTextBoxBackgroundColor"] = "#FF2C2C2E",
        ["SettingsWindowTextBoxBorderColor"] = "#FF3A3A3C",
        ["SettingsWindowBorderColor"] = "#FF38383A",
        ["SettingsWindowBorderBrushColor"] = "#FF38383A",
    };

    public static int Run(string srcDir, string outDir)
    {
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
        foreach (var kv in CornerRadii)
            xaml = SetKeyed(xaml, kv.Key, kv.Value);
        foreach (var kv in SettingsBrushes)
            xaml = SetBrush(xaml, kv.Key, kv.Value);
        xaml = SetKeyed(xaml, "ThemeDisplayName", DisplayName);
        xaml = SetKeyed(xaml, "themeName", ThemeName);
        xaml = Regex.Replace(xaml, "RadiusX=\"0\"(\\s+)RadiusY=\"0\"(\\s+Rect=\"0,0,68,68\")", "RadiusX=\"15\"$1RadiusY=\"15\"$2"); // iOS icon corner ≈ 22% of 68px
        File.WriteAllText(themeFile, xaml);

        string ini = Path.Combine(outDir, "ThemeConfig.ini");
        if (File.Exists(ini)) File.WriteAllText(ini, RecolorText(File.ReadAllText(ini)));

        Console.WriteLine($"theme '{DisplayName}' -> {outDir} ({png} png recoloured)");
        return 0;
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
            : h >= 180 && h < 260 ? SystemBlue
            : h >= 15 && h < 48 ? SystemBlue   // stock orange is the selection highlight: Apple uses one accent
            : h >= 48 && h < 75 ? SystemYellow
            : h >= 75 && h < 180 ? SystemGreen
            : h >= 260 && h < 330 ? SystemPurple
            : SystemRed;

        if (system != null)
        {
            // snap to the Apple colour, keep a little of the original lightness so hover/pressed states stay distinct
            h = system[0]; s = system[1];
            l = Math.Min(Math.Max(system[2] + (l - 0.5) * 0.4, 0.40), 0.75);
        }
        else
        {
            // greys and navy backgrounds -> neutral Apple greys; darks are compressed toward #1C1C1E/#2C2C2E/#3A3A3C
            h = NeutralHue; s = NeutralSat;
            if (l < 0.5) l = l * 0.85 + 0.017;
        }

        HslToRgb(h, s, l, out r, out g, out b);
        return (a << 24) | ((uint)Math.Round(r * 255) << 16) | ((uint)Math.Round(g * 255) << 8) | (uint)Math.Round(b * 255);
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
