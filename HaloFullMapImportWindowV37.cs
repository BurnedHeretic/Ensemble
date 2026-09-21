using Ensemble.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    internal sealed class HaloFullMapImportWindowV37 :
        Window
    {
        private readonly TextBox
            _coverageText;

        private readonly TextBox
            _multiplierText;

        private readonly TextBox
            _verticalOffsetText;

        private readonly TextBox
            _smoothingText;

        private readonly TextBox
            _slopeText;

        private readonly TextBox
            _cellsText;

        private readonly CheckBox
            _visualCheck;

        private readonly CheckBox
            _terrainCheck;

        private readonly CheckBox
            _optimizeCheck;

        private readonly ComboBox
            _surfaceMode;

        public float CoverageFraction
        {
            get;
            private set;
        } = 0.90f;

        public float SizeMultiplier
        {
            get;
            private set;
        } = 1.0f;

        public float VerticalOffset
        {
            get;
            private set;
        }

        public int SmoothingPasses
        {
            get;
            private set;
        } = 1;

        public float MaxSlopeDegrees
        {
            get;
            private set;
        } = 72.0f;

        public int CellsPerAxis
        {
            get;
            private set;
        } = 4;

        public bool ImportVisualBsp =>
            _visualCheck.IsChecked ==
            true;

        public bool GenerateGameplayTerrain =>
            _terrainCheck.IsChecked ==
            true;

        public bool OptimizeVisualBsp =>
            _optimizeCheck.IsChecked ==
            true &&
            ImportVisualBsp;

        public HaloTerrainSurfaceMode SurfaceMode
        {
            get;
            private set;
        } =
            HaloTerrainSurfaceMode.LowestWalkable;

        public HaloFullMapImportWindowV37()
        {
            Title =
                "Import MCC Halo Map";

            Width =
                650;

            MinHeight =
                650;

            MaxHeight =
                Math.Max(
                    650,
                    SystemParameters.WorkArea.Height -
                    70);

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

            ScrollViewer scroll =
                new ScrollViewer
                {
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto,

                    HorizontalScrollBarVisibility =
                        ScrollBarVisibility.Disabled
                };

            Grid root =
                new Grid
                {
                    Margin =
                        new Thickness(
                            20)
                };

            for (int i = 0;
                 i < 20;
                 i++)
            {
                root.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height =
                            GridLength.Auto
                    });
            }

            int row =
                0;

            TextBlock heading =
                new TextBlock
                {
                    Text =
                        "MCC HALO .MAP → HALO WARS IMPORT",

                    FontSize =
                        19,

                    FontWeight =
                        FontWeights.Bold,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                };

            Add(
                root,
                heading,
                row++);

            TextBlock intro =
                new TextBlock
                {
                    Text =
                        "One workflow for MCC Halo CE, Halo 2, Halo 3, Halo 3: ODST, Halo: Reach, Halo 4, and Halo 2 Anniversary Multiplayer .map files. " +
                        "Ensemble reads the source BSP through the all-games cache bridge, fits it to the open Halo Wars battlefield, " +
                        "then the same transformed geometry can be projected into the target XTD/XSD terrain.",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            14)
                };

            Add(
                root,
                intro,
                row++);

            _visualCheck =
                new CheckBox
                {
                    Content =
                        "Import BSP / environment visuals",

                    IsChecked =
                        true,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            4)
                };

            Add(
                root,
                _visualCheck,
                row++);

            _terrainCheck =
                new CheckBox
                {
                    Content =
                        "Generate Halo Wars gameplay terrain from BSP surfaces",

                    IsChecked =
                        true,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            4)
                };

            Add(
                root,
                _terrainCheck,
                row++);

            _optimizeCheck =
                new CheckBox
                {
                    Content =
                        "Finalize visual BSP into spatial Halo Wars chunks",

                    IsChecked =
                        true,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            12)
                };

            Add(
                root,
                _optimizeCheck,
                row++);

            TextBlock sizeHeading =
                SectionHeading(
                    "FIT / TRANSFORM");

            Add(
                root,
                sizeHeading,
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Map coverage (%)",
                    "90",
                    out _coverageText),
                row++);

            Add(
                root,
                Help(
                    "100 fills the target map footprint. 90 leaves a small border. Values above 100 intentionally extend past the map bounds."),
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Extra size multiplier",
                    "1.0",
                    out _multiplierText),
                row++);

            Add(
                root,
                Help(
                    "Applied after auto-fit. Very small values are supported for maps whose source-unit scale is unusually large."),
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Vertical offset",
                    "0",
                    out _verticalOffsetText),
                row++);

            Add(
                root,
                Help(
                    "Added to the target map's low representative ground height. The BSP bottom-centre pivot and generated terrain share this exact placement."),
                row++);

            TextBlock terrainHeading =
                SectionHeading(
                    "GAMEPLAY TERRAIN");

            Add(
                root,
                terrainHeading,
                row++);

            Grid surfaceRow =
                new Grid();

            surfaceRow.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            190)
                });

            surfaceRow.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            TextBlock surfaceLabel =
                new TextBlock
                {
                    Text =
                        "Heightfield surface",

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            12,
                            3)
                };

            _surfaceMode =
                new ComboBox
                {
                    MinWidth =
                        220,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            3)
                };

            _surfaceMode.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        "Lowest walkable surface",
                    Tag =
                        HaloTerrainSurfaceMode.LowestWalkable
                });

            _surfaceMode.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        "Highest walkable surface",
                    Tag =
                        HaloTerrainSurfaceMode.HighestWalkable
                });

            _surfaceMode.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        "Surface nearest target terrain",
                    Tag =
                        HaloTerrainSurfaceMode.NearestToTemplate
                });

            _surfaceMode.SelectedIndex =
                0;

            Grid.SetColumn(
                surfaceLabel,
                0);

            Grid.SetColumn(
                _surfaceMode,
                1);

            surfaceRow.Children.Add(
                surfaceLabel);

            surfaceRow.Children.Add(
                _surfaceMode);

            Add(
                root,
                surfaceRow,
                row++);

            Add(
                root,
                Help(
                    "Halo FPS BSPs can contain stacked floors, roofs and caves. Lowest is the safest default for RTS ground movement; use Highest for roof/platform-heavy maps."),
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Maximum ground slope (°)",
                    "72",
                    out _slopeText),
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Terrain smoothing passes",
                    "1",
                    out _smoothingText),
                row++);

            TextBlock optimizeHeading =
                SectionHeading(
                    "BSP FINALIZATION");

            Add(
                root,
                optimizeHeading,
                row++);

            Add(
                root,
                BuildFieldRow(
                    "Spatial cells per axis",
                    "4",
                    out _cellsText),
                row++);

            Add(
                root,
                Help(
                    "4 = up to 16 culling-friendly UGX chunks. Empty cells are skipped. 3-5 is recommended; higher values require more unused rigid UGX slots."),
                row++);

            Border note =
                new Border
                {
                    BorderThickness =
                        new Thickness(
                            1),

                    Padding =
                        new Thickness(
                            10),

                    Margin =
                        new Thickness(
                            0,
                            10,
                            0,
                            14),

                    Child =
                        new TextBlock
                        {
                            Text =
                                "Terrain generation updates XTD preview data now. On Save, Ensemble writes XTD and synchronizes XSD gameplay heights/pathing. " +
                                "The original Halo FPS scripts, encounters, weapons, vehicles and AI are not imported. " +
                                "For Halo CE Anniversary and Halo 2 Anniversary campaign maps, .map contains the classic Blam BSP; the remastered Saber visual layer is stored separately and is outside this .map importer.",

                            TextWrapping =
                                TextWrapping.Wrap
                        }
                };

            Add(
                root,
                note,
                row++);

            StackPanel buttons =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    HorizontalAlignment =
                        HorizontalAlignment.Right,

                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            2)
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

            Button import =
                new Button
                {
                    Content =
                        "IMPORT MAP",

                    MinWidth =
                        120,

                    MinHeight =
                        34,

                    IsDefault =
                        true
                };

            import.Click +=
                Import_Click;

            buttons.Children.Add(
                cancel);

            buttons.Children.Add(
                import);

            Add(
                root,
                buttons,
                row++);

            scroll.Content =
                root;

            Content =
                scroll;
        }

        private void Import_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!ImportVisualBsp &&
                !GenerateGameplayTerrain)
            {
                MessageBox.Show(
                    this,
                    "Select at least one import target: BSP visuals or gameplay terrain.",
                    "Halo Map Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!TryParseFloat(
                    _coverageText.Text,
                    out float coverage) ||
                coverage <
                    5 ||
                coverage >
                    300)
            {
                Warn(
                    "Map coverage must be between 5 and 300 percent.");

                return;
            }

            if (!TryParseFloat(
                    _multiplierText.Text,
                    out float multiplier) ||
                multiplier <
                    0.001f ||
                multiplier >
                    100.0f)
            {
                Warn(
                    "Extra size multiplier must be between 0.001 and 100.");

                return;
            }

            if (!TryParseFloat(
                    _verticalOffsetText.Text,
                    out float verticalOffset) ||
                !float.IsFinite(
                    verticalOffset) ||
                verticalOffset <
                    -10000 ||
                verticalOffset >
                    10000)
            {
                Warn(
                    "Vertical offset must be a finite value between -10000 and 10000.");

                return;
            }

            if (!TryParseFloat(
                    _slopeText.Text,
                    out float slope) ||
                slope <
                    5 ||
                slope >
                    89)
            {
                Warn(
                    "Maximum ground slope must be between 5 and 89 degrees.");

                return;
            }

            if (!int.TryParse(
                    _smoothingText.Text,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out int smoothing) ||
                smoothing <
                    0 ||
                smoothing >
                    8)
            {
                Warn(
                    "Terrain smoothing passes must be between 0 and 8.");

                return;
            }

            if (!int.TryParse(
                    _cellsText.Text,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out int cells) ||
                cells <
                    2 ||
                cells >
                    8)
            {
                Warn(
                    "Spatial cells per axis must be between 2 and 8.");

                return;
            }

            if (_surfaceMode.SelectedItem
                    is not ComboBoxItem surfaceItem ||
                surfaceItem.Tag
                    is not HaloTerrainSurfaceMode surfaceMode)
            {
                Warn(
                    "Choose a valid gameplay-terrain surface mode.");

                return;
            }

            CoverageFraction =
                coverage /
                100.0f;

            SizeMultiplier =
                multiplier;

            VerticalOffset =
                verticalOffset;

            MaxSlopeDegrees =
                slope;

            SmoothingPasses =
                smoothing;

            CellsPerAxis =
                cells;

            SurfaceMode =
                surfaceMode;

            DialogResult =
                true;
        }

        private void Warn(
            string message)
        {
            MessageBox.Show(
                this,
                message,
                "Halo Map Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static Grid BuildFieldRow(
            string label,
            string value,
            out TextBox textBox)
        {
            Grid grid =
                new Grid();

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            190)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            TextBlock labelBlock =
                new TextBlock
                {
                    Text =
                        label,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            12,
                            3)
                };

            textBox =
                new TextBox
                {
                    Text =
                        value,

                    MinWidth =
                        150,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            3)
                };

            Grid.SetColumn(
                labelBlock,
                0);

            Grid.SetColumn(
                textBox,
                1);

            grid.Children.Add(
                labelBlock);

            grid.Children.Add(
                textBox);

            return grid;
        }

        private static TextBlock SectionHeading(
            string text)
        {
            return new TextBlock
            {
                Text =
                    text,

                FontWeight =
                    FontWeights.Bold,

                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        7)
            };
        }

        private static TextBlock Help(
            string text)
        {
            return new TextBlock
            {
                Text =
                    text,

                TextWrapping =
                    TextWrapping.Wrap,

                Opacity =
                    0.72,

                Margin =
                    new Thickness(
                        190,
                        2,
                        0,
                        8)
            };
        }

        private static void Add(
            Grid root,
            UIElement child,
            int row)
        {
            Grid.SetRow(
                child,
                row);

            root.Children.Add(
                child);
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
