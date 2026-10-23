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
            var slide = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(slide, 30);   // smooth enough for a logo, keeps the UI thread light
            shift.BeginAnimation(TranslateTransform.XProperty, slide);

            // soft glow whose colour cycles through the same palette
            var glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.55 };
            var cycle = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4), RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 0; i < colours.Length; i++)
                cycle.KeyFrames.Add(new LinearColorKeyFrame((Color)ColorConverter.ConvertFromString(colours[i]), KeyTime.FromPercent(i / (double)(colours.Length - 1))));
            Timeline.SetDesiredFrameRate(cycle, 30);
            glow.BeginAnimation(DropShadowEffect.ColorProperty, cycle);
            title.Effect = glow;

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

        static string GetSetting(string name, string fallback)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
                return key?.GetValue(name) as string ?? fallback;
        }
    }
}
