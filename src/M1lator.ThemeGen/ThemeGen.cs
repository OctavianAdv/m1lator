using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace M1lator.ThemeGen;

/// <summary>
/// Builds the "m1lator Midnight" theme from the stock BlueStacks theme (Client\Assets):
///  - navy backgrounds / greys  -> graphite neutrals with a faint violet tint
///  - blue + orange accents     -> violet accent family
///  - reds / greens / yellows   -> untouched (errors, success, warnings keep their meaning)
/// The same colour transform is applied to the XAML ThemeFile, ThemeConfig.ini and every PNG,
/// so icons and brushes stay consistent.
/// </summary>
static class ThemeGen
{
    public const string ThemeName = "m1lator";
    const string DisplayName = "m1lator Midnight";

    const double NeutralHue = 248, NeutralSatScale = 0.30;
    const double AccentHue = 262, HighlightHue = 280;
    const int PhotoColorThreshold = 6000;

    static readonly Dictionary<string, string> CornerRadii = new()
    {
        ["BeginnerGuideRadius"] = "8,8,8,8",
        ["ButtonCornerRadius"] = "6,6,6,6",
        ["MessageWindowRadius"] = "8,8,8,8",
        ["PopupRadius"] = "8,8,8,8",
        ["PreferenceDropDownRadius"] = "6,6,6,6",
        ["SettingsWindowRadius"] = "8,8,8,8",
        ["SidebarElementCornerRadius"] = "6,6,6,6",
        ["SpeedUpTipsRadius"] = "8,8,8,8",
        ["TabRadius"] = "6,6,0,0",
        ["TextBoxRadius"] = "6,6,6,6",
    };

    public static int Run(string srcDir, string outDir)
    {
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        int png = 0;
        foreach (var src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(outDir, Path.GetRelativePath(srcDir, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (src.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { RecolorPng(src, dst); png++; }
            else File.Copy(src, dst);
        }

        string themeFile = Path.Combine(outDir, "ThemeFile");
        string xaml = RecolorText(File.ReadAllText(themeFile));
        foreach (var (key, value) in CornerRadii)
            xaml = SetKeyed(xaml, key, value);
        xaml = SetKeyed(xaml, "ThemeDisplayName", DisplayName);
        xaml = SetKeyed(xaml, "themeName", ThemeName);
        xaml = Regex.Replace(xaml, "RadiusX=\"0\"(\\s+)RadiusY=\"0\"(\\s+Rect=\"0,0,68,68\")", "RadiusX=\"14\"$1RadiusY=\"14\"$2");
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

        bool blue = h >= 180 && h <= 260;
        bool orange = h >= 15 && h <= 50;
        bool vivid = s >= 0.45 && l > 0.25 && l < 0.85;

        // accents get a lightness floor so text/links stay readable on the dark graphite background
        if (vivid && blue) { h = AccentHue; s = Math.Max(s, 0.80); l = Math.Max(l, 0.62); }
        else if (vivid && orange) { h = HighlightHue; s = Math.Max(s, 0.80); l = Math.Max(l, 0.62); }
        else if (blue || s < 0.25) { h = NeutralHue; s *= NeutralSatScale; l = l < 0.5 ? l * 0.92 : l; }
        else return argb; // reds, greens, yellows keep their meaning

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
