using Ensemble.Models;
using Ensemble.Services;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    public partial class MapMetadataWindow :
        Window
    {
        private readonly MapMetadata
            _sourceMetadata;

        public MapMetadata? Metadata
        {
            get;
            private set;
        }

        public MapMetadataWindow(
            string eraFileName,
            MapMetadata metadata)
        {
            InitializeComponent();

            _sourceMetadata =
                metadata ??
                throw new ArgumentNullException(
                    nameof(metadata));

            InternalEraText.Text =
                eraFileName;

            DisplayNameTextBox.Text =
                metadata.DisplayName;

            DescriptionTextBox.Text =
                metadata.Description;

            Loaded +=
                MapMetadataWindow_Loaded;
        }

        private void MapMetadataWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            HaloWarsThemeService.Apply(
                this);

            int playerCount =
                _sourceMetadata.PlayerCount;

            if (playerCount is not
                (2 or 4 or 6))
            {
                MainWindow? mainWindow =
                    FindMainWindowV33();

                playerCount =
                    mainWindow?.GetPlayerCountV33() ??
                    2;
            }

            SelectPlayerCount(
                playerCount);

            DisplayNameTextBox.Focus();
            DisplayNameTextBox.SelectAll();
        }

        private void SelectPlayerCount(
            int playerCount)
        {
            foreach (object item
                     in PlayerCountComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    int.TryParse(
                        comboItem.Tag?.ToString(),
                        out int value) &&
                    value == playerCount)
                {
                    PlayerCountComboBox.SelectedItem =
                        comboItem;

                    return;
                }
            }

            PlayerCountComboBox.SelectedIndex =
                0;
        }

        private int ReadPlayerCount()
        {
            if (PlayerCountComboBox.SelectedItem
                    is ComboBoxItem item &&
                int.TryParse(
                    item.Tag?.ToString(),
                    out int value) &&
                value is 2 or 4 or 6)
            {
                return value;
            }

            throw new InvalidOperationException(
                "Select a valid Halo Wars player count.");
        }

        private MainWindow? FindMainWindowV33()
        {
            if (Owner is MainWindow owner)
            {
                return owner;
            }

            return Application.Current
                .Windows
                .OfType<MainWindow>()
                .FirstOrDefault(
                    window =>
                        window.IsActive)
                ??
                Application.Current
                    .Windows
                    .OfType<MainWindow>()
                    .FirstOrDefault();
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string displayName =
                DisplayNameTextBox.Text
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                    displayName))
            {
                MessageBox.Show(
                    this,
                    "Enter a map display name.",
                    "Map Metadata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            int playerCount;

            try
            {
                playerCount =
                    ReadPlayerCount();

                MainWindow? mainWindow =
                    FindMainWindowV33();

                if (mainWindow !=
                        null &&
                    !mainWindow.IsPlayerCountSynchronizedV33(
                        playerCount))
                {
                    mainWindow.ApplyPlayerCountV33(
                        playerCount);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Player Count Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            Metadata =
                new MapMetadata
                {
                    FormatVersion =
                        Math.Max(
                            1,
                            _sourceMetadata.FormatVersion),

                    DisplayName =
                        displayName,

                    Description =
                        DescriptionTextBox.Text
                            .Trim(),

                    PlayerCount =
                        playerCount
                };

            DialogResult =
                true;
        }
    }
}
