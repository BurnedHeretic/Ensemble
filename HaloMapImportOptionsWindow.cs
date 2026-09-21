using Ensemble.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    internal sealed class HaloMapImportOptionsWindow : Window
    {
        private readonly TextBox _coverageText;
        private readonly TextBox _multiplierText;

        public float CoverageFraction { get; private set; } = 0.90f;
        public float SizeMultiplier { get; private set; } = 1.0f;

        public HaloMapImportOptionsWindow()
        {
            Title = "Halo BSP Import Size";
            Width = 500;
            MinWidth = 440;
            MaxHeight = Math.Max(
                420,
                SystemParameters.WorkArea.Height - 90);
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            HaloWarsThemeService.Apply(this);

            ScrollViewer scroll = new()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            Grid root = new()
            {
                Margin = new Thickness(18)
            };

            for (int i = 0; i < 7; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock heading = new()
            {
                Text = "HALO BSP IMPORT SIZE",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            TextBlock info = new()
            {
                Text =
                    "Ensemble measures the geometry that was actually extracted, then fits that footprint to the current Halo Wars battlefield. This avoids tiny imports caused by distant/auxiliary source BSP metadata.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(info, 1);
            root.Children.Add(info);

            Grid coverageGrid = BuildFieldRow(
                "Map coverage (%)",
                "90",
                out _coverageText);
            Grid.SetRow(coverageGrid, 2);
            root.Children.Add(coverageGrid);

            TextBlock coverageHelp = new()
            {
                Text = "100 = fill the available map footprint. Values above 100 intentionally extend beyond it.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(155, 3, 0, 10)
            };
            Grid.SetRow(coverageHelp, 3);
            root.Children.Add(coverageHelp);

            Grid multiplierGrid = BuildFieldRow(
                "Extra size multiplier",
                "1.0",
                out _multiplierText);
            Grid.SetRow(multiplierGrid, 4);
            root.Children.Add(multiplierGrid);

            TextBlock multiplierHelp = new()
            {
                Text =
                    "Use 2.0 for twice the auto-fit size, 0.5 for half size. " +
                    "Very small source/Halo Wars scale mismatches can be corrected down to 0.001x.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(155, 3, 0, 16)
            };
            Grid.SetRow(multiplierHelp, 5);
            root.Children.Add(multiplierHelp);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 2)
            };

            Button cancel = new()
            {
                Content = "CANCEL",
                MinWidth = 92,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            Button import = new()
            {
                Content = "IMPORT",
                MinWidth = 100,
                MinHeight = 32,
                IsDefault = true
            };
            import.Click += Import_Click;

            buttons.Children.Add(cancel);
            buttons.Children.Add(import);
            Grid.SetRow(buttons, 6);
            root.Children.Add(buttons);

            scroll.Content = root;
            Content = scroll;
        }

        private static Grid BuildFieldRow(
            string label,
            string value,
            out TextBox textBox)
        {
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelBlock = new()
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 10, 3)
            };

            textBox = new TextBox
            {
                Text = value,
                MinWidth = 120,
                Margin = new Thickness(0, 3, 0, 3)
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(textBox);
            return grid;
        }

        private void Import_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryParseFloat(_coverageText.Text, out float coveragePercent) ||
                coveragePercent < 5 || coveragePercent > 300)
            {
                MessageBox.Show(
                    this,
                    "Map coverage must be between 5 and 300 percent.",
                    "Halo BSP Import Size",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!TryParseFloat(_multiplierText.Text, out float multiplier) ||
                multiplier < 0.001f || multiplier > 100.0f)
            {
                MessageBox.Show(
                    this,
                    "Extra size multiplier must be between 0.001 and 100.0.",
                    "Halo BSP Import Size",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CoverageFraction = coveragePercent / 100.0f;
            SizeMultiplier = multiplier;
            DialogResult = true;
        }

        private static bool TryParseFloat(
            string? text,
            out float value)
        {
            return float.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.CurrentCulture,
                       out value)
                   ||
                   float.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value);
        }
    }
}
