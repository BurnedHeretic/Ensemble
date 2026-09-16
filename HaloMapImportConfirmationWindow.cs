using Ensemble.Services;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    /// <summary>
    /// Size-bounded confirmation dialog for Halo map imports.
    ///
    /// The old long native MessageBox could place its Yes/No buttons below the
    /// visible popup edge on smaller displays or higher DPI settings. This
    /// window keeps the action buttons in a fixed footer and scrolls only the
    /// explanatory text.
    /// </summary>
    internal sealed class HaloMapImportConfirmationWindow : Window
    {
        public HaloMapImportConfirmationWindow(
            string sourceName,
            string targetName,
            float coverageFraction,
            float sizeMultiplier)
        {
            Title = "Import Halo Map";
            Width = 620;
            MinWidth = 500;
            MaxWidth = 760;
            MinHeight = 320;
            MaxHeight = Math.Max(
                320,
                SystemParameters.WorkArea.Height - 72);
            Height = Math.Min(
                560,
                MaxHeight);
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            HaloWarsThemeService.Apply(this);

            Grid root = new()
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = new GridLength(
                        1,
                        GridUnitType.Star)
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            TextBlock heading = new()
            {
                Text = "NATIVE HALO REACH MAP IMPORT",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            TextBlock body = new()
            {
                Text =
                    $"Source: {sourceName}\n" +
                    $"Target: {targetName}\n\n" +
                    "Ensemble will import CORE BSP / ENVIRONMENT GEOMETRY ONLY.\n\n" +
                    "Halo FPS scenery objects, weapons, vehicles, characters, scripts and encounters are intentionally ignored; individual assets can be imported separately through the Objects menu.\n\n" +
                    $"Requested map coverage: {coverageFraction * 100.0f:0.#}%\n" +
                    $"Extra size multiplier: {sizeMultiplier:0.###}x\n\n" +
                    "The selected scale is baked into the generated Halo Wars UGX.\n\n" +
                    "Continue with the import?",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 10, 0)
            };

            ScrollViewer scroll = new()
            {
                Content = body,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            Border footer = new()
            {
                Padding = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = SystemColors.ControlDarkBrush
            };

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

            Button noButton = new()
            {
                Content = "NO",
                MinWidth = 96,
                MinHeight = 34,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            Button yesButton = new()
            {
                Content = "YES - IMPORT",
                MinWidth = 126,
                MinHeight = 34,
                IsDefault = true
            };

            yesButton.Click +=
                (_, _) =>
                {
                    DialogResult = true;
                };

            buttons.Children.Add(noButton);
            buttons.Children.Add(yesButton);
            footer.Child = buttons;

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }
    }
}
