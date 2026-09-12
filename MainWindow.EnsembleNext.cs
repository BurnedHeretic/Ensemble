using Ensemble.Controls;
using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.Numerics;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ensemble
{
    /// <summary>
    /// Extension layer for Ensemble's SC2 editing, unified Object Browser,
    /// cross-map placement and Halo Wars visual presentation.
    /// </summary>
    public partial class MainWindow
    {
        private bool
            _ensembleNextInitialized;

        private MapViewport3D?
            _ensemble3DViewport;

        private bool
            _ensembleObjectsVisible =
                true;

        private Grid?
            _eraInfoLayoutGrid;

        private FrameworkElement?
            _eraInfoPanel;

        private GridSplitter?
            _eraInfoSplitter;

        private ColumnDefinition?
            _eraInfoSplitterColumn;

        private ColumnDefinition?
            _eraInfoPanelColumn;

        private GridLength
            _eraInfoSavedSplitterWidth =
                new GridLength(5);

        private GridLength
            _eraInfoSavedPanelWidth =
                new GridLength(330);

        static MainWindow()
        {
            EventManager.RegisterClassHandler(
                typeof(
                    MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    EnsembleNext_Loaded),
                true);

            EventManager.RegisterClassHandler(
                typeof(
                    MainWindow),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(
                    EnsembleNext_PreviewKeyDown),
                true);
        }

        private static void EnsembleNext_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                is not MainWindow window)
            {
                return;
            }

            window.InitializeEnsembleNext();
        }

        private static void EnsembleNext_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (sender
                is not MainWindow window)
            {
                return;
            }

            // Existing Edit > Duplicate now handles both SCN and SC2.
            if (Keyboard.Modifiers ==
                    ModifierKeys.Control
                &&
                e.Key ==
                    Key.D
                &&
                window._selectedScenarioItem
                is ScenarioArtObject)
            {
                window
                    .DuplicateSelectedArtObjectNext();

                e.Handled =
                    true;

                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control
                &&
                (
                    e.Key ==
                        Key.Z
                    ||
                    e.Key ==
                        Key.Y
                ))
            {
                window.Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                            window.Refresh3DViewportNext(
                                true)));
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control
                &&
                e.Key ==
                    Key.D0)
            {
                window._ensemble3DViewport?
                    .FitMap();
            }

            // Insert is now the unified Object Browser / Add Object command.
            if (Keyboard.Modifiers ==
                    ModifierKeys.None
                &&
                e.Key ==
                    Key.Insert)
            {
                window
                    .OpenObjectBrowserNext();

                e.Handled =
                    true;
            }
        }

        private void InitializeEnsembleNext()
        {
            if (_ensembleNextInitialized)
            {
                return;
            }

            _ensembleNextInitialized =
                true;

            HaloWarsThemeService.Apply(
                this);

            Initialize3DViewportNext();

            ScenarioMapCanvas.SelectionChanged +=
                EnsembleNext_SelectionChanged;

            // ---------------------------------------------------------
            // MERGE ADD OBJECT + CROSS-MAP OBJECT IMPORT
            // ---------------------------------------------------------
            //
            // From the user's perspective these are the same action:
            // choose an object, then place it on the current map.
            //
            // Reuse the existing Edit menu slot rather than exposing two
            // separate workflows.

            AddObjectMenuItem.Click -=
                AddObject_Click;

            AddObjectMenuItem.Header =
                "_Object Browser...";

            AddObjectMenuItem.InputGestureText =
                "Insert";

            AddObjectMenuItem.Click +=
                (
                    _,
                    _) =>
                    OpenObjectBrowserNext();

            // Existing Edit > Duplicate remains the single Duplicate command.
            // MainWindow's normal handler covers SCN.  This extension adds SC2.
            DuplicateObjectMenuItem.Click +=
                EnsembleNext_DuplicateMenuClick;

            ScenarioMapCanvas.ItemMoved +=
                (
                    _,
                    _) =>
                    Refresh3DViewportNext(
                        false);

            ScenarioMapCanvas.ItemRotated +=
                (
                    _,
                    _) =>
                    Refresh3DViewportNext(
                        false);

            ScenarioMapCanvas.ObjectAdded +=
                (
                    _,
                    _) =>
                    Refresh3DViewportNext(
                        false);

            ScenarioMapCanvas.ObjectDeleted +=
                (
                    _,
                    _) =>
                    Refresh3DViewportNext(
                        false);

            ScenarioMapCanvas.TerrainPreviewChanged +=
                (
                    _,
                    _) =>
                    Refresh3DTerrainGeometryNext();

            ScenarioMapCanvas.IsVisibleChanged +=
                (
                    _,
                    _) =>
                    Update3DViewportVisibilityNext();

            UndoMenuItem.Click +=
                (
                    _,
                    _) =>
                    Dispatcher.BeginInvoke(
                        new Action(
                            () =>
                                Refresh3DViewportNext(
                                    true)));

            RedoMenuItem.Click +=
                (
                    _,
                    _) =>
                    Dispatcher.BeginInvoke(
                        new Action(
                            () =>
                                Refresh3DViewportNext(
                                    true)));

            // ---------------------------------------------------------
            // 3D VIEWPORT LIVE-SYNC
            // ---------------------------------------------------------
            //
            // The original menu handlers update the proven 2D/editor data.
            // These follow-up handlers mirror the resulting state into the
            // 3D presentation layer immediately.

            RemoveAllVegetationMenuItem.Click +=
                (
                    _,
                    _) =>
                    Dispatcher.BeginInvoke(
                        new Action(
                            () =>
                                Refresh3DViewportNext(
                                    false)));

            FlattenTerrainMenuItem.Click +=
                (
                    _,
                    _) =>
                    Dispatcher.BeginInvoke(
                        new Action(
                            Refresh3DTerrainGeometryNext));

            ImportTerrainTextureMenuItem.Click +=
                (
                    _,
                    _) =>
                    Dispatcher.BeginInvoke(
                        new Action(
                            () =>
                                Refresh3DViewportNext(
                                    true)));

            TerrainTextureMenuItem.Click +=
                (
                    _,
                    _) =>
                    _ensemble3DViewport?
                        .SetTerrainDisplayMode(
                            Ensemble.Controls
                                .TerrainDisplayMode.Texture);

            TerrainHeightMapMenuItem.Click +=
                (
                    _,
                    _) =>
                    _ensemble3DViewport?
                        .SetTerrainDisplayMode(
                            Ensemble.Controls
                                .TerrainDisplayMode.HeightMap);

            TerrainHiddenMenuItem.Click +=
                (
                    _,
                    _) =>
                    _ensemble3DViewport?
                        .SetTerrainDisplayMode(
                            Ensemble.Controls
                                .TerrainDisplayMode.Hidden);

            TerrainGridMenuItem.Click +=
                (
                    _,
                    _) =>
                {
                    if (_ensemble3DViewport !=
                        null)
                    {
                        _ensemble3DViewport.ShowGrid =
                            TerrainGridMenuItem.IsChecked;
                    }
                };

            AddObjectsMenu();
        }

        private void EnsembleNext_SelectionChanged(
            object? sender,
            ScenarioSelectionChangedEventArgs e)
        {
            NormalizeLegacyScenarioObjectIdCounter();

            if (e.SelectedItem
                is ScenarioArtObject)
            {
                DuplicateObjectMenuItem.IsEnabled =
                    true;
            }

            _ensemble3DViewport?
                .SelectItem(
                    e.SelectedItem);
        }

        private void EnsembleNext_DuplicateMenuClick(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedScenarioItem
                is ScenarioArtObject)
            {
                DuplicateSelectedArtObjectNext();
            }
        }

        private void AddObjectsMenu()
        {
            Menu? menu =
                FindVisualDescendant<Menu>(
                    this);

            if (menu ==
                null)
            {
                return;
            }

            InitializeEraInfoMenuNext(
                menu);

            InitializeGameAssetsMenuNext(
                menu);

            MenuItem objectsMenu =
                new MenuItem
                {
                    Header =
                        "_Objects"
                };

            ItemsControl? oldParent =
                ItemsControl
                    .ItemsControlFromItemContainer(
                        AddObjectMenuItem);

            oldParent?
                .Items
                .Remove(
                    AddObjectMenuItem);

            AddObjectMenuItem.Header =
                "_Add Object...";

            AddObjectMenuItem.InputGestureText =
                "Insert";

            objectsMenu.Items.Add(
                AddObjectMenuItem);

            MenuItem importCustomMesh =
                new MenuItem
                {
                    Header =
                        "_Import Custom Mesh..."
                };

            importCustomMesh.Click +=
                (
                    _,
                    _) =>
                    ImportCustomMeshNext();

            objectsMenu.Items.Add(
                importCustomMesh);

            objectsMenu.Items.Add(
                new Separator());

            MenuItem showAll =
                new MenuItem
                {
                    Header =
                        "_Show All Objects"
                };

            showAll.Click +=
                (
                    _,
                    _) =>
                    SetAllObjectsVisibleNext(
                        true);

            objectsMenu.Items.Add(
                showAll);

            MenuItem hideAll =
                new MenuItem
                {
                    Header =
                        "_Hide All Objects"
                };

            hideAll.Click +=
                (
                    _,
                    _) =>
                    SetAllObjectsVisibleNext(
                        false);

            objectsMenu.Items.Add(
                hideAll);

            MenuItem? helpMenu =
                FindTopLevelMenuItem(
                    menu,
                    "Help");

            int helpIndex =
                helpMenu != null
                    ? menu.Items.IndexOf(
                        helpMenu)
                    : -1;

            if (helpIndex >= 0)
            {
                menu.Items.Insert(
                    helpIndex,
                    objectsMenu);
            }
            else
            {
                menu.Items.Add(
                    objectsMenu);
            }

            AddHelpCommands(
                helpMenu);
        }

        private void InitializeEraInfoMenuNext(
            Menu menu)
        {
            MenuItem? viewMenu =
                FindTopLevelMenuItem(
                    menu,
                    "View");

            if (viewMenu ==
                null)
            {
                return;
            }

            LocateEraInfoPanelNext();

            if (viewMenu.Items.Count >
                0)
            {
                viewMenu.Items.Add(
                    new Separator());
            }

            MenuItem showEraInfo =
                new MenuItem
                {
                    Header =
                        "Show _ERA Info",

                    IsCheckable =
                        true,

                    IsChecked =
                        false
                };

            showEraInfo.Checked +=
                (
                    _,
                    _) =>
                    SetEraInfoVisibleNext(
                        true);

            showEraInfo.Unchecked +=
                (
                    _,
                    _) =>
                    SetEraInfoVisibleNext(
                        false);

            viewMenu.Items.Add(
                showEraInfo);

            SetEraInfoVisibleNext(
                false);
        }

        private void LocateEraInfoPanelNext()
        {
            if (_eraInfoPanel !=
                null)
            {
                return;
            }

            DependencyObject? current =
                RightPanelTitle;

            while (current !=
                null)
            {
                DependencyObject? parent =
                    VisualTreeHelper.GetParent(
                        current);

                if (current
                        is FrameworkElement element &&
                    parent
                        is Grid grid &&
                    Grid.GetColumn(
                        element) ==
                        4)
                {
                    _eraInfoPanel =
                        element;

                    _eraInfoLayoutGrid =
                        grid;

                    if (grid.ColumnDefinitions.Count >
                        4)
                    {
                        _eraInfoSplitterColumn =
                            grid.ColumnDefinitions[
                                3];

                        _eraInfoPanelColumn =
                            grid.ColumnDefinitions[
                                4];

                        _eraInfoSavedSplitterWidth =
                            _eraInfoSplitterColumn.Width;

                        _eraInfoSavedPanelWidth =
                            _eraInfoPanelColumn.Width;
                    }

                    _eraInfoSplitter =
                        grid.Children
                            .OfType<GridSplitter>()
                            .FirstOrDefault(
                                splitter =>
                                    Grid.GetColumn(
                                        splitter) ==
                                    3);

                    return;
                }

                current =
                    parent;
            }
        }

        private void SetEraInfoVisibleNext(
            bool visible)
        {
            LocateEraInfoPanelNext();

            if (_eraInfoPanel ==
                null)
            {
                return;
            }

            _eraInfoPanel.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (_eraInfoSplitter !=
                null)
            {
                _eraInfoSplitter.Visibility =
                    visible
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            if (_eraInfoPanelColumn !=
                null)
            {
                _eraInfoPanelColumn.Width =
                    visible
                        ? _eraInfoSavedPanelWidth
                        : new GridLength(0);
            }

            if (_eraInfoSplitterColumn !=
                null)
            {
                _eraInfoSplitterColumn.Width =
                    visible
                        ? _eraInfoSavedSplitterWidth
                        : new GridLength(0);
            }
        }

        private void InitializeGameAssetsMenuNext(
            Menu menu)
        {
            MenuItem? toolsMenu =
                FindTopLevelMenuItem(
                    menu,
                    "Tools");

            if (toolsMenu ==
                null)
            {
                return;
            }

            if (toolsMenu.Items.Count >
                0)
            {
                toolsMenu.Items.Add(
                    new Separator());
            }

            MenuItem locateAssets =
                new MenuItem
                {
                    Header =
                        "_Locate Halo Wars Game Assets..."
                };

            locateAssets.Click +=
                (
                    _,
                    _) =>
                    LocateHaloWarsGameAssetsNext();

            toolsMenu.Items.Add(
                locateAssets);

            MenuItem rescanAssets =
                new MenuItem
                {
                    Header =
                        "_Rescan Halo Wars Game Assets"
                };

            rescanAssets.Click +=
                (
                    _,
                    _) =>
                {
                    string message =
                        HaloWarsAssetArchiveService
                            .Rescan(
                                _currentArchive);

                    Refresh3DViewportNext(
                        false);

                    StatusText.Text =
                        message;
                };

            toolsMenu.Items.Add(
                rescanAssets);
        }

        private void LocateHaloWarsGameAssetsNext()
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Locate Halo Wars root.era",

                    Filter =
                        "Halo Wars Root Archive (root.era)|root.era|" +
                        "Halo Wars ERA (*.era)|*.era|" +
                        "All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false
                };

            if (dialog.ShowDialog(
                    this) !=
                true)
            {
                return;
            }

            if (!HaloWarsAssetArchiveService
                    .ConfigureFromRootEra(
                        dialog.FileName,
                        out string message))
            {
                MessageBox.Show(
                    this,
                    message,
                    "Halo Wars Game Assets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                StatusText.Text =
                    message;

                return;
            }

            Refresh3DViewportNext(
                false);

            StatusText.Text =
                message;

            MessageBox.Show(
                this,
                message +
                "\n\nThe 3D viewport will now resolve models from the map ERA and Halo Wars' shared archives.",
                "Halo Wars Game Assets",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static MenuItem? FindTopLevelMenuItem(
            Menu menu,
            string headerText)
        {
            foreach (object item
                     in menu.Items)
            {
                if (item
                    is not MenuItem menuItem)
                {
                    continue;
                }

                string text =
                    menuItem.Header?
                        .ToString()
                        ?.Replace(
                            "_",
                            string.Empty)
                    ??
                    string.Empty;

                if (text.Equals(
                        headerText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return menuItem;
                }
            }

            return null;
        }

        private void SetAllObjectsVisibleNext(
            bool visible)
        {
            _ensembleObjectsVisible =
                visible;

            if (_ensemble3DViewport !=
                null)
            {
                _ensemble3DViewport.ShowObjects =
                    visible;
            }

            if (!visible)
            {
                ScenarioMapCanvas
                    .ClearSelection();

                _ensemble3DViewport?
                    .SelectItem(
                        null);

                StatusText.Text =
                    "All SCN and SC2 objects hidden.";
            }
            else
            {
                ScenarioMapCanvas
                    .SetArtObjects(
                        _currentArtObjects);

                StatusText.Text =
                    "Showing all SCN and SC2 objects.";
            }
        }

        private void AddHelpCommands(
            MenuItem? helpMenu)
        {
            if (helpMenu ==
                null)
            {
                return;
            }

            if (helpMenu.Items.Count >
                0)
            {
                helpMenu.Items.Add(
                    new Separator());
            }

            MenuItem source =
                new MenuItem
                {
                    Header =
                        "_GitHub Source..."
                };

            source.Click +=
                (
                    _,
                    _) =>
                    OpenGitHubSourceNext();

            helpMenu.Items.Add(
                source);

            MenuItem updates =
                new MenuItem
                {
                    Header =
                        "_Check for Updates..."
                };

            updates.Click +=
                async (
                    _,
                    _) =>
                    await CheckForUpdatesNext();

            helpMenu.Items.Add(
                updates);
        }

        private void ImportCustomMeshNext()
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Import Custom Mesh",

                    Filter =
                        "Supported Mesh Files|" +
                        "*.obj;*.fbx;*.gltf;*.glb;*.dae;*.3ds;*.gr2;*.ugx|" +
                        "Wavefront OBJ (*.obj)|*.obj|" +
                        "FBX (*.fbx)|*.fbx|" +
                        "glTF (*.gltf;*.glb)|*.gltf;*.glb|" +
                        "Halo Wars / Granny (*.ugx;*.gr2)|*.ugx;*.gr2|" +
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
                ImportedMeshEntry imported =
                    CustomMeshImportService
                        .Import(
                            dialog.FileName);

                StatusText.Text =
                    $"Imported custom mesh {imported.DisplayName} " +
                    "into Ensemble's local object library.";

                MessageBox.Show(
                    this,
                    "Custom mesh imported into Ensemble's local object library.\n\n" +
                    $"Mesh: {imported.DisplayName}\n" +
                    $"Format: {imported.Extension}\n\n" +
                    "It will now appear in Objects > Add Object under the " +
                    "Imported meshes filter.",
                    "Mesh Imported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                OpenObjectBrowserNext();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Mesh Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void OpenGitHubSourceNext()
        {
            Process.Start(
                new ProcessStartInfo(
                    UpdateCheckService.RepositoryUrl)
                {
                    UseShellExecute =
                        true
                });
        }

        private async Task CheckForUpdatesNext()
        {
            StatusText.Text =
                "Checking GitHub for Ensemble updates...";

            try
            {
                UpdateCheckResult result =
                    await UpdateCheckService
                        .CheckAsync();

                if (!result.ReleaseAvailable)
                {
                    MessageBox.Show(
                        this,
                        result.Message,
                        "Check for Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    StatusText.Text =
                        "No published Ensemble release found.";

                    return;
                }

                string message =
                    result.Message +
                    "\n\n" +
                    $"Current: {result.CurrentVersion}\n" +
                    $"Latest:  {result.LatestVersion}";

                if (result.UpdateAvailable)
                {
                    MessageBoxResult choice =
                        MessageBox.Show(
                            this,
                            message +
                            "\n\nOpen the release page?",
                            "Ensemble Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                    if (choice ==
                            MessageBoxResult.Yes
                        &&
                        !string.IsNullOrWhiteSpace(
                            result.ReleaseUrl))
                    {
                        Process.Start(
                            new ProcessStartInfo(
                                result.ReleaseUrl)
                            {
                                UseShellExecute =
                                    true
                            });
                    }
                }
                else
                {
                    MessageBox.Show(
                        this,
                        message,
                        "Check for Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                StatusText.Text =
                    result.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Ensemble could not contact GitHub.\n\n" +
                    ex.Message,
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                StatusText.Text =
                    "Update check failed.";
            }
        }

        private void Initialize3DViewportNext()
        {
            if (_ensemble3DViewport !=
                null)
            {
                return;
            }

            if (ScenarioMapCanvas.Parent
                is not Grid viewportGrid)
            {
                return;
            }

            _ensemble3DViewport =
                new MapViewport3D
                {
                    Visibility =
                        Visibility.Collapsed
                };

            Panel.SetZIndex(
                _ensemble3DViewport,
                100);

            viewportGrid.Children.Add(
                _ensemble3DViewport);

            _ensemble3DViewport.SelectionChanged +=
                (
                    sender,
                    e) =>
                {
                    ScenarioMapCanvas_SelectionChanged(
                        sender,
                        e);

                    EnsembleNext_SelectionChanged(
                        sender,
                        e);
                };

            _ensemble3DViewport.ItemMoved +=
                (
                    _,
                    e) =>
                {
                    ScenarioMapCanvas
                        .ApplyHistoryPosition(
                            e.Item,
                            e.NewPosition);

                    ScenarioMapCanvas_ItemMoved(
                        ScenarioMapCanvas,
                        e);

                    Refresh3DViewportNext(
                        false);
                };

            Refresh3DViewportNext(
                true);

            _ensemble3DViewport.ShowGrid =
                TerrainGridMenuItem.IsChecked;

            _ensemble3DViewport.SetTerrainDisplayMode(
                TerrainHeightMapMenuItem.IsChecked
                    ? Ensemble.Controls
                        .TerrainDisplayMode.HeightMap
                    : TerrainHiddenMenuItem.IsChecked
                        ? Ensemble.Controls
                            .TerrainDisplayMode.Hidden
                        : Ensemble.Controls
                            .TerrainDisplayMode.Texture);

            Update3DViewportVisibilityNext();
        }

        private void Refresh3DViewportNext(
            bool rebuildTerrain)
        {
            if (_ensemble3DViewport ==
                null)
            {
                return;
            }

            ScenarioMap? map =
                ScenarioMapCanvas.Scenario;

            if (map ==
                null)
            {
                _ensemble3DViewport.Visibility =
                    Visibility.Collapsed;

                return;
            }

            if (rebuildTerrain)
            {
                ImageSource? terrainTexture =
                    TerrainViewportTextureService
                        .TryBuild(
                            ScenarioMapCanvas);

                _ensemble3DViewport.SetScene(
                    map,
                    ScenarioMapCanvas.TerrainHeightMap,
                    _currentArtObjects,
                    terrainTexture,
                    _currentArchive);
            }
            else
            {
                _ensemble3DViewport.RefreshObjects(
                    _currentArtObjects);
            }

            _ensemble3DViewport.ShowObjects =
                _ensembleObjectsVisible;

            bool show =
                ScenarioMapCanvas.Visibility ==
                    Visibility.Visible;

            _ensemble3DViewport.Visibility =
                show
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void Refresh3DTerrainGeometryNext()
        {
            if (_ensemble3DViewport ==
                    null
                ||
                ScenarioMapCanvas.Scenario ==
                    null)
            {
                return;
            }

            _ensemble3DViewport.RefreshTerrain(
                ScenarioMapCanvas.TerrainHeightMap);
        }

        private void Update3DViewportVisibilityNext()
        {
            if (_ensemble3DViewport ==
                null)
            {
                return;
            }

            if (ScenarioMapCanvas.Visibility ==
                Visibility.Visible)
            {
                Refresh3DViewportNext(
                    true);
            }
            else
            {
                _ensemble3DViewport.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void DuplicateSelectedArtObjectNext()
        {
            if (_selectedScenarioItem
                    is not ScenarioArtObject source
                ||
                _currentArtObjectsChunk ==
                    null
                ||
                _currentArtObjectsOriginalSc2Data ==
                    null
                ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                return;
            }

            int newId =
                AllocateCrossLayerObjectId(
                    map);

            ScenarioArtObject duplicate =
                CloneArtObjectModel(
                    source,
                    newId,
                    source.Position +
                    new Vector3(
                        12,
                        0,
                        12));

            byte[]? beforePending =
                _pendingArtObjectsSc2Replacement?
                    .ToArray();

            byte[] sourceSc2 =
                beforePending
                ??
                _currentArtObjectsOriginalSc2Data;

            byte[] afterPending =
                XmbObjectTransferService
                    .CloneObjectSubtree(
                        sourceSc2,
                        sourceSc2,
                        source.Id,
                        newId);

            afterPending =
                XmbDocumentService
                    .WriteArtObjectTransforms(
                        afterPending,
                        new[]
                        {
                            duplicate
                        });

            int insertIndex =
                _currentArtObjects.Count;

            _currentArtObjects.Add(
                duplicate);

            _pendingArtObjectsSc2Replacement =
                afterPending;

            _artObjectsDirty =
                true;

            RefreshArtObjectLayer();

            Refresh3DViewportNext(
                false);

            PushEnsembleNextHistory(
                new ArtObjectStructureHistoryAction(
                    this,
                    duplicate,
                    insertIndex,
                    beforePending,
                    afterPending,
                    $"Duplicate {source.DisplayName}"));

            StatusText.Text =
                $"Duplicated SC2 ArtObject {source.DisplayName} | " +
                $"new ID {newId}";
        }

        private void OpenObjectBrowserNext()
        {
            NormalizeLegacyScenarioObjectIdCounter();

            if (_currentArchive ==
                    null
                ||
                _currentScenarioOriginalXmbData ==
                    null
                ||
                ScenarioMapCanvas.Scenario
                    ==
                    null)
            {
                MessageBox.Show(
                    this,
                    "Open a Halo Wars map ERA before adding objects.",
                    "Object Browser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ObjectBrowserWindow dialog =
                new ObjectBrowserWindow(
                    _currentArchive.FilePath)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog()
                !=
                true
                ||
                dialog.SelectedEntry ==
                    null)
            {
                return;
            }

            try
            {
                if (dialog.SelectedEntry.Layer ==
                    ObjectCatalogLayer.ArtObject)
                {
                    ImportForeignArtObject(
                        dialog.SelectedEntry,
                        dialog.PreserveDonorPosition);
                }
                else
                {
                    ImportForeignScenarioObject(
                        dialog.SelectedEntry,
                        dialog.PreserveDonorPosition);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Object Placement Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Object placement failed.";
            }
        }

        private void ImportForeignArtObject(
            ObjectCatalogEntry entry,
            bool preserveDonorPosition)
        {
            if (entry.ArtObject ==
                    null
                ||
                _currentArtObjectsChunk ==
                    null
                ||
                _currentArtObjectsOriginalSc2Data ==
                    null
                ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                throw new InvalidOperationException(
                    "The target map has no editable SC2 ArtObjects companion.");
            }

            ScenarioArtObject source =
                entry.ArtObject;

            int newId =
                AllocateCrossLayerObjectId(
                    map);

            Vector3 targetPosition =
                preserveDonorPosition
                    ? source.Position
                    : GetMapCentrePosition(
                        map,
                        source.Position.Y);

            ScenarioArtObject imported =
                CloneArtObjectModel(
                    source,
                    newId,
                    targetPosition);

            byte[]? beforePending =
                _pendingArtObjectsSc2Replacement?
                    .ToArray();

            byte[] targetSc2 =
                beforePending
                ??
                _currentArtObjectsOriginalSc2Data;

            byte[] afterPending =
                XmbObjectTransferService
                    .CloneObjectSubtree(
                        targetSc2,
                        entry.SourceXmbData,
                        source.Id,
                        newId);

            afterPending =
                XmbDocumentService
                    .WriteArtObjectTransforms(
                        afterPending,
                        new[]
                        {
                            imported
                        });

            int insertIndex =
                _currentArtObjects.Count;

            _currentArtObjects.Add(
                imported);

            _pendingArtObjectsSc2Replacement =
                afterPending;

            _artObjectsDirty =
                true;

            RefreshArtObjectLayer();

            Refresh3DViewportNext(
                false);

            HighlightPlacedObjectNext(
                imported);

            PushEnsembleNextHistory(
                new ArtObjectStructureHistoryAction(
                    this,
                    imported,
                    insertIndex,
                    beforePending,
                    afterPending,
                    $"Place {source.DisplayName}"));

            StatusText.Text =
                $"Placed SC2 ArtObject {source.DisplayName} " +
                $"from {System.IO.Path.GetFileName(entry.DonorEraPath)} | " +
                $"new ID {newId}";
        }

        private void ImportForeignScenarioObject(
            ObjectCatalogEntry entry,
            bool preserveDonorPosition)
        {
            if (entry.ScenarioObject ==
                    null
                ||
                _currentScenarioOriginalXmbData ==
                    null
                ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                throw new InvalidOperationException(
                    "No editable SCN scenario is loaded.");
            }

            ScenarioObject source =
                entry.ScenarioObject;

            int newId =
                AllocateCrossLayerObjectId(
                    map);

            Vector3 targetPosition =
                preserveDonorPosition
                    ? source.Position
                    : GetMapCentrePosition(
                        map,
                        source.Position.Y);

            byte[] beforeScenario =
                _currentScenarioOriginalXmbData
                    .ToArray();

            byte[] afterScenario =
                XmbObjectTransferService
                    .CloneObjectSubtree(
                        beforeScenario,
                        entry.SourceXmbData,
                        source.Id,
                        newId);

            ScenarioObject imported =
                CloneScenarioObjectModel(
                    source,
                    newId,
                    targetPosition);

            imported.IsNewObject =
                false;

            imported.SourceObjectId =
                newId;

            imported.OriginalEditorName =
                imported.EditorName +
                "\u200B";

            int insertIndex =
                map.Objects.Count;

            _currentScenarioOriginalXmbData =
                afterScenario;

            map.Objects.Add(
                imported);

            RefreshScenarioCanvas(
                map);

            Refresh3DViewportNext(
                false);

            HighlightPlacedObjectNext(
                imported);

            PushEnsembleNextHistory(
                new ScenarioImportHistoryAction(
                    this,
                    imported,
                    insertIndex,
                    beforeScenario,
                    afterScenario,
                    $"Place {imported.EditorName}"));

            StatusText.Text =
                $"Placed SCN object {imported.EditorName} " +
                $"from {System.IO.Path.GetFileName(entry.DonorEraPath)} | " +
                $"new ID {newId}";
        }

        private int AllocateCrossLayerObjectId(
            ScenarioMap map)
        {
            const int MaxSignedInt24 =
                0x007FFFFF;

            HashSet<int> usedIds =
                map.Objects
                    .Select(
                        obj =>
                            obj.Id)
                    .Concat(
                        _currentArtObjects
                            .Select(
                                obj =>
                                    obj.Id))
                    .Where(
                        id =>
                            id >
                                0
                            &&
                            id <=
                                MaxSignedInt24)
                    .ToHashSet();

            // ScenarioMap.Objects intentionally represents the objects the
            // editor understands. XMB files can also contain structural
            // objects that are not represented by that model. Those hidden
            // IDs caused Preserve Position imports to collide with IDs such
            // as 262 even though the visible object list considered them free.
            AddStructuralObjectIds(
                usedIds,
                _currentScenarioOriginalXmbData,
                MaxSignedInt24);

            AddStructuralObjectIds(
                usedIds,
                _pendingArtObjectsSc2Replacement
                ??
                _currentArtObjectsOriginalSc2Data,
                MaxSignedInt24);

            int highestUsed =
                usedIds.Count ==
                    0
                    ? 0
                    : usedIds.Max();

            int candidate =
                highestUsed +
                1;

            if (candidate <=
                    MaxSignedInt24
                &&
                !usedIds.Contains(
                    candidate))
            {
                map.MaxKnownId =
                    candidate;

                return candidate;
            }

            for (candidate = 1;
                 candidate <=
                    MaxSignedInt24;
                 candidate++)
            {
                if (!usedIds.Contains(
                        candidate))
                {
                    map.MaxKnownId =
                        candidate;

                    return candidate;
                }
            }

            throw new InvalidDataException(
                "No free Halo Wars Object ID remains inside the signed Int24 range.");
        }

        private static void AddStructuralObjectIds(
            HashSet<int> usedIds,
            byte[]? xmbData,
            int maximumId)
        {
            if (xmbData ==
                null)
            {
                return;
            }

            try
            {
                foreach (int id
                         in XmbObjectTransferService
                             .ReadObjectIds(
                                 xmbData))
                {
                    if (id >
                            0
                        &&
                        id <=
                            maximumId)
                    {
                        usedIds.Add(
                            id);
                    }
                }
            }
            catch
            {
                // The parsed ScenarioMap IDs are still a safe fallback if a
                // malformed optional companion XMB cannot be enumerated.
            }
        }

        private void HighlightPlacedObjectNext(
            object item)
        {
            _selectedScenarioItem =
                item;

            ScenarioSelectionChangedEventArgs args =
                new ScenarioSelectionChangedEventArgs(
                    item);

            ScenarioMapCanvas_SelectionChanged(
                ScenarioMapCanvas,
                args);

            EnsembleNext_SelectionChanged(
                ScenarioMapCanvas,
                args);

            _ensemble3DViewport?
                .FocusItem(
                    item);
        }

        private void NormalizeLegacyScenarioObjectIdCounter()
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            const int MaxSignedInt24 =
                0x007FFFFF;

            map.MaxKnownId =
                map.Objects
                    .Select(obj => obj.Id)
                    .Concat(
                        _currentArtObjects
                            .Select(obj => obj.Id))
                    .Where(
                        id =>
                            id > 0 &&
                            id <= MaxSignedInt24)
                    .DefaultIfEmpty(0)
                    .Max();
        }

        private static Vector3 GetMapCentrePosition(
            ScenarioMap map,
            float y)
        {
            return
                new Vector3(
                    (
                        map.MinX +
                        map.MaxX
                    )
                    *
                    0.5f,
                    y,
                    (
                        map.MinZ +
                        map.MaxZ
                    )
                    *
                    0.5f);
        }

        private static ScenarioArtObject CloneArtObjectModel(
            ScenarioArtObject source,
            int newId,
            Vector3 position)
        {
            ScenarioArtObject result =
                new ScenarioArtObject
                {
                    Id =
                        newId,

                    SourceObjectId =
                        newId,

                    EditorName =
                        source.EditorName,

                    OriginalEditorName =
                        source.EditorName,

                    Type =
                        source.Type,

                    Position =
                        position,

                    Forward =
                        source.Forward,

                    Right =
                        source.Right,

                    Group =
                        source.Group,

                    VisualVariationIndex =
                        source.VisualVariationIndex
                };

            foreach (string flag
                     in source.Flags)
            {
                result.Flags.Add(
                    flag);
            }

            return result;
        }

        private static ScenarioObject CloneScenarioObjectModel(
            ScenarioObject source,
            int newId,
            Vector3 position)
        {
            ScenarioObject result =
                new ScenarioObject
                {
                    Id =
                        newId,

                    SourceObjectId =
                        newId,

                    IsNewObject =
                        false,

                    IsSquad =
                        source.IsSquad,

                    Player =
                        source.Player,

                    TintValue =
                        source.TintValue,

                    EditorName =
                        source.EditorName,

                    OriginalEditorName =
                        source.EditorName,

                    Type =
                        source.Type,

                    Position =
                        position,

                    Forward =
                        source.Forward,

                    Right =
                        source.Right,

                    Group =
                        source.Group,

                    VisualVariationIndex =
                        source.VisualVariationIndex
                };

            foreach (string flag
                     in source.Flags)
            {
                result.Flags.Add(
                    flag);
            }

            return result;
        }

        private void RefreshArtObjectLayer()
        {
            ScenarioMapCanvas
                .SetArtObjects(
                    _ensembleObjectsVisible
                        ? _currentArtObjects
                        : Array.Empty<
                            ScenarioArtObject>());
        }

        private void RefreshScenarioCanvas(
            ScenarioMap map)
        {
            ScenarioMapCanvas.SetMap(
                map);

            RefreshArtObjectLayer();
        }

        private void PushEnsembleNextHistory(
            IScenarioHistoryAction action)
        {
            _undoStack.Push(
                action);

            _redoStack.Clear();

            _currentRevisionId =
                action.AfterRevisionId;

            UpdateUndoRedoUi();

            UpdateDirtyState();
        }

        private void RestoreArtStructureStateNext(
            ScenarioArtObject artObject,
            int index,
            byte[]? pending,
            bool shouldExist)
        {
            if (shouldExist)
            {
                if (!_currentArtObjects.Contains(
                        artObject))
                {
                    _currentArtObjects.Insert(
                        Math.Clamp(
                            index,
                            0,
                            _currentArtObjects.Count),
                        artObject);
                }
            }
            else
            {
                _currentArtObjects.Remove(
                    artObject);
            }

            _pendingArtObjectsSc2Replacement =
                pending?
                    .ToArray();

            _artObjectsDirty =
                _pendingArtObjectsSc2Replacement !=
                null;

            RefreshArtObjectLayer();
        }

        private void RestoreArtPropertiesStateNext(
            ScenarioArtObject artObject,
            string name,
            int group,
            int variation,
            byte[]? pending)
        {
            artObject.EditorName =
                name;

            artObject.Group =
                group;

            artObject.VisualVariationIndex =
                variation;

            _pendingArtObjectsSc2Replacement =
                pending?
                    .ToArray();

            _artObjectsDirty =
                _pendingArtObjectsSc2Replacement !=
                null;

            RefreshArtObjectLayer();
        }

        private void RestoreScenarioImportStateNext(
            ScenarioObject scenarioObject,
            int index,
            byte[] scenarioBaseline,
            bool shouldExist)
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            _currentScenarioOriginalXmbData =
                scenarioBaseline
                    .ToArray();

            if (shouldExist)
            {
                if (!map.Objects.Contains(
                        scenarioObject))
                {
                    map.Objects.Insert(
                        Math.Clamp(
                            index,
                            0,
                            map.Objects.Count),
                        scenarioObject);
                }
            }
            else
            {
                map.Objects.Remove(
                    scenarioObject);
            }

            RefreshScenarioCanvas(
                map);
        }

        private sealed class ArtObjectStructureHistoryAction :
            IScenarioHistoryAction
        {
            private readonly MainWindow
                _owner;

            private readonly ScenarioArtObject
                _artObject;

            private readonly int
                _index;

            private readonly byte[]?
                _before;

            private readonly byte[]
                _after;

            public ArtObjectStructureHistoryAction(
                MainWindow owner,
                ScenarioArtObject artObject,
                int index,
                byte[]? before,
                byte[] after,
                string description)
            {
                _owner =
                    owner;

                _artObject =
                    artObject;

                _index =
                    index;

                _before =
                    before?
                        .ToArray();

                _after =
                    after.ToArray();

                Description =
                    description;

                BeforeRevisionId =
                    owner._currentRevisionId;

                AfterRevisionId =
                    ++owner._nextRevisionId;
            }

            public string Description
            {
                get;
            }

            public long BeforeRevisionId
            {
                get;
            }

            public long AfterRevisionId
            {
                get;
            }

            public void Undo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreArtStructureStateNext(
                        _artObject,
                        _index,
                        _before,
                        false);
            }

            public void Redo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreArtStructureStateNext(
                        _artObject,
                        _index,
                        _after,
                        true);
            }
        }

        private sealed class ArtObjectPropertyHistoryAction :
            IScenarioHistoryAction
        {
            private readonly MainWindow
                _owner;

            private readonly ScenarioArtObject
                _artObject;

            private readonly string
                _oldName;

            private readonly int
                _oldGroup;

            private readonly int
                _oldVariation;

            private readonly string
                _newName;

            private readonly int
                _newGroup;

            private readonly int
                _newVariation;

            private readonly byte[]?
                _before;

            private readonly byte[]
                _after;

            public ArtObjectPropertyHistoryAction(
                MainWindow owner,
                ScenarioArtObject artObject,
                string oldName,
                int oldGroup,
                int oldVariation,
                string newName,
                int newGroup,
                int newVariation,
                byte[]? before,
                byte[] after)
            {
                _owner =
                    owner;

                _artObject =
                    artObject;

                _oldName =
                    oldName;

                _oldGroup =
                    oldGroup;

                _oldVariation =
                    oldVariation;

                _newName =
                    newName;

                _newGroup =
                    newGroup;

                _newVariation =
                    newVariation;

                _before =
                    before?
                        .ToArray();

                _after =
                    after.ToArray();

                Description =
                    $"Edit {artObject.DisplayName} properties";

                BeforeRevisionId =
                    owner._currentRevisionId;

                AfterRevisionId =
                    ++owner._nextRevisionId;
            }

            public string Description
            {
                get;
            }

            public long BeforeRevisionId
            {
                get;
            }

            public long AfterRevisionId
            {
                get;
            }

            public void Undo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreArtPropertiesStateNext(
                        _artObject,
                        _oldName,
                        _oldGroup,
                        _oldVariation,
                        _before);
            }

            public void Redo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreArtPropertiesStateNext(
                        _artObject,
                        _newName,
                        _newGroup,
                        _newVariation,
                        _after);
            }
        }

        private sealed class ScenarioImportHistoryAction :
            IScenarioHistoryAction
        {
            private readonly MainWindow
                _owner;

            private readonly ScenarioObject
                _scenarioObject;

            private readonly int
                _index;

            private readonly byte[]
                _before;

            private readonly byte[]
                _after;

            public ScenarioImportHistoryAction(
                MainWindow owner,
                ScenarioObject scenarioObject,
                int index,
                byte[] before,
                byte[] after,
                string description)
            {
                _owner =
                    owner;

                _scenarioObject =
                    scenarioObject;

                _index =
                    index;

                _before =
                    before.ToArray();

                _after =
                    after.ToArray();

                Description =
                    description;

                BeforeRevisionId =
                    owner._currentRevisionId;

                AfterRevisionId =
                    ++owner._nextRevisionId;
            }

            public string Description
            {
                get;
            }

            public long BeforeRevisionId
            {
                get;
            }

            public long AfterRevisionId
            {
                get;
            }

            public void Undo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreScenarioImportStateNext(
                        _scenarioObject,
                        _index,
                        _before,
                        false);
            }

            public void Redo(
                MapCanvas canvas)
            {
                _owner
                    .RestoreScenarioImportStateNext(
                        _scenarioObject,
                        _index,
                        _after,
                        true);
            }
        }

        private static T? FindVisualDescendant<T>(
            DependencyObject root)
            where T :
                DependencyObject
        {
            int count =
                VisualTreeHelper.GetChildrenCount(
                    root);

            for (int i = 0;
                 i < count;
                 i++)
            {
                DependencyObject child =
                    VisualTreeHelper.GetChild(
                        root,
                        i);

                if (child
                    is T match)
                {
                    return match;
                }

                T? nested =
                    FindVisualDescendant<T>(
                        child);

                if (nested !=
                    null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
