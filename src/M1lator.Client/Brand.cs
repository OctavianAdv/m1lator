using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace M1lator.Client
{
    /// <summary>
    /// m1lator branding for the BlueStacks client (Bluestacks.exe). Calls are injected by the patcher:
    ///   App.Main start                    -> Init()                  global UI font
    ///   TopBar constructor end            -> ApplyTopBar(this)       RGB "M1LATOR", no BlueStacks logo/version
    ///   AboutSettingsControl ctor end     -> ApplyAbout(this)        m1lator version instead of BlueStacks'
    ///   SettingsWindow.CreateAllButtons   -> FilterSettingsTabs/MapSettingsTab   hide tabs we don't use
    ///   SettingsWindow.ChangeSettingsTab  -> MapSettingsTab          never open a hidden tab
    ///   SettingsWindow ctor end           -> StyleSettings(this)     pill tabs, large title
    /// Settings: HKLM\SOFTWARE\BlueStacks_bgp64\m1lator
    ///   Font     REG_SZ  UI font: "Geist" (default) | "Space Grotesk" | "System" (keep Windows font)
    ///   Version  REG_SZ  m1lator version shown in Settings -> About (written by the installer)
    /// Fonts live in Client\m1lator-fonts. Every method swallows errors: branding must never break the client.
    /// </summary>
    public static class Brand
    {
        const string KeyPath = @"SOFTWARE\BlueStacks_bgp64\m1lator";
        const string Title = "M1LATOR";
        const BindingFlags Field = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static readonly string FontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "m1lator-fonts");

        public static void Init()
        {
            try
            {
                string name = GetSetting("Font", "Geist");
                if (string.Equals(name, "System", StringComparison.OrdinalIgnoreCase)) return;
                FontFamily font = LoadFont(name);
                if (font == null) return;
                // FontFamily is inherited: overriding the default on the root element types reaches every control
                // (BlueStacks never sets an explicit font). Must run before the first window is created.
                foreach (Type t in new[] { typeof(Window), typeof(ToolTip), typeof(ContextMenu) })
                    TextElement.FontFamilyProperty.OverrideMetadata(t, new FrameworkPropertyMetadata(font, FrameworkPropertyMetadataOptions.Inherits));
            }
            catch { }
            try
            {
                // Liquid Glass: give every BlueStacks window the blurred-wallpaper glass base once it is shown
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((s, e) => { if (s is Window w) ApplyGlass(w); }));
            }
            catch { }
        }

        // ---------------------------------------------------------------- glass
        // Liquid Glass the way Windows' own Mica works: the desktop wallpaper, heavily blurred and tinted, painted as the
        // window's base layer; the theme's translucent chrome brushes (ThemeGen Preset.Glass) show it through.
        // (DWM blur-behind does not render on BlueStacks' layered AllowsTransparency window, see v1.10 notes.)
        // Blur is free: the wallpaper is decoded at 48 px and smoothly upscaled - no Effect, no per-frame cost.
        // m1lator\Glass     REG_DWORD 0 = off, 1 = on          (Theme.bat, from the preset)
        // m1lator\GlassTint REG_SZ   #AARRGGBB tint over the wallpaper
        // m1lator\Accent    REG_SZ   #RRGGBB used for the fallback light blobs (no wallpaper image)
        static Brush glassBrush;

        public static void ApplyGlass(Window window)
        {
            try
            {
                if (GetDword("Glass", 0) <= 0) return;
                // the window's root surface: MainWindow.OuterBorder, or a Border as window content (keeps its corner radius)
                Border root = Get(window, "OuterBorder") as Border ?? window.Content as Border;
                if (root == null) return;
                if (glassBrush == null) glassBrush = BuildGlassBrush();
                root.Background = glassBrush;
            }
            catch { }
        }

        static Brush BuildGlassBrush()
        {
            Color tint = ParseColor(GetSetting("GlassTint", "#B81C1C1E"));
            Color accent = ParseColor(GetSetting("Accent", "#0A84FF"));
            var group = new DrawingGroup();
            var full = new Rect(0, 0, 160, 90);

            ImageSource wallpaper = LoadWallpaperThumb();
            if (wallpaper != null)
            {
                group.Children.Add(new ImageDrawing(wallpaper, full));
            }
            else
            {
                // no image wallpaper (solid colour / slideshow): soft accent light blobs, iOS-wallpaper style
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)), null, new RectangleGeometry(full)));
                group.Children.Add(Blob(accent, 0.25, 0.20, 0.9));
                group.Children.Add(Blob(Color.FromRgb(0xBF, 0x5A, 0xF2), 0.85, 0.85, 0.8));
                group.Children.Add(Blob(Color.FromRgb(0x64, 0xD2, 0xFF), 0.90, 0.10, 0.5));
            }
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(tint), null, new RectangleGeometry(full)));
            // specular sheen across the top, the "liquid" highlight
            var sheen = new LinearGradientBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90);
            group.Children.Add(new GeometryDrawing(sheen, null, new RectangleGeometry(new Rect(0, 0, 160, 18))));

            var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);   // smooth upscale = the blur
            RenderOptions.SetCachingHint(brush, CachingHint.Cache);
            brush.Freeze();
            return brush;
        }

        static GeometryDrawing Blob(Color c, double x, double y, double strength)
        {
            var g = new RadialGradientBrush(Color.FromArgb((byte)(0xFF * strength), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B))
            {
                Center = new Point(x, y), GradientOrigin = new Point(x, y), RadiusX = 0.6, RadiusY = 0.9,
            };
            return new GeometryDrawing(g, null, new RectangleGeometry(new Rect(0, 0, 160, 90)));
        }

        static ImageSource LoadWallpaperThumb()
        {
            try
            {
                string path;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    path = key?.GetValue("WallPaper") as string;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(path);
                img.DecodePixelWidth = 48;                                      // tiny decode -> heavy blur when stretched
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromArgb(0xB8, 0x1C, 0x1C, 0x1E); }
        }

        public static void ApplyTopBar(object topBar)
        {
            try
            {
                // BlueStacks re-shows these when tabs change (AppTabButtons): keep them hidden
                KeepHidden(Get(topBar, "mVersionText"));
                KeepHidden(Get(topBar, "mTitleIcon"));
                if (Get(topBar, "mTitleText") is TextBlock title) MakeWordmark(title);
            }
            catch { }
        }

        public static void ApplyAbout(object about)
        {
            try
            {
                if (Get(about, "mVersionLabel") is ContentControl version)
                    version.Content = "m1lator " + GetSetting("Version", "");
            }
            catch { }
        }

        // ---------------------------------------------------------------- settings window
        // Hidden tabs: m1lator\HiddenSettings (comma separated BlueStacks tab ids), default below.
        //   STRING_NOTIFICATION          notifications are disabled by m1lator anyway
        //   STRING_GAME_SETTINGS         BlueStacks presets for PUBG / Free Fire / COD only
        //   STRING_SHORTCUT_KEY_SETTINGS only F11 is active
        const string DefaultHiddenSettings = "STRING_NOTIFICATION,STRING_GAME_SETTINGS,STRING_SHORTCUT_KEY_SETTINGS";

        // SettingsWindow.CreateAllButtons start: drop hidden tabs before the buttons are built
        public static void FilterSettingsTabs(object window)
        {
            try
            {
                if (!(GetProp(window, "SettingsControlNameList") is System.Collections.Generic.List<string> tabs)) return;
                string[] hidden = GetSetting("HiddenSettings", DefaultHiddenSettings).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                tabs.RemoveAll(t => Array.IndexOf(hidden, t) >= 0);
                var start = window.GetType().GetProperty("StartUpTab");
                if (start != null) start.SetValue(window, MapSettingsTab(window, start.GetValue(window, null) as string), null);
            }
            catch { }
        }

        // SettingsWindow.ChangeSettingsTab / CreateAllButtons: a request for a hidden tab opens Engine instead
        // (BlueStacks indexes its button dictionary by tab id, so an unknown tab would throw)
        public static string MapSettingsTab(object window, string tab)
        {
            try
            {
                if (!(GetProp(window, "SettingsControlNameList") is System.Collections.Generic.List<string> tabs) || tabs.Count == 0 || tabs.Contains(tab)) return tab;
                return tabs.Contains("STRING_ENGINE_SETTING") ? "STRING_ENGINE_SETTING" : tabs[0];
            }
            catch { return tab; }
        }

        // SettingsWindow constructor end: macOS System Settings style (colours come from the m1lator theme)
        public static void StyleSettings(object window)
        {
            try
            {
                if (!(window is FrameworkElement root)) return;
                RoutedEventHandler apply = null;
                apply = (s, e) =>
                {
                    root.Loaded -= apply;
                    try
                    {
                        if (Get(window, "mLblBlueStacksSettings") is Label title)
                        {
                            title.Content = "Settings";
                            title.FontSize = 20;
                            title.FontWeight = FontWeights.Bold;
                        }
                        Hide(Get(window, "mSelectedLine"));          // selection is shown by the pill instead
                        if (Get(window, "mSettingsButtons") is System.Collections.IDictionary buttons)
                            foreach (var b in buttons.Values)
                                if (b is Control button) StyleTabButton(button);
                    }
                    catch { }
                };
                root.Loaded += apply;
            }
            catch { }
        }

        static void StyleTabButton(Control button)
        {
            button.Margin = new Thickness(10, 2, 10, 2);
            button.MinHeight = 36;
            button.FontSize = 13.5;
            button.ApplyTemplate();
            // round the template's background border into a pill
            for (DependencyObject d = button; d != null && VisualTreeHelper.GetChildrenCount(d) > 0;)
            {
                d = VisualTreeHelper.GetChild(d, 0);
                if (d is Border border) { border.CornerRadius = new CornerRadius(8); break; }
            }
        }

        // ---------------------------------------------------------------- wordmark

        static void MakeWordmark(TextBlock title)
        {
            title.Text = Title;
            title.FontWeight = FontWeights.Bold;
            title.FontSize = Math.Max(title.FontSize, 15);
            FontFamily grotesk = LoadFont("Space Grotesk");
            if (grotesk != null) title.FontFamily = grotesk;

            // flowing rainbow: Apple system colours, repeated, slid across the text forever (first stop == last stop)
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), SpreadMethod = GradientSpreadMethod.Repeat };
            string[] colours = { "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2", "#FF453A" };
            for (int i = 0; i < colours.Length; i++)
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colours[i]), i / (double)(colours.Length - 1)));
            var shift = new TranslateTransform();
            brush.RelativeTransform = shift;
            title.Foreground = brush;
            // NOT a forever animation: BlueStacks' window is layered (AllowsTransparency), so every animation frame makes
            // WPF re-render and hand the whole window to Windows (GPU -> CPU copy) - a constant cost while gaming.
            // The rainbow flows twice at startup and once whenever the mouse is over the logo; otherwise it is static.
            void Flow(int times)
            {
                var slide = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(3)) { RepeatBehavior = new RepeatBehavior(times) };
                Timeline.SetDesiredFrameRate(slide, 30);
                shift.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            Flow(2);
            title.MouseEnter += (s, e) => Flow(1);

            // static soft glow in the theme accent
            Color glowColor;
            try { glowColor = (Color)ColorConverter.ConvertFromString(GetSetting("Accent", "#0A84FF")); }
            catch { glowColor = Color.FromRgb(0x0A, 0x84, 0xFF); }
            title.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.5, Color = glowColor };

            // BlueStacks writes the instance name into the title later (MainWindow.SetWindowTitle): keep ours
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(title, (s, e) => { if (title.Text != Title) title.Text = Title; });
        }

        // ---------------------------------------------------------------- helpers

        static FontFamily LoadFont(string family)
        {
            if (!Directory.Exists(FontDir)) return null;
            var font = new FontFamily(new Uri(FontDir.TrimEnd('\\') + "\\"), "./#" + family);
            return font.FamilyTypefaces.Count > 0 ? font : null;
        }

        static object Get(object owner, string field)
        {
            // fields may live on a base class (SettingsWindowBase)
            for (Type t = owner?.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(field, Field | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(owner);
            }
            return null;
        }

        static object GetProp(object owner, string name) => owner?.GetType().GetProperty(name)?.GetValue(owner, null);

        static void Hide(object element)
        {
            if (element is UIElement ui) ui.Visibility = Visibility.Collapsed;
        }

        static void KeepHidden(object element)
        {
            if (!(element is UIElement ui)) return;
            ui.Visibility = Visibility.Collapsed;
            DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement))
                .AddValueChanged(ui, (s, e) => { if (ui.Visibility != Visibility.Collapsed) ui.Visibility = Visibility.Collapsed; });
        }

        static int GetDword(string name, int fallback)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
                return key?.GetValue(name) is int v ? v : fallback;
        }

        static string GetSetting(string name, string fallback)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
                return key?.GetValue(name) as string ?? fallback;
        }
    }
}
