using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ensemble
{
    public partial class ObjectBrowserWindow :
        Window
    {
        private sealed class PreviewCacheValue
        {
            public required ImageSource Image
            {
                get;
                init;
            }

            public required string Kind
            {
                get;
                init;
            }
        }

        private readonly List<ObjectCatalogEntry>
            _allEntries =
                new();

        private readonly Dictionary<string, EraArchiveInfo>
            _previewArchives =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PreviewCacheValue>
            _previewCache =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<ObjectCatalogEntry>
            _previewGenerationPending =
                new();

        private readonly string?
            _initialEraPath;

        private int
            _previewGenerationVersion;

        private int
            _catalogLoadVersion;

        private int
            _rawCatalogCount;

        private int
            _duplicateCount;

        private int
            _eraFileCount;

        private string
            _libraryStatusMessage =
                string.Empty;

        internal ObjectCatalogEntry? SelectedEntry
        {
            get;
            private set;
        }

        internal bool PreserveDonorPosition =>
            PreservePositionCheck.IsChecked ==
            true;

        private bool Prefer3DPreviews =>
            PreviewModeCombo.SelectedIndex !=
            1;

        public ObjectBrowserWindow(
            string? initialEraPath =
                null)
        {
            _initialEraPath =
                initialEraPath;

            InitializeComponent();

            Loaded +=
                async (
                    _,
                    _) =>
                {
                    HaloWarsThemeService.Apply(
                        this);

                    if (!string.IsNullOrWhiteSpace(
                            _initialEraPath))
                    {
                        await LoadGameLibraryAsync();
                    }
                };
        }

        private async Task LoadGameLibraryAsync()
        {
            if (string.IsNullOrWhiteSpace(
                    _initialEraPath))
            {
                return;
            }

            int version =
                ++_catalogLoadVersion;

            SetLibraryBusy(
                true,
                "SCANNING HALO WARS ERA LIBRARY...");

            try
            {
                GameObjectCatalogLoadResult result =
                    await Task.Run(
                        () =>
                            GameObjectCatalogService
                                .LoadGameLibrary(
                                    _initialEraPath));

                if (version !=
                    _catalogLoadVersion)
                {
                    return;
                }

                InvalidatePreviewWork(
                    clearImages:
                        true);

                _previewArchives.Clear();

                foreach (KeyValuePair<string, EraArchiveInfo> pair
                         in result.Archives)
                {
                    _previewArchives[
                        pair.Key] =
                            pair.Value;
                }

                _allEntries.Clear();
                _allEntries.AddRange(
                    result.Entries);

                _rawCatalogCount =
                    result.RawObjectCount;

                _duplicateCount =
                    result.DuplicateCount;

                _eraFileCount =
                    result.EraFileCount;

                DonorPathText.Text =
                    !string.IsNullOrWhiteSpace(
                        result.GameDirectory)
                        ? result.GameDirectory
                        : "Halo Wars game directory not detected — current ERA only";

                _libraryStatusMessage =
                    BuildLibraryStatus(
                        result.Warnings.Count);

                WarningText.Text =
                    _libraryStatusMessage;

                ApplyFilter();
            }
            catch (Exception ex)
            {
                if (version !=
                    _catalogLoadVersion)
                {
                    return;
                }

                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Unable to Build Object Library",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                WarningText.Text =
                    "Object library scan failed. You can still add an external ERA manually.";
            }
            finally
            {
                if (version ==
                    _catalogLoadVersion)
                {
                    SetLibraryBusy(
                        false,
                        string.Empty);
                }
            }
        }

        private async void BrowseEra_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Add External Halo Wars ERA to Object Library",

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

            await AddExternalEraAsync(
                dialog.FileName);
        }

        private async void RescanGame_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadGameLibraryAsync();
        }

        private void ManageCustomMeshes_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_initialEraPath) ||
                !File.Exists(_initialEraPath))
            {
                MessageBox.Show(
                    this,
                    "Open a map ERA before managing embedded custom meshes.",
                    "Embedded Custom Meshes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            CustomMeshManagerWindow dialog =
                new CustomMeshManagerWindow(_initialEraPath)
                {
                    Owner = this
                };

            dialog.ShowDialog();

            if (dialog.QueuedDeletionCount > 0)
            {
                if (Owner is MainWindow owner)
                {
                    owner.ApplyEmbeddedCustomMeshDeletionFromBrowser(
                        dialog.QueuedRecords);
                }
                else
                {
                    CustomMeshPendingAssetService.QueueDelete(
                        dialog.QueuedRecords);
                }

                _libraryStatusMessage =
                    $"{dialog.QueuedDeletionCount:N0} custom mesh deletion(s) queued. " +
                    "Close Object Browser and save the map (Ctrl+S) to apply them.";

                WarningText.Text =
                    _libraryStatusMessage;
            }
        }

        private async Task AddExternalEraAsync(
            string eraPath)
        {
            int version =
                ++_catalogLoadVersion;

            SetLibraryBusy(
                true,
                "ADDING EXTERNAL ERA...");

            try
            {
                ObjectCatalogLoadResult load =
                    await Task.Run(
                        () =>
                            CrossEraObjectCatalogService
                                .Load(
                                    eraPath,
                                    includeImportedMeshes:
                                        false));

                if (version !=
                    _catalogLoadVersion)
                {
                    return;
                }

                if (load.Archive !=
                    null)
                {
                    _previewArchives[
                        Path.GetFullPath(
                            eraPath)] =
                                load.Archive;
                }

                int beforeUnique =
                    _allEntries.Count;

                int addedRaw =
                    load.Entries.Count;

                List<ObjectCatalogEntry> combined =
                    _allEntries
                        .Concat(
                            load.Entries)
                        .ToList();

                List<ObjectCatalogEntry> deduplicated =
                    GameObjectCatalogService
                        .Deduplicate(
                            combined,
                            _initialEraPath);

                int duplicatesRemovedThisAdd =
                    Math.Max(
                        0,
                        combined.Count -
                        deduplicated.Count);

                _rawCatalogCount +=
                    addedRaw;

                _duplicateCount +=
                    duplicatesRemovedThisAdd;

                _eraFileCount++;

                _allEntries.Clear();
                _allEntries.AddRange(
                    deduplicated);

                InvalidatePreviewWork(
                    clearImages:
                        true);

                _libraryStatusMessage =
                    BuildLibraryStatus(
                        load.Warnings.Count) +
                    $"  Added {Path.GetFileName(eraPath)}.";

                WarningText.Text =
                    _libraryStatusMessage;

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Unable to Add External ERA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (version ==
                    _catalogLoadVersion)
                {
                    SetLibraryBusy(
                        false,
                        string.Empty);
                }
            }
        }

        private string BuildLibraryStatus(
            int warningCount)
        {
            string status =
                $"{_eraFileCount:N0} ERA file(s) scanned  •  " +
                $"{_allEntries.Count:N0} unique object type(s)  •  " +
                $"{_duplicateCount:N0} duplicate placement(s) removed";

            if (warningCount >
                0)
            {
                status +=
                    $"  •  {warningCount:N0} archive/XMB warning(s)";
            }

            return status;
        }

        private void SetLibraryBusy(
            bool busy,
            string status)
        {
            FilterText.IsEnabled =
                !busy;

            LayerCombo.IsEnabled =
                !busy;

            PreviewModeCombo.IsEnabled =
                !busy;

            AddEraButton.IsEnabled =
                !busy;

            RescanButton.IsEnabled =
                !busy;

            ManageCustomMeshesButton.IsEnabled =
                !busy;

            ObjectsList.IsEnabled =
                !busy;

            if (busy)
            {
                SelectedEntry =
                    null;

                ImportButton.IsEnabled =
                    false;

                CountText.Text =
                    "SCANNING...";

                WarningText.Text =
                    status;

                Cursor =
                    System.Windows.Input.Cursors.Wait;
            }
            else
            {
                Cursor =
                    null;
            }
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

        private void PreviewModeCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            InvalidatePreviewWork(
                clearImages:
                    true);

            QueueVisiblePreviews();
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
                $"{filtered.Count:N0} UNIQUE OBJECTS";

            SelectedEntry =
                null;

            ImportButton.IsEnabled =
                false;

            ShowSelected(
                null);

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    QueueVisiblePreviews));
        }

        private void ObjectListItem_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is ListBoxItem item &&
                item.DataContext
                    is ObjectCatalogEntry entry)
            {
                QueuePreview(
                    entry);
            }
        }

        private void QueueVisiblePreviews()
        {
            for (int i = 0;
                 i < ObjectsList.Items.Count;
                 i++)
            {
                if (ObjectsList
                        .ItemContainerGenerator
                        .ContainerFromIndex(
                            i)
                    is ListBoxItem item &&
                    item.DataContext
                        is ObjectCatalogEntry entry)
                {
                    QueuePreview(
                        entry);
                }
            }
        }

        private void QueuePreview(
            ObjectCatalogEntry entry)
        {
            if (entry.PreviewImage !=
                    null ||
                !_previewGenerationPending.Add(
                    entry))
            {
                return;
            }

            int generationVersion =
                _previewGenerationVersion;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    () =>
                    {
                        try
                        {
                            if (generationVersion !=
                                    _previewGenerationVersion ||
                                entry.PreviewImage !=
                                    null)
                            {
                                return;
                            }

                            string previewKey =
                                BuildPreviewKey(
                                    entry);

                            if (!_previewCache.TryGetValue(
                                    previewKey,
                                    out PreviewCacheValue? cached))
                            {
                                ImageSource? preview =
                                    null;

                                string kind =
                                    string.Empty;

                                if (Prefer3DPreviews)
                                {
                                    preview =
                                        ObjectPreview3DService
                                            .TryCreate(
                                                GetPreviewArchive(
                                                    entry),
                                                entry);

                                    if (preview !=
                                        null)
                                    {
                                        kind =
                                            "3D";
                                    }
                                }

                                if (preview ==
                                    null)
                                {
                                    preview =
                                        ObjectPreviewService
                                            .Create(
                                                entry.Layer,
                                                entry.Name,
                                                entry.Type);

                                    kind =
                                        "2D";
                                }

                                cached =
                                    new PreviewCacheValue
                                    {
                                        Image =
                                            preview,

                                        Kind =
                                            kind
                                    };

                                _previewCache[
                                    previewKey] =
                                        cached;
                            }

                            entry.PreviewKind =
                                cached.Kind;

                            entry.PreviewImage =
                                cached.Image;
                        }
                        finally
                        {
                            _previewGenerationPending.Remove(
                                entry);
                        }
                    }));
        }

        private EraArchiveInfo? GetPreviewArchive(
            ObjectCatalogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(
                    entry.DonorEraPath))
            {
                return null;
            }

            try
            {
                string full =
                    Path.GetFullPath(
                        entry.DonorEraPath);

                return _previewArchives.TryGetValue(
                    full,
                    out EraArchiveInfo? archive)
                    ? archive
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private string BuildPreviewKey(
            ObjectCatalogEntry entry)
        {
            string identity =
                string.IsNullOrWhiteSpace(
                    entry.Type)
                    ? entry.InternalName
                    : entry.Type;

            int variation =
                entry.ScenarioObject?
                    .VisualVariationIndex
                ??
                entry.ArtObject?
                    .VisualVariationIndex
                ??
                0;

            return
                $"{(Prefer3DPreviews ? "3D" : "2D")}|" +
                $"{entry.Layer}|" +
                $"{entry.DonorEraPath}|" +
                $"{identity.Trim()}|" +
                $"VAR={variation}";
        }

        private void InvalidatePreviewWork(
            bool clearImages)
        {
            _previewGenerationVersion++;
            _previewGenerationPending.Clear();
            _previewCache.Clear();

            if (!clearImages)
            {
                return;
            }

            foreach (ObjectCatalogEntry entry
                     in _allEntries)
            {
                entry.PreviewImage =
                    null;

                entry.PreviewKind =
                    string.Empty;
            }
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
                bool supported =
                    SelectedEntry.CanPlace;

                WarningText.Text =
                    supported
                        ? "OBJ/FBX geometry is ready for native Halo Wars UGX compilation. " +
                          "Ensemble will reserve an unused rigid SC2 visual slot, compile the model, " +
                          "and embed its map-local UGX when you save. Initial custom meshes inherit " +
                          "the stock slot's Halo Wars material."
                        : "This staged mesh format is not yet supported by the native UGX compiler. " +
                          "Export it as OBJ or FBX first.";
            }
            else
            {
                WarningText.Text =
                    _libraryStatusMessage;
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

            SelectedInternalNameText.Text =
                entry?.InternalNameText
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
                entry?.SourceDisplayText
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

            if (SelectedEntry.Layer ==
                ObjectCatalogLayer.ImportedMesh)
            {
                if (!SelectedEntry.CanPlace)
                {
                    MessageBox.Show(
                        this,
                        "Export the model as OBJ or FBX before compiling it to Halo Wars UGX.",
                        "Unsupported Custom Mesh",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                if (Owner is not MainWindow owner)
                {
                    throw new InvalidOperationException(
                        "The Object Browser has no owning map editor window.");
                }

                try
                {
                    ImportButton.IsEnabled = false;
                    ObjectsList.IsEnabled = false;
                    Cursor = System.Windows.Input.Cursors.Wait;
                    WarningText.Text =
                        "COMPILING CUSTOM MODEL TO HALO WARS UGX...";

                    owner.PlaceImportedMeshFromBrowser(
                        SelectedEntry,
                        _allEntries,
                        _previewArchives);

                    // The MainWindow placement method already cloned the SC2
                    // object and registered the generated UGX for the next
                    // save. Return false so OpenObjectBrowserNext does not
                    // route this ImportedMesh through the normal SCN path.
                    DialogResult = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.ToString(),
                        "Custom Mesh Placement Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    ImportButton.IsEnabled = true;
                    ObjectsList.IsEnabled = true;
                    Cursor = null;
                    WarningText.Text =
                        "Custom mesh compilation failed. Choose another model or review the error details.";
                }

                return;
            }

            ObjectPlacementContextService
                .SetPending(
                    SelectedEntry,
                    PreserveDonorPosition);

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
