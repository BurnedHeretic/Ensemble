using Ensemble.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    internal sealed class HaloBspOptimizeWindowV36 : Window
    {
        private readonly TextBox
            _gridText;

        public int CellsPerAxis
        {
            get;
            private set;
        } = 4;

        public HaloBspOptimizeWindowV36(
            string meshName,
            float currentScale)
        {
            Title =
                "Finalize Halo BSP";

            Width =
                570;

            MinHeight =
                390;

            MaxHeight =
                Math.Max(
                    390,
                    SystemParameters.WorkArea.Height -
                    80);

            SizeToContent =
                SizeToContent.Height;

            ResizeMode =
                ResizeMode.NoResize;

            WindowStartupLocation =
                WindowStartupLocation.CenterOwner;

            ShowInTaskbar =
                false;

            HaloWarsThemeService.Apply(
                this);

            Grid root =
                new Grid
                {
                    Margin =
                        new Thickness(
                            20)
                };

            for (int i = 0;
                 i < 7;
                 i++)
            {
                root.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height =
                            GridLength.Auto
                    });
            }

            TextBlock heading =
                new TextBlock
                {
                    Text =
                        "FINALIZE / OPTIMIZE HALO BSP",

                    FontSize =
                        18,

                    FontWeight =
                        FontWeights.Bold,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            14)
                };

            Grid.SetRow(
                heading,
                0);

            root.Children.Add(
                heading);

            TextBlock info =
                new TextBlock
                {
                    Text =
                        "Use this after you are happy with the imported BSP's position, rotation and scale. Ensemble will replace the single map-sized object with spatial UGX chunks whose origins sit close to their geometry. This is intended to prevent Halo Wars from culling a huge BSP from certain camera positions / angles.",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            14)
                };

            Grid.SetRow(
                info,
                1);

            root.Children.Add(
                info);

            TextBlock mesh =
                new TextBlock
                {
                    Text =
                        $"Selected: {meshName}\nCommitted custom-mesh scale: {currentScale:0.###}x",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Opacity =
                        0.82,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            14)
                };

            Grid.SetRow(
                mesh,
                2);

            root.Children.Add(
                mesh);

            Grid field =
                new Grid();

            field.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            170)
                });

            field.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            TextBlock label =
                new TextBlock
                {
                    Text =
                        "Spatial cells per axis",

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            _gridText =
                new TextBox
                {
                    Text =
                        "4",

                    MinWidth =
                        120
                };

            Grid.SetColumn(
                label,
                0);

            Grid.SetColumn(
                _gridText,
                1);

            field.Children.Add(
                label);

            field.Children.Add(
                _gridText);

            Grid.SetRow(
                field,
                3);

            root.Children.Add(
                field);

            TextBlock help =
                new TextBlock
                {
                    Text =
                        "4 means a 4×4 grid (empty cells are skipped). Higher values create more, smaller Halo Wars objects and usually improve culling, but require more unused rigid UGX slots. Recommended: 3-5.",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Opacity =
                        0.72,

                    Margin =
                        new Thickness(
                            170,
                            6,
                            0,
                            14)
                };

            Grid.SetRow(
                help,
                4);

            root.Children.Add(
                help);

            TextBlock warning =
                new TextBlock
                {
                    Text =
                        "After finalization, treat the chunks as game-ready output. If you want to make a major transform change, it is cleaner to undo/re-import the single BSP and finalize it again.",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            16)
                };

            Grid.SetRow(
                warning,
                5);

            root.Children.Add(
                warning);

            StackPanel buttons =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    HorizontalAlignment =
                        HorizontalAlignment.Right
                };

            Button cancel =
                new Button
                {
                    Content =
                        "CANCEL",

                    MinWidth =
                        96,

                    MinHeight =
                        34,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            8,
                            0),

                    IsCancel =
                        true
                };

            Button finalize =
                new Button
                {
                    Content =
                        "FINALIZE BSP",

                    MinWidth =
                        128,

                    MinHeight =
                        34,

                    IsDefault =
                        true
                };

            finalize.Click +=
                Finalize_Click;

            buttons.Children.Add(
                cancel);

            buttons.Children.Add(
                finalize);

            Grid.SetRow(
                buttons,
                6);

            root.Children.Add(
                buttons);

            Content =
                root;
        }

        private void Finalize_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!int.TryParse(
                    _gridText.Text,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out int value) ||
                value < 2 ||
                value > 8)
            {
                MessageBox.Show(
                    this,
                    "Spatial cells per axis must be a whole number from 2 to 8.",
                    "Finalize Halo BSP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            CellsPerAxis =
                value;

            DialogResult =
                true;
        }
    }
}
