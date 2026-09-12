using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace Ensemble
{
    public partial class ObjectBrowserWindow :
        Window
    {
        private readonly List<ObjectCatalogEntry>
            _allEntries =
                new();

        private readonly string?
            _initialEraPath;

        internal ObjectCatalogEntry? SelectedEntry
        {
            get;
            private set;
        }

        internal bool PreserveDonorPosition =>
            PreservePositionCheck.IsChecked ==
            true;

        public ObjectBrowserWindow(
            string? initialEraPath =
                null)
        {
            _initialEraPath =
                initialEraPath;

            InitializeComponent();

            Loaded +=
                (
                    _,
                    _) =>
                {
                    HaloWarsThemeService.Apply(
                        this);

                    if (!string.IsNullOrWhiteSpace(
                            _initialEraPath))
                    {
                        try
                        {
                            LoadEra(
                                _initialEraPath);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                this,
                                ex.ToString(),
                                "Unable to Read Initial ERA",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                };
        }

        private void BrowseEra_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Choose Halo Wars ERA",

                    Filter =
                        "Halo Wars ERA (*.era)|*.era|" +
                        "All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false
                };

            if (dialog.ShowDialog(
                    this)
                !=
                true)
            {
                return;
            }

            try
            {
                LoadEra(
                    dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Unable to Read Donor ERA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadEra(
            string eraPath)
        {
            DonorPathText.Text =
                eraPath;

            ObjectCatalogLoadResult result =
                CrossEraObjectCatalogService
                    .Load(
                        eraPath);

            _allEntries.Clear();

            _allEntries.AddRange(
                result.Entries);

            WarningText.Text =
                result.Warnings.Count ==
                    0
                    ? string.Empty
                    : $"{result.Warnings.Count} file(s) could not be catalogued from this ERA.";

            ApplyFilter();
        }

        private void FilterText_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void LayerCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string filter =
                FilterText.Text
                    ?.Trim()
                    .ToLowerInvariant()
                ??
                string.Empty;

            int layerIndex =
                LayerCombo.SelectedIndex;

            IEnumerable<ObjectCatalogEntry> query =
                _allEntries;

            if (layerIndex ==
                1)
            {
                query =
                    query.Where(
                        entry =>
                            entry.Layer ==
                            ObjectCatalogLayer
                                .Scenario);
            }
            else if (
                layerIndex ==
                2)
            {
                query =
                    query.Where(
                        entry =>
                            entry.Layer ==
                            ObjectCatalogLayer
                                .ArtObject);
            }
            else if (
                layerIndex ==
                3)
            {
                query =
                    query.Where(
                        entry =>
                            entry.Layer ==
                            ObjectCatalogLayer
                                .ImportedMesh);
            }

            if (!string.IsNullOrWhiteSpace(
                    filter))
            {
                query =
                    query.Where(
                        entry =>
                            entry.SearchText
                                .Contains(
                                    filter,
                                    StringComparison.Ordinal));
            }

            List<ObjectCatalogEntry> filtered =
                query.ToList();

            ObjectsList.ItemsSource =
                filtered;

            CountText.Text =
                $"{filtered.Count:N0} OBJECTS";

            SelectedEntry =
                null;

            ImportButton.IsEnabled =
                false;

            ShowSelected(
                null);
        }

        private void ObjectsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            SelectedEntry =
                ObjectsList.SelectedItem
                as ObjectCatalogEntry;

            ImportButton.IsEnabled =
                SelectedEntry?.CanPlace ==
                true;

            if (SelectedEntry?.Layer ==
                ObjectCatalogLayer.ImportedMesh)
            {
                WarningText.Text =
                    "This custom mesh is staged in Ensemble's local library. " +
                    "Raw mesh -> Halo Wars UGX conversion is not implemented yet, " +
                    "so it cannot be placed into an ERA in this build.";
            }

            ShowSelected(
                SelectedEntry);
        }

        private void ShowSelected(
            ObjectCatalogEntry? entry)
        {
            SelectedNameText.Text =
                entry?.Name
                ??
                "-";

            SelectedLayerText.Text =
                entry?.LayerName
                ??
                "-";

            SelectedTypeText.Text =
                entry?.Type
                ??
                "-";

            SelectedPositionText.Text =
                entry?.PositionText
                ??
                "-";

            SelectedSourceText.Text =
                entry?.SourceFileName
                ??
                "-";
        }

        private void Import_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (SelectedEntry ==
                null)
            {
                return;
            }

            DialogResult =
                true;
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;
        }
    }
}
