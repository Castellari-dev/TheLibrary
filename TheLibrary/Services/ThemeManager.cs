using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TheLibrary.Services
{
    public class AccentOption : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public Color Light { get; set; }
        public Color Dark { get; set; }

        public Color Current =>
            string.Equals(ThemeManager.CurrentTheme, ThemeManager.ThemeDark, StringComparison.OrdinalIgnoreCase)
                ? Dark : Light;

        public Brush Preview
        {
            get
            {
                var b = new SolidColorBrush(Current);
                b.Freeze();
                return b;
            }
        }

        public override string ToString() => Name;

        public event PropertyChangedEventHandler PropertyChanged;

        internal void RaisePreviewChanged()
        {
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new PropertyChangedEventArgs("Current"));
            h(this, new PropertyChangedEventArgs("Preview"));
        }
    }

    public static class ThemeManager
    {
        public const string ThemeLight = "Claro";
        public const string ThemeDark = "Escuro";

        public static readonly string[] Themes = { ThemeLight, ThemeDark };

        public static readonly List<AccentOption> Accents = new List<AccentOption>
        {
            new AccentOption { Name = "Verde",    Light = C("#2E7D32"), Dark = C("#43A047") },
            new AccentOption { Name = "Azul",     Light = C("#1565C0"), Dark = C("#42A5F5") },
            new AccentOption { Name = "Roxo",     Light = C("#6A1B9A"), Dark = C("#AB47BC") },
            new AccentOption { Name = "Vermelho", Light = C("#C62828"), Dark = C("#EF5350") },
            new AccentOption { Name = "Laranja",  Light = C("#EF6C00"), Dark = C("#FFA726") },
            new AccentOption { Name = "Teal",     Light = C("#00796B"), Dark = C("#26A69A") },
            new AccentOption { Name = "Grafite",  Light = C("#37474F"), Dark = C("#78909C") },
            new AccentOption { Name = "Rosa",     Light = C("#AD1457"), Dark = C("#EC407A") }
        };

        public static string CurrentTheme { get; private set; } = ThemeLight;
        public static string CurrentAccent { get; private set; } = "Verde";
        public static bool IsDark => string.Equals(CurrentTheme, ThemeDark, StringComparison.OrdinalIgnoreCase);

        public static event EventHandler ThemeChanged;

        private const string LightPath = "Themes/Light.xaml";
        private const string DarkPath = "Themes/Dark.xaml";

        private static ResourceDictionary _palette;
        private static bool _hooked;

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public static AccentOption FindAccent(string name)
        {
            foreach (var a in Accents)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return Accents[0];
        }

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded));
        }

        public static void Apply(string theme, string accentName)
        {
            var app = Application.Current;
            if (app == null) return;

            Init();

            bool dark = string.Equals(theme, ThemeDark, StringComparison.OrdinalIgnoreCase);
            CurrentTheme = dark ? ThemeDark : ThemeLight;

            var accent = FindAccent(accentName);
            CurrentAccent = accent.Name;

            var dict = new ResourceDictionary
            {
                Source = new Uri(dark ? DarkPath : LightPath, UriKind.Relative)
            };

            SwapPalette(app.Resources.MergedDictionaries, dict);

            Color baseColor = dark ? accent.Dark : accent.Light;
            Color surface = ColorFrom(dict, "Surface", dark ? C("#191F26") : Colors.White);

            app.Resources["Accent"] = Freeze(new SolidColorBrush(baseColor));
            app.Resources["AccentHover"] = Freeze(new SolidColorBrush(Shift(baseColor, dark ? 0.12 : -0.14)));
            app.Resources["AccentSoft"] = Freeze(new SolidColorBrush(Mix(baseColor, surface, dark ? 0.80 : 0.86)));
            app.Resources["AccentText"] = Freeze(new SolidColorBrush(ContrastText(baseColor)));

            foreach (var a in Accents) a.RaisePreviewChanged();

            foreach (Window w in app.Windows) ApplyToWindow(w);

            var handler = ThemeChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static void SwapPalette(Collection<ResourceDictionary> merged, ResourceDictionary dict)
        {
            int idx = _palette != null ? merged.IndexOf(_palette) : -1;

            if (idx < 0)
            {
                for (int i = 0; i < merged.Count; i++)
                {
                    var src = merged[i].Source != null ? merged[i].Source.OriginalString : null;
                    if (string.IsNullOrEmpty(src)) continue;

                    if (src.IndexOf("Light.xaml", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        src.IndexOf("Dark.xaml", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        idx = i;
                        break;
                    }
                }
            }

            if (idx >= 0) merged[idx] = dict;
            else merged.Insert(0, dict);

            _palette = dict;
        }

        private static Color ColorFrom(ResourceDictionary dict, string key, Color fallback)
        {
            var brush = dict[key] as SolidColorBrush;
            return brush != null ? brush.Color : fallback;
        }


        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            var w = sender as Window;
            if (w != null) ApplyToWindow(w);
        }

        public static void ApplyToWindow(Window w)
        {
            if (w == null) return;

            if (IsUnset(w, Control.BackgroundProperty))
                w.SetResourceReference(Control.BackgroundProperty, "Bg");

            if (IsUnset(w, Control.ForegroundProperty))
                w.SetResourceReference(Control.ForegroundProperty, "Text");

            ApplyTitleBar(w, IsDark);
        }

        private static bool IsUnset(DependencyObject d, DependencyProperty p)
        {
            var source = DependencyPropertyHelper.GetValueSource(d, p).BaseValueSource;
            return source == BaseValueSource.Default || source == BaseValueSource.Inherited;
        }


        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;  
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;         

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                int X, int Y, int cx, int cy, uint uFlags);

        private static void ApplyTitleBar(Window w, bool dark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;

                int on = dark ? 1 : 0;

                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref on, sizeof(int));

                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch
            {
                // Não faz nada se falhar, porque não é crítico. e eu fiquei com preguiça de testar se é Win10 ou Win11, e se a API existe, etc.
            }
        }

        private static Brush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        private static Color Shift(Color c, double amount)
        {
            if (amount >= 0)
                return Color.FromRgb(
                    (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                    (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                    (byte)Math.Min(255, c.B + (255 - c.B) * amount));

            double f = 1 + amount;
            return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        private static Color Mix(Color a, Color b, double weightB)
        {
            double wa = 1 - weightB;
            return Color.FromRgb(
                (byte)(a.R * wa + b.R * weightB),
                (byte)(a.G * wa + b.G * weightB),
                (byte)(a.B * wa + b.B * weightB));
        }

        private static readonly Color TextOnLight = Color.FromRgb(0x1B, 0x22, 0x27);

        private static Color ContrastText(Color bg)
        {
            double bgLum = RelativeLuminance(bg);
            double withWhite = ContrastRatio(bgLum, RelativeLuminance(Colors.White));
            double withDark = ContrastRatio(bgLum, RelativeLuminance(TextOnLight));
            return withDark > withWhite ? TextOnLight : Colors.White;
        }

        private static double ContrastRatio(double l1, double l2)
        {
            double hi = Math.Max(l1, l2);
            double lo = Math.Min(l1, l2);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        private static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}