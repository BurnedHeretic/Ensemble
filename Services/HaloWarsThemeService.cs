using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ensemble.Services
{
    internal static class HaloWarsThemeService
    {
        private static readonly Color Background = Color.FromRgb(0x06, 0x11, 0x1B);
        private static readonly Color Panel = Color.FromRgb(0x0A, 0x18, 0x24);
        private static readonly Color Raised = Color.FromRgb(0x10, 0x27, 0x38);
        private static readonly Color Accent = Color.FromRgb(0x43, 0xC8, 0xF5);
        private static readonly Color Line = Color.FromRgb(0x31, 0x5E, 0x73);
        private static readonly Color Text = Color.FromRgb(0xEA, 0xF9, 0xFF);
        private static readonly Color Muted = Color.FromRgb(0x8D, 0xA8, 0xB8);

        public static void Apply(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            FontFamily displayFont = ResolveBundledOrInstalledFont(
                "Handel Gothic Regular (H2).ttf",
                "Handel Gothic",
                "Handel Gothic",
                "HandelGothic BT",
                "Handel Gothic BT",
                "Bahnschrift SemiCondensed",
                "Segoe UI");

            FontFamily bodyFont = ResolveBundledOrInstalledFont(
                "Conduit ITC Regular (ODST).otf",
                "Conduit ITC",
                "Conduit ITC",
                "ConduitITC",
                "Bahnschrift",
                "Segoe UI");

            window.Background = new SolidColorBrush(Background);
            window.Foreground = new SolidColorBrush(Text);
            window.FontFamily = bodyFont;

            RemapVisualTree(window, displayFont, bodyFont);

            if (window is MainWindow main)
            {
                main.Title = "ENSEMBLE // HALO WARS MAP EDITOR";
                AddTopAccent(main);
            }
        }

        private static FontFamily ResolveBundledOrInstalledFont(
            string bundledFileName,
            string bundledFamilyName,
            params string[] installedCandidates)
        {
            string fontsDirectory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts");

            string bundledPath =
                Path.Combine(
                    fontsDirectory,
                    bundledFileName);

            if (File.Exists(bundledPath))
            {
                try
                {
                    string directoryWithSlash =
                        fontsDirectory.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)
                        +
                        Path.DirectorySeparatorChar;

                    return new FontFamily(
                        new Uri(
                            directoryWithSlash,
                            UriKind.Absolute),
                        "./#" +
                        bundledFamilyName);
                }
                catch
                {
                    // Fall back to installed/system fonts.
                }
            }

            return ResolveFont(
                installedCandidates);
        }

        private static FontFamily ResolveFont(
            params string[] candidates)
        {
            foreach (string candidate
                     in candidates)
            {
                bool installed =
                    Fonts.SystemFontFamilies.Any(
                        family =>
                            family.Source.Equals(
                                candidate,
                                StringComparison.OrdinalIgnoreCase));

                if (installed)
                {
                    return new FontFamily(
                        candidate);
                }
            }

            return new FontFamily(
                "Segoe UI");
        }

        private static void RemapVisualTree(
            DependencyObject root,
            FontFamily displayFont,
            FontFamily bodyFont)
        {
            if (root is Control control)
                control.FontFamily = bodyFont;

            if (root is Menu menu)
            {
                menu.FontFamily = displayFont;
                menu.FontSize = 13;
            }

            if (root is MenuItem menuItem)
                menuItem.FontFamily = displayFont;

            if (root is Button button)
            {
                button.FontFamily = displayFont;
                button.FontWeight = FontWeights.SemiBold;
            }

            if (root is StatusBar status)
                status.FontFamily = displayFont;

            if (root is TextBlock textBlock)
            {
                bool looksLikeHeader =
                    textBlock.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight() ||
                    (!string.IsNullOrWhiteSpace(textBlock.Text) &&
                     textBlock.Text.Length <= 40 &&
                     textBlock.Text == textBlock.Text.ToUpperInvariant());

                textBlock.FontFamily = looksLikeHeader ? displayFont : bodyFont;

                if (TryGetSolidColor(textBlock.Foreground, out Color foreground))
                {
                    if (IsClose(foreground, Colors.White))
                        textBlock.Foreground = new SolidColorBrush(Text);
                    else if (IsClose(foreground, Color.FromRgb(0xAA, 0xAA, 0xAA)))
                        textBlock.Foreground = new SolidColorBrush(Muted);
                }
            }

            if (root is Border border)
            {
                if (TryGetSolidColor(border.Background, out Color background))
                {
                    if (IsClose(background, Color.FromRgb(0x1E, 0x1E, 0x1E)))
                        border.Background = new SolidColorBrush(Background);
                    else if (IsClose(background, Color.FromRgb(0x25, 0x25, 0x26)))
                        border.Background = new SolidColorBrush(Panel);
                    else if (IsClose(background, Color.FromRgb(0x33, 0x33, 0x37)))
                        border.Background = new SolidColorBrush(Raised);
                }

                if (TryGetSolidColor(border.BorderBrush, out Color borderColor) &&
                    IsClose(borderColor, Color.FromRgb(0x3F, 0x3F, 0x46)))
                {
                    border.BorderBrush = new SolidColorBrush(Line);
                }
            }

            if (root is GridSplitter splitter)
                splitter.Background = new SolidColorBrush(Line);

            if (root is TextBox textBox &&
                textBox.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase))
            {
                textBox.FontFamily = ResolveFont("Cascadia Mono", "Consolas");
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                RemapVisualTree(VisualTreeHelper.GetChild(root, i), displayFont, bodyFont);
        }

        private static void AddTopAccent(MainWindow window)
        {
            if (window.Content is not DockPanel dock)
                return;

            bool exists = dock.Children.OfType<Border>()
                .Any(border => string.Equals(border.Tag as string, "ENSEMBLE_HW_ACCENT", StringComparison.Ordinal));

            if (exists)
                return;

            Border accent = new Border
            {
                Tag = "ENSEMBLE_HW_ACCENT",
                Height = 2,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Colors.Transparent, 0),
                        new GradientStop(Accent, 0.25),
                        new GradientStop(Color.FromRgb(0x9B, 0xEA, 0xFF), 0.5),
                        new GradientStop(Accent, 0.75),
                        new GradientStop(Colors.Transparent, 1)
                    },
                    new Point(0, 0),
                    new Point(1, 0))
            };

            DockPanel.SetDock(accent, Dock.Top);
            dock.Children.Insert(Math.Min(1, dock.Children.Count), accent);
        }

        private static bool TryGetSolidColor(Brush? brush, out Color color)
        {
            if (brush is SolidColorBrush solid)
            {
                color = solid.Color;
                return true;
            }

            color = default;
            return false;
        }

        private static bool IsClose(Color a, Color b)
        {
            const int tolerance = 3;
            return Math.Abs(a.R - b.R) <= tolerance &&
                   Math.Abs(a.G - b.G) <= tolerance &&
                   Math.Abs(a.B - b.B) <= tolerance;
        }
    }
}
