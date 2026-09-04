using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ensemble
{
    public partial class MainWindow : Window
    {
        private EraArchiveInfo? _currentArchive;

        private bool _isDirty;

        private string?
            _currentSavePath;

        private long
            _nextRevisionId;

        private long
            _currentRevisionId;

        private long
            _savedRevisionId;

        private object? _selectedScenarioItem;

        private ScenarioObject? _pendingAddTemplate;

        private readonly Stack<IScenarioHistoryAction>
            _undoStack = new();

        private readonly Stack<IScenarioHistoryAction>
            _redoStack = new();

        private byte[]?
            _currentScenarioOriginalXmbData;

        private EraChunkInfo?
            _currentScenarioChunk;

        private EraChunkInfo?
            _currentTerrainChunk;

        private byte[]?
            _currentTerrainOriginalXtdData;

        private EraChunkInfo?
            _currentSimulationChunk;

        private byte[]?
            _currentSimulationOriginalXsdData;

        private MapMetadata?
            _currentMapMetadata;

        private EraManifestFooterService.Manifest?
            _currentEraManifest;

        public MainWindow()
        {
            InitializeComponent();

            ScenarioMapCanvas.SelectionChanged +=
                ScenarioMapCanvas_SelectionChanged;

            ScenarioMapCanvas.ItemMoved +=
                ScenarioMapCanvas_ItemMoved;

            ScenarioMapCanvas.ItemRotated +=
                ScenarioMapCanvas_ItemRotated;

            ScenarioMapCanvas.SphereRadiusChanged +=
                ScenarioMapCanvas_SphereRadiusChanged;

            ScenarioMapCanvas.ObjectPropertiesChanged +=
                ScenarioMapCanvas_ObjectPropertiesChanged;

            ScenarioMapCanvas.ObjectAdded +=
                ScenarioMapCanvas_ObjectAdded;

            ScenarioMapCanvas.ObjectDeleted +=
                ScenarioMapCanvas_ObjectDeleted;

            ScenarioMapCanvas.PlacementRequested +=
                ScenarioMapCanvas_PlacementRequested;

            ScenarioMapCanvas.PathPointMoved +=
                ScenarioMapCanvas_PathPointMoved;

            ScenarioMapCanvas.TerrainPreviewChanged +=
                ScenarioMapCanvas_TerrainPreviewChanged;

            PreviewKeyDown +=
                MainWindow_PreviewKeyDown;

            Closing += MainWindow_Closing;

        }

        private void MainWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (!_isDirty)
                return;

            MessageBoxResult result =
                MessageBox.Show(
                    this,

                    "This map contains unsaved changes.\n\n" +
                    "Would you like to save them before closing?",

                    "Unsaved Changes",

                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

            if (result ==
                MessageBoxResult.Cancel)
            {
                e.Cancel =
                    true;

                return;
            }

            if (result ==
                MessageBoxResult.No)
            {
                return;
            }

            // Yes
            if (!SaveCurrentDocument())
            {
                // User cancelled Save As, or save failed.
                e.Cancel =
                    true;
            }
        }

        private void MainWindow_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (Keyboard.Modifiers ==
                ModifierKeys.Control &&
                (e.Key ==
                Key.D0 ||
                e.Key ==
                Key.NumPad0))
            {
                ScenarioMapCanvas.FitMapView();

                StatusText.Text =
                    "Map view reset.";

                e.Handled =
                    true;

                return;
            }

            if (e.Key ==
                Key.Escape &&
                ScenarioMapCanvas.IsObjectPlacementActive)
            {
                ScenarioMapCanvas
                    .CancelObjectPlacement();

                _pendingAddTemplate =
                    null;

                StatusText.Text =
                    "Object placement cancelled.";

                e.Handled =
                    true;

                return;
            }

            if (e.Key == Key.Escape &&
                ScenarioMapCanvas.IsTerrainSculptActive)
            {
                SetTerrainSculptMode(
                    Ensemble.Controls
                        .TerrainSculptMode.None);

                e.Handled =
                    true;

                return;
            }

            // Ctrl + Shift + S
            // Save As
            if (Keyboard.Modifiers ==
                    (ModifierKeys.Control |
                     ModifierKeys.Shift) &&
                e.Key ==
                    Key.S)
            {
                SaveCurrentDocumentAs();

                e.Handled =
                    true;

                return;
            }

            // Ctrl + S
            // Save
            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key ==
                    Key.S)
            {
                SaveCurrentDocument();

                e.Handled =
                    true;

                return;
            }

            //Add Object
            if (e.Key ==
                Key.Insert &&
                AddObjectMenuItem.IsEnabled &&
                Keyboard.FocusedElement
                is not TextBox)
            {
                OpenAddObjectDialog();

                e.Handled =
                    true;

                return;
            }

            //Duplicate Object
            if (Keyboard.Modifiers ==
                ModifierKeys.Control &&
                e.Key == 
                Key.D)
            {
                DuplicateSelectedObject();

                e.Handled =
                    true;

                return;
            }

            // Delete Object
            if (e.Key ==
                Key.Delete &&
                _selectedScenarioItem
                is ScenarioObject &&
                Keyboard.FocusedElement
                is not TextBox)
            {
                DeleteSelectedObject();

                e.Handled =
                    true;

                return;
            }

            if (Keyboard.Modifiers ==
                ModifierKeys.Control &&
                e.Key == Key.Z && ScenarioMapCanvas.IsTerrainSculptActive)
            {
                if (ScenarioMapCanvas
                    .UndoTerrainPreview())
                {
                    StatusText.Text =
                        "Undo terrain sculpt preview.";
                }
                else
                {
                    StatusText.Text =
                        "No terrain sculpt preview to undo.";
                }

                e.Handled =
                    true;

                return;
            }

            // Ctrl + Z
            // Undo
            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key ==
                    Key.Z)
            {
                UndoLastMove();

                e.Handled =
                    true;

                return;
            }

            if (Keyboard.Modifiers ==
                ModifierKeys.Control &&
                e.Key == Key.Y && ScenarioMapCanvas.IsTerrainSculptActive)
            {
                if (ScenarioMapCanvas
                    .RedoTerrainPreview())
                {
                    StatusText.Text =
                        "Redo terrain sculpt preview.";
                }
                else
                {
                    StatusText.Text =
                        "No terrain sculpt preview to redo.";
                }

                e.Handled =
                    true;

                return;
            }

            // Ctrl + Y
            // Redo
            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key ==
                    Key.Y)
            {
                RedoLastMove();

                e.Handled =
                    true;

                return;
            }
        }

        private void OpenEra_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Open Halo Wars ERA",

                    Filter =
                        "Halo Wars ERA (*.era)|*.era|" +
                        "All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false

                };

            bool? result =
                dialog.ShowDialog(this);

            if (result != true)
                return;

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
                    "Unable to Open ERA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Failed to open ERA.";
            }
        }

        private void LoadEra(string filePath)
        {
            StatusText.Text =
                "Decrypting and reading ERA...";

            _currentArchive =
                EraArchiveService.Open(
                    filePath);

            _currentEraManifest =
                EraManifestFooterService.TryRead(filePath);


            if (_currentEraManifest !=
                null)
            {
                _currentMapMetadata =
                    new MapMetadata
                    {
                        DisplayName =
                            _currentEraManifest.DisplayName,

                        Description =
                            _currentEraManifest.Description
                    };
            }
            else
            {
                // Backwards compatibility for maps made before
                // ERA-embedded metadata existed.
                _currentMapMetadata =
                    MapMetadataService.Load(
                        filePath);
            }

            ScenarioMapCanvas.CancelObjectPlacement();

            ScenarioMapCanvas
                .SetTerrainSculptMode(
                Ensemble.Controls
                .TerrainSculptMode.None);

            ScenarioMapCanvas
                .SetTerrainHeightMap(
                    null);

            _currentTerrainChunk =
                null;

            _currentTerrainOriginalXtdData =
                null;

            _currentSimulationChunk =
                null;

            _currentSimulationOriginalXsdData =
                null;

            _pendingAddTemplate =
                null;

            SaveMenuItem.IsEnabled =
                false;

            SaveAsMenuItem.IsEnabled =
                false;

            AddObjectMenuItem.IsEnabled =
                false;

            RegisterCustomMapMenuItem.IsEnabled =
                false;

            MapMetadataMenuItem.IsEnabled =
                false;

            _undoStack.Clear();

            _redoStack.Clear();

            _nextRevisionId =
                0;

            _currentRevisionId =
                0;

            _savedRevisionId =
                0;

            _currentSavePath =
                _currentArchive.FilePath;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            _currentScenarioOriginalXmbData =
                null;

            _currentScenarioChunk =
                null;

            ExportScenarioXmbMenuItem.IsEnabled =
                false;

            ExtractAllMenuItem.IsEnabled =
                true;

            FileNameText.Text =
                _currentArchive.FileName;

            FilePathText.Text =
                _currentArchive.FilePath;

            FileSizeText.Text =
                $"{FormatBytes(_currentArchive.FileSize)} " +
                $"({_currentArchive.FileSize:N0} bytes)";

            HeaderHexText.Text =
                _currentArchive
                    .DecryptedHeaderHex;

            HeaderAsciiText.Text =
                _currentArchive
                    .DecryptedHeaderAscii;

            BuildArchiveTree();

            WelcomePanel.Visibility =
                Visibility.Collapsed;

            StatusText.Text =
                $"Valid Halo Wars ERA | " +
                $"{_currentArchive.Chunks.Count - 1} files | " +
                $"{_currentArchive.Chunks.Count} chunks | " +
                $"{(_currentArchive.IsEncrypted ? "Encrypted" : "Unencrypted")}";
        }

        private void ExtractAll_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive == null)
                return;

            OpenFolderDialog dialog =
                new OpenFolderDialog
                {
                    Title =
                        "Choose ERA Extraction Folder",

                    Multiselect =
                        false
                };

            bool? result =
                dialog.ShowDialog(this);

            if (result != true)
                return;

            try
            {
                StatusText.Text =
                    "Extracting ERA...";

                int count =
                    EraExtractionService.ExtractAll(
                        _currentArchive,
                        dialog.FolderName);

                StatusText.Text =
                    $"Extracted {count} files.";

                MessageBox.Show(
                    this,
                    $"Successfully extracted {count} files.\n\n" +
                    dialog.FolderName,
                    "ERA Extraction Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "ERA Extraction Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "ERA extraction failed.";
            }
        }

        private void ExtractFile_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive == null)
                return;

            if (sender is not MenuItem menuItem)
                return;

            if (menuItem.Parent is not ContextMenu contextMenu)
                return;

            if (contextMenu.PlacementTarget
                is not TreeViewItem treeItem)
                return;

            if (treeItem.Tag
                is not EraChunkInfo taggedChunk)
            {
                return;
            }

            if (taggedChunk.Index < 0 ||
                taggedChunk.Index >=
                    _currentArchive.Chunks.Count)
            {
                return;
            }

            EraChunkInfo chunk =
                _currentArchive.Chunks[
                    taggedChunk.Index];

            string defaultName =
                System.IO.Path.GetFileName(
                    chunk.FileName);

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title =
                        "Extract ERA File",

                    FileName =
                        defaultName,

                    Filter =
                        "All Files (*.*)|*.*"
                };

            bool? result =
                dialog.ShowDialog(this);

            if (result != true)
                return;

            try
            {
                StatusText.Text =
                    $"Extracting {defaultName}...";

                EraExtractionService.ExtractFile(
                    _currentArchive,
                    chunk,
                    dialog.FileName);

                StatusText.Text =
                    $"Extracted {defaultName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "File Extraction Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "File extraction failed.";
            }
        }

        private void Undo_Click(
            object sender,
            RoutedEventArgs e)
        {
            UndoLastMove();
        }

        private void Redo_Click(
            object sender,
            RoutedEventArgs e)
        {
            RedoLastMove();
        }

        private void UndoLastMove()
        {
            if (_undoStack.Count == 0)
                return;

            IScenarioHistoryAction action =
                _undoStack.Pop();

            action.Undo(
                ScenarioMapCanvas);

            _redoStack.Push(
                action);

            _currentRevisionId =
                action.BeforeRevisionId;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Undo: {action.Description}";
        }

        private void RedoLastMove()
        {
            if (_redoStack.Count == 0)
                return;

            IScenarioHistoryAction action =
                _redoStack.Pop();

            action.Redo(
                ScenarioMapCanvas);

            _undoStack.Push(
                action);

            _currentRevisionId =
                action.AfterRevisionId;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Redo: {action.Description}";
        }

        private void UpdateUndoRedoUi()
        {
            bool canUndo =
                _undoStack.Count > 0;

            bool canRedo =
                _redoStack.Count > 0;

            UndoMenuItem.IsEnabled =
                canUndo;

            RedoMenuItem.IsEnabled =
                canRedo;

            UndoMenuItem.Header =
                canUndo
                    ? $"_Undo {_undoStack.Peek().Description}"
                    : "_Undo";

            RedoMenuItem.Header =
                canRedo
                    ? $"_Redo {_redoStack.Peek().Description}"
                    : "_Redo";
        }

        //-----------------------------------------------------
        // Scenario History Actions
        //-----------------------------------------------------

        private interface IScenarioHistoryAction
        {
            string Description
            {
                get;
            }

            long BeforeRevisionId
            {
                get;
            }

            long AfterRevisionId
            {
                get;
            }

            void Undo(
                Ensemble.Controls.MapCanvas canvas);

            void Redo(
                Ensemble.Controls.MapCanvas canvas);
        }

        private sealed class MoveHistoryAction :
            IScenarioHistoryAction
        {
            public MoveHistoryAction(
                object item,
                Vector3 oldPosition,
                Vector3 newPosition,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Item =
                    item;

                OldPosition =
                    oldPosition;

                NewPosition =
                    newPosition;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public long BeforeRevisionId
            {
                get;
            }

            public long AfterRevisionId
            {
                get;
            }

            public object Item
            {
                get;
            }

            public Vector3 OldPosition
            {
                get;
            }

            public Vector3 NewPosition
            {
                get;
            }

            public string Description =>
                $"Move {GetItemDisplayName(Item)}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryPosition(
                    Item,
                    OldPosition);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryPosition(
                    Item,
                    NewPosition);
            }
        }

        private sealed class RotationHistoryAction :
            IScenarioHistoryAction
        {
            public RotationHistoryAction(
                object item,
                Vector3 oldForward,
                Vector3 oldRight,
                Vector3 newForward,
                Vector3 newRight,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Item =
                    item;

                OldForward =
                    oldForward;

                OldRight =
                    oldRight;

                NewForward =
                    newForward;

                NewRight =
                    newRight;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public long BeforeRevisionId
            {
                get;
            }

            public long AfterRevisionId
            {
                get;
            }

            public object Item
            {
                get;
            }

            public Vector3 OldForward
            {
                get;
            }

            public Vector3 OldRight
            {
                get;
            }

            public Vector3 NewForward
            {
                get;
            }

            public Vector3 NewRight
            {
                get;
            }

            public string Description =>
                $"Rotate {GetItemDisplayName(Item)}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryOrientation(
                    Item,
                    OldForward,
                    OldRight);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryOrientation(
                    Item,
                    NewForward,
                    NewRight);
            }
        }

        private sealed class PathPointMoveHistoryAction :
            IScenarioHistoryAction
        {
            public PathPointMoveHistoryAction(
                ScenarioPath path,
                int pointIndex,
                Vector3 oldPosition,
                Vector3 newPosition,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Path =
                    path;

                PointIndex =
                    pointIndex;

                OldPosition =
                    oldPosition;

                NewPosition =
                    newPosition;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public ScenarioPath Path
            {
                get;
            }

            public int PointIndex
            {
                get;
            }

            public Vector3 OldPosition
            {
                get;
            }

            public Vector3 NewPosition
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

            public string Description =>
                $"Move {Path.Name} point {PointIndex + 1}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryPathPoint(
                    Path,
                    PointIndex,
                    OldPosition);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryPathPoint(
                    Path,
                    PointIndex,
                    NewPosition);
            }
        }

        private sealed class SphereRadiusHistoryAction :
            IScenarioHistoryAction
        {
            public SphereRadiusHistoryAction(
                ScenarioSphere sphere,
                float oldRadius,
                float newRadius,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Sphere =
                    sphere;

                OldRadius =
                    oldRadius;

                NewRadius =
                    newRadius;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public ScenarioSphere Sphere
            {
                get;
            }

            public float OldRadius
            {
                get;
            }

            public float NewRadius
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

            public string Description =>
                $"Resize {Sphere.Name}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistorySphereRadius(
                    Sphere,
                    OldRadius);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistorySphereRadius(
                    Sphere,
                    NewRadius);
            }
        }

        private sealed class ObjectPropertiesHistoryAction :
            IScenarioHistoryAction
        {
            public ObjectPropertiesHistoryAction(
                ScenarioObject obj,
                string oldEditorName,
                int oldPlayer,
                int oldGroup,
                int oldVariation,
                string newEditorName,
                int newPlayer,
                int newGroup,
                int newVariation,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Object =
                    obj;

                OldEditorName =
                    oldEditorName;

                OldPlayer =
                    oldPlayer;

                OldGroup =
                    oldGroup;

                OldVariation =
                    oldVariation;

                NewEditorName =
                    newEditorName;

                NewPlayer =
                    newPlayer;

                NewGroup =
                    newGroup;

                NewVariation =
                    newVariation;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public ScenarioObject Object { get; }

            public string OldEditorName { get; }

            public int OldPlayer { get; }

            public int OldGroup { get; }

            public int OldVariation { get; }

            public string NewEditorName { get; }

            public int NewPlayer { get; }

            public int NewGroup { get; }

            public int NewVariation { get; }

            public long BeforeRevisionId { get; }

            public long AfterRevisionId { get; }

            public string Description =>
                $"Edit {Object.EditorName} properties";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryObjectProperties(
                    Object,
                    OldEditorName,
                    OldPlayer,
                    OldGroup,
                    OldVariation);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryObjectProperties(
                    Object,
                    NewEditorName,
                    NewPlayer,
                    NewGroup,
                    NewVariation);
            }
        }

        private sealed class DeleteObjectHistoryAction :
            IScenarioHistoryAction
        {
            public DeleteObjectHistoryAction(
                ScenarioObject obj,
                bool wasNewObject,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Object =
                    obj;

                WasNewObject =
                    wasNewObject;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public ScenarioObject Object
            {
                get;
            }

            public bool WasNewObject
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

            public string Description =>
                $"Delete {Object.EditorName}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryRestoreObject(
                    Object,
                    WasNewObject);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryDeleteObject(
                    Object,
                    WasNewObject);
            }
        }

        private void ArchiveFile_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_currentArchive == null)
                return;

            if (sender is not TreeViewItem item)
                return;

            if (item.Tag is not EraChunkInfo taggedChunk)
                return;

            if (taggedChunk.Index < 0 ||
                taggedChunk.Index >=
                    _currentArchive.Chunks.Count)
            {
                return;
            }

            EraChunkInfo chunk =
                _currentArchive.Chunks[
                    taggedChunk.Index];

            if (!chunk.FileName.EndsWith(
                    ".xmb",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                StatusText.Text =
                    $"Reading {chunk.FileName}...";

                byte[] xmbData =
                    EraExtractionService.ExtractChunk(
                        _currentArchive,
                        chunk);

                string xmlText =
                    XmbDocumentService.Read(
                        xmbData);

                if (chunk.FileName.EndsWith(
                        ".scn.xmb",
                        StringComparison.OrdinalIgnoreCase))
                {

                    _currentScenarioOriginalXmbData =
                        xmbData.ToArray();

                    _currentScenarioChunk =
                        chunk;

                    ExportScenarioXmbMenuItem.IsEnabled =
                        true;

                    SaveMenuItem.IsEnabled =
                        true;

                    SaveAsMenuItem.IsEnabled =
                        true;

                    AddObjectMenuItem.IsEnabled =
                        true;

                    RegisterCustomMapMenuItem.IsEnabled =
                        true;

                    MapMetadataMenuItem.IsEnabled =
                        true;

                    ScenarioMap map =
                        ScenarioParserService.Parse(
                            xmlText);

                    ScenarioMapCanvas.SetMap(
                        map);

                    TerrainHeightMap? terrain =
                        TryLoadTerrainHeightMap(map);

                    TerrainTextureMap? terrainTexture =
                        TryLoadTerrainTextureMap(
                            map);

                    TerrainSimulationMap? simulation =
                        TryLoadTerrainSimulationMap(
                            map,
                            terrain);

                    ScenarioMapCanvas.Visibility =
                        Visibility.Visible;

                    XmbPreviewText.Visibility =
                        Visibility.Collapsed;

                    WelcomePanel.Visibility =
                        Visibility.Collapsed;

                    ShowArchiveInformation();

                    StatusText.Text =
                        $"Loaded {map.Name} | " +
                        $"{map.Objects.Count} objects | " +
                        $"{map.PlayerStarts.Count} player starts | " +
                        $"{map.Spheres.Count} design spheres | " +
                        $"{map.Paths.Count} design paths" +
                        (terrain != null
                        ? $" | XTD {terrain.Width}×{terrain.Height} | " +
                        $"height {terrain.MinHeight:0.##} → " +
                        $"{terrain.MaxHeight:0.##}"
                        : " | no XTD terrain loaded")
                        +
                        (terrainTexture != null
                        ? $" | XTT {terrainTexture.Width}×{terrainTexture.Height}"
                        : " | no XTT texture loaded")
                        +
                        (simulation != null
                        ? $" | XSD {simulation.Width}×{simulation.Width}"
                        : " | no XSD simulation loaded");
                }
                else
                {
                    XmbPreviewText.Text =
                        xmlText;

                    XmbPreviewText.Visibility =
                        Visibility.Visible;

                    ScenarioMapCanvas.Visibility =
                        Visibility.Collapsed;

                    WelcomePanel.Visibility =
                        Visibility.Collapsed;

                    StatusText.Text =
                        $"Decoded XMB: {chunk.FileName}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "XMB Read Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "XMB read failed.";
            }

            e.Handled =
                true;
        }

        private static void ValidateScenarioRoundTrip(
            ScenarioMap expected,
            ScenarioMap actual)
        {
            if (expected.Objects.Count !=
                actual.Objects.Count)
            {
                throw new InvalidDataException(
                    "Scenario XMB verification failed: " +
                    "object count changed.");
            }

            foreach (ScenarioObject expectedObject
                     in expected.Objects)
            {
                ScenarioObject? actualObject =
                    actual.Objects.Find(
                        x =>
                            x.Id ==
                            expectedObject.Id);

                if (actualObject == null)
                {
                    throw new InvalidDataException(
                        $"Scenario XMB verification failed: " +
                        $"object ID {expectedObject.Id} disappeared.");
                }

                if (!string.Equals(
                    expectedObject.EditorName,
                    actualObject.EditorName,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Object {expectedObject.Id} EditorName " +
                        $"failed round-trip verification.\n\n" +
                        $"Expected: {expectedObject.EditorName}\n" +
                        $"Actual:   {actualObject.EditorName}");
                }

                if (expectedObject.Player !=
                    actualObject.Player)
                {
                    throw new InvalidDataException(
                        $"Object {expectedObject.Id} Player " +
                        $"failed round-trip verification.");
                }

                if (expectedObject.Group !=
                    actualObject.Group)
                {
                    throw new InvalidDataException(
                        $"Object {expectedObject.Id} Group " +
                        $"failed round-trip verification.");
                }

                if (expectedObject.VisualVariationIndex !=
                    actualObject.VisualVariationIndex)
                {
                    throw new InvalidDataException(
                        $"Object {expectedObject.Id} VisualVariationIndex " +
                        $"failed round-trip verification.");
                }

                RequireVectorEqual(
                    expectedObject.Position,
                    actualObject.Position,
                    $"Object {expectedObject.Id} Position");

                RequireVectorEqual(
                    expectedObject.Forward,
                    actualObject.Forward,
                    $"Object {expectedObject.Id} Forward");

                RequireVectorEqual(
                    expectedObject.Right,
                    actualObject.Right,
                    $"Object {expectedObject.Id} Right");
            }

            foreach (ScenarioPlayerStart expectedStart
                     in expected.PlayerStarts)
            {
                ScenarioPlayerStart? actualStart =
                    actual.PlayerStarts.Find(
                        x =>
                            x.Number ==
                            expectedStart.Number);

                if (actualStart == null)
                {
                    throw new InvalidDataException(
                        $"Scenario XMB verification failed: " +
                        $"player start {expectedStart.Number} disappeared.");
                }

                RequireVectorEqual(
                    expectedStart.Position,
                    actualStart.Position,
                    $"Player Start {expectedStart.Number} Position");

                RequireVectorEqual(
                    expectedStart.Forward,
                    actualStart.Forward,
                    $"Player Start {expectedStart.Number} Forward");
            }

            foreach (ScenarioSphere expectedSphere
                     in expected.Spheres)
            {
                ScenarioSphere? actualSphere =
                    actual.Spheres.Find(
                        x =>
                            x.Id ==
                            expectedSphere.Id);

                if (actualSphere == null)
                {
                    throw new InvalidDataException(
                        $"Scenario XMB verification failed: " +
                        $"design sphere {expectedSphere.Id} disappeared.");
                }

                RequireVectorEqual(
                    expectedSphere.Position,
                    actualSphere.Position,
                    $"Design Sphere {expectedSphere.Id} Position");

                if (MathF.Abs(
                    expectedSphere.Radius -
                    actualSphere.Radius) >
                    0.0001f)
                {
                    throw new InvalidDataException(
                        $"Design Sphere {expectedSphere.Id} Radius " +
                        $"failed round-trip verification.\n\n" +
                        $"Expected: {expectedSphere.Radius}\n" +
                        $"Actual:   {actualSphere.Radius}");
                }
            }

            foreach (ScenarioPath expectedPath
                in expected.Paths)
            {
                ScenarioPath? actualPath =
                    actual.Paths.Find(
                        x =>
                            x.Id ==
                            expectedPath.Id);

                if (actualPath == null)
                {
                    throw new InvalidDataException(
                        $"Scenario XMB verification failed: " +
                        $"design path {expectedPath.Id} disappeared.");
                }

                if (expectedPath.Points.Count !=
                    actualPath.Points.Count)
                {
                    throw new InvalidDataException(
                        $"Design Path {expectedPath.Id} point count " +
                        $"failed round-trip verification.\n\n" +
                        $"Expected: {expectedPath.Points.Count}\n" +
                        $"Actual:   {actualPath.Points.Count}");
                }

                for (int i = 0;
                     i < expectedPath.Points.Count;
                     i++)
                {
                    RequireVectorEqual(
                        expectedPath.Points[i],
                        actualPath.Points[i],
                        $"Design Path {expectedPath.Id} Point {i + 1}");
                }
            }
        }

        private static void ValidateTerrainRoundTrip(
            TerrainHeightMap expected,
            TerrainHeightMap actual)
        {
            if (expected.Width !=
                    actual.Width ||
                expected.Height !=
                    actual.Height)
            {
                throw new InvalidDataException(
                    "Terrain XTD verification failed: " +
                    "dimensions changed.");
            }


            if (expected.Heights.Length !=
                actual.Heights.Length)
            {
                throw new InvalidDataException(
                    "Terrain XTD verification failed: " +
                    "height count changed.");
            }


            float tolerance =
                Math.Max(
                    0.0001f,
                    actual.HeightQuantizationStep *
                    0.51f);


            for (int i = 0;
                 i < expected.Heights.Length;
                 i++)
            {
                float difference =
                    MathF.Abs(
                        expected.Heights[i] -
                        actual.Heights[i]);

                if (difference >
                    tolerance)
                {
                    throw new InvalidDataException(
                        "Terrain XTD verification failed.\n\n" +
                        $"Vertex: {i:N0}\n" +
                        $"Expected: {expected.Heights[i]:0.####}\n" +
                        $"Actual:   {actual.Heights[i]:0.####}\n" +
                        $"Tolerance: {tolerance:0.####}");
                }
            }
        }

        private static void RequireVectorEqual(
            Vector3 expected,
            Vector3 actual,
            string description)
        {
            const float tolerance =
                0.0001f;

            if (MathF.Abs(
                    expected.X -
                    actual.X) >
                    tolerance ||
                MathF.Abs(
                    expected.Y -
                    actual.Y) >
                    tolerance ||
                MathF.Abs(
                    expected.Z -
                    actual.Z) >
                    tolerance)
            {
                throw new InvalidDataException(
                    $"{description} failed round-trip verification.\n\n" +
                    $"Expected: {expected}\n" +
                    $"Actual:   {actual}");
            }
        }

        private void ExportScenarioXmb_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentScenarioOriginalXmbData ==
                    null ||
                _currentScenarioChunk ==
                    null ||
                ScenarioMapCanvas.Scenario ==
                    null)
            {
                return;
            }

            string defaultName =
                System.IO.Path.GetFileName(
                    _currentScenarioChunk.FileName);

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title =
                        "Export Modified Halo Wars Scenario",

                    FileName =
                        defaultName,

                    Filter =
                        "Halo Wars Scenario XMB (*.scn.xmb)|*.scn.xmb|" +
                        "XMB Files (*.xmb)|*.xmb|" +
                        "All Files (*.*)|*.*"
                };

            if (dialog.ShowDialog(this) !=
                true)
            {
                return;
            }

            try
            {
                StatusText.Text =
                    "Building modified scenario XMB...";

                ScenarioMap expected =
                    ScenarioMapCanvas.Scenario;

                byte[] rebuilt =
                    XmbDocumentService.WriteScenario(
                        _currentScenarioOriginalXmbData,
                        expected);

                // -----------------------------------------------------
                // Full Ensemble round-trip verification
                // -----------------------------------------------------

                string verificationXml =
                    XmbDocumentService.Read(
                        rebuilt);

                ScenarioMap verificationMap =
                    ScenarioParserService.Parse(
                        verificationXml);

                ValidateScenarioRoundTrip(
                    expected,
                    verificationMap);

                File.WriteAllBytes(
                    dialog.FileName,
                    rebuilt);

                StatusText.Text =
                    $"Exported and verified {defaultName}";

                MessageBox.Show(
                    this,
                    "Modified scenario exported successfully.\n\n" +
                    "Ensemble decoded the newly generated XMB again " +
                    "and verified all editable scenario positions " +
                    "and orientations before writing it to disk.",
                    "Scenario Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Scenario Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Scenario export failed.";
            }
        }

        private void ScenarioMapCanvas_SelectionChanged(
            object? sender,
            Ensemble.Controls.ScenarioSelectionChangedEventArgs e)
        {

            bool objectSelected =
                e.SelectedItem is ScenarioObject;

            DuplicateObjectMenuItem.IsEnabled =
                objectSelected;

            DeleteObjectMenuItem.IsEnabled =
                objectSelected;

            _selectedScenarioItem =
                e.SelectedItem;

            if (e.SelectedItem == null)
            {
                ShowArchiveInformation();

                return;
            }

            ArchiveInfoPanel.Visibility =
                Visibility.Collapsed;

            SelectionInfoPanel.Visibility =
                Visibility.Visible;

            PopulatePositionEditor(
                e.SelectedItem);

            PopulateRotationEditor(
                e.SelectedItem);

            switch (e.SelectedItem)
            {
                case ScenarioObject obj:

                    ShowScenarioObject(
                        obj);

                    break;


                case ScenarioPlayerStart start:

                    ShowPlayerStart(
                        start);

                    break;


                case ScenarioSphere sphere:

                    ShowScenarioSphere(
                        sphere);

                    break;


                case ScenarioPath path:

                    ShowScenarioPath(
                        path);

                    break;
            }
        }

        private void ScenarioMapCanvas_ObjectAdded(
            object? sender,
            Ensemble.Controls.ScenarioObjectAddedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new AddObjectHistoryAction(
                    e.Object,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Added {e.Object.Type} | " +
                $"New ID: {e.Object.Id} | " +
                $"{ScenarioMapCanvas.Scenario?.Objects.Count ?? 0} objects";
        }

        private void ScenarioMapCanvas_PathPointMoved(
            object? sender,
            Ensemble.Controls.ScenarioPathPointMovedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new PathPointMoveHistoryAction(
                    e.Path,
                    e.PointIndex,
                    e.OldPosition,
                    e.NewPosition,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Moved {e.Path.Name} point {e.PointIndex + 1} | " +
                $"X {e.NewPosition.X:0.##}, " +
                $"Z {e.NewPosition.Z:0.##}";
        }

        //-----------------------------------------------------
        // Terrain
        //-----------------------------------------------------
        private TerrainHeightMap? TryLoadTerrainHeightMap(
            ScenarioMap map)
        {
            if (_currentArchive == null)
                return null;

            List<EraChunkInfo> candidates =
                _currentArchive.Chunks
                    .Where(
                        x =>
                            x.FileName.EndsWith(
                                ".xtd",
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (candidates.Count ==
                0)
            {
                ScenarioMapCanvas
                    .SetTerrainHeightMap(
                        null);

                return null;

                _currentTerrainChunk =
                    null;

                _currentTerrainOriginalXtdData =
                    null;
            }


            string terrainKey =
                NormalizeTerrainName(
                    map.Terrain);

            EraChunkInfo? terrainChunk =
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            GetEraFileStem(
                                x.FileName))
                        ==
                        terrainKey);


            terrainChunk ??=
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            x.FileName)
                            .Contains(
                                terrainKey,
                                StringComparison.Ordinal));


            // Most skirmish ERAs only contain one XTD,
            // so use it if filename matching wasn't necessary.
            if (terrainChunk == null &&
                candidates.Count ==
                    1)
            {
                terrainChunk =
                    candidates[0];
            }


            if (terrainChunk == null)
            {
                ScenarioMapCanvas
                    .SetTerrainHeightMap(
                        null);

                return null;

                _currentTerrainChunk =
                    null;

                _currentTerrainOriginalXtdData =
                    null;
            }


            byte[] xtdData =
                EraExtractionService.ExtractChunk(
                    _currentArchive,
                    terrainChunk);

            _currentTerrainChunk =
                terrainChunk;

            _currentTerrainOriginalXtdData =
                xtdData.ToArray();

            TerrainHeightMap terrain =
                TerrainXtdService.Read(
                    xtdData);

            ScenarioMapCanvas
                .SetTerrainHeightMap(
                    terrain);

            return terrain;
        }

        private TerrainTextureMap? TryLoadTerrainTextureMap(
            ScenarioMap map)
        {
            if (_currentArchive == null)
                return null;

            List<EraChunkInfo> candidates =
                _currentArchive.Chunks
                    .Where(
                        x =>
                            x.FileName.EndsWith(
                                ".xtt",
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (candidates.Count ==
                0)
            {
                ScenarioMapCanvas
                    .SetTerrainTextureMap(
                        null);

                return null;
            }


            string terrainKey =
                NormalizeTerrainName(
                    map.Terrain);

            EraChunkInfo? terrainChunk =
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            GetEraFileStem(
                                x.FileName))
                        ==
                        terrainKey);


            terrainChunk ??=
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            x.FileName)
                            .Contains(
                                terrainKey,
                                StringComparison.Ordinal));


            if (terrainChunk == null &&
                candidates.Count ==
                    1)
            {
                terrainChunk =
                    candidates[0];
            }


            if (terrainChunk == null)
            {
                ScenarioMapCanvas
                    .SetTerrainTextureMap(
                        null);

                return null;
            }


            byte[] xttData =
                EraExtractionService.ExtractChunk(
                    _currentArchive,
                    terrainChunk);

            TerrainTextureMap terrain =
                TerrainXttService.Read(
                    xttData);

            ScenarioMapCanvas
                .SetTerrainTextureMap(
                    terrain);

            return terrain;
        }

        private TerrainSimulationMap?
            TryLoadTerrainSimulationMap(
            ScenarioMap map,
        TerrainHeightMap? referenceTerrain)
        {
            if (_currentArchive == null ||
                referenceTerrain == null)
            {
                _currentSimulationChunk =
                    null;

                _currentSimulationOriginalXsdData =
                    null;

                return null;
            }


            List<EraChunkInfo> candidates =
                _currentArchive.Chunks
                    .Where(
                        x =>
                            x.FileName.EndsWith(
                                ".xsd",
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();


            if (candidates.Count ==
                0)
            {
                _currentSimulationChunk =
                    null;

                _currentSimulationOriginalXsdData =
                    null;

                return null;
            }


            string terrainKey =
                NormalizeTerrainName(
                    map.Terrain);


            EraChunkInfo? terrainChunk =
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            GetEraFileStem(
                                x.FileName))
                        ==
                        terrainKey);


            terrainChunk ??=
                candidates.FirstOrDefault(
                    x =>
                        NormalizeTerrainName(
                            x.FileName)
                        .Contains(
                            terrainKey,
                            StringComparison.Ordinal));


            if (terrainChunk == null &&
                candidates.Count ==
                    1)
            {
                terrainChunk =
                    candidates[0];
            }


            if (terrainChunk == null)
            {
                _currentSimulationChunk =
                    null;

                _currentSimulationOriginalXsdData =
                    null;

                return null;
            }


            byte[] xsdData =
                EraExtractionService.ExtractChunk(
                    _currentArchive,
                    terrainChunk);


            TerrainSimulationMap simulation =
                TerrainXsdService.Read(
                    xsdData,
                    referenceTerrain);


            _currentSimulationChunk =
                terrainChunk;

            _currentSimulationOriginalXsdData =
                xsdData.ToArray();


            return simulation;
        }

        private void ScenarioMapCanvas_TerrainPreviewChanged(
            object? sender,
            EventArgs e)
        {
            UpdateDirtyState();
        }

        private static string GetEraFileStem(
            string fileName)
        {
            string normalized =
                fileName.Replace(
                    '\\',
                    '/');

            int slash =
                normalized.LastIndexOf(
                    '/');

            string leaf =
                slash >= 0
                    ? normalized[
                        (slash + 1)..]
                    : normalized;

            int dot =
                leaf.LastIndexOf(
                    '.');

            return dot >= 0
                ? leaf[..dot]
                : leaf;
        }

        private static string NormalizeTerrainName(string value)
        {
            if (string.IsNullOrWhiteSpace(
                value))
            {
                return string.Empty;
            }

            return new string(
                value
                    .Where(
                        char.IsLetterOrDigit)
                    .Select(
                        char.ToLowerInvariant)
                    .ToArray());
        }

        private sealed class AddObjectHistoryAction :
            IScenarioHistoryAction
        {
            public AddObjectHistoryAction(
                ScenarioObject obj,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Object =
                    obj;

                BeforeRevisionId =
                    beforeRevisionId;

                AfterRevisionId =
                    afterRevisionId;
            }

            public ScenarioObject Object
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

            public string Description =>
                $"Add {Object.Type}";

            public void Undo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryRemoveObject(
                    Object);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryAddObject(
                    Object);
            }
        }

        private void PopulatePositionEditor(
            object item)
        {
            System.Numerics.Vector3 position;

            bool editable;

            SphereRadiusEditorPanel.Visibility =
                item is ScenarioSphere
                ? Visibility.Visible
                : Visibility.Collapsed;

            switch (item)
            {
                case ScenarioObject obj:

                    position =
                        obj.Position;

                    editable =
                        true;

                    break;


                case ScenarioPlayerStart start:

                    position =
                        start.Position;

                    editable =
                        true;

                    break;


                case ScenarioSphere sphere:

                    position =
                        sphere.Position;

                    editable =
                        true;

                    SphereRadiusTextBox.Text =
                        sphere.Radius.ToString(
                            "G9",
                            CultureInfo.InvariantCulture);

                    break;


                case ScenarioPath path:

                    position =
                        path.Position;

                    // We haven't implemented path editing yet.
                    editable =
                        false;

                    break;


                default:

                    position =
                        System.Numerics.Vector3.Zero;

                    editable =
                        false;

                    break;
            }

            PositionXTextBox.Text =
                position.X.ToString(
                    "G9",
                    CultureInfo.InvariantCulture);

            PositionYTextBox.Text =
                position.Y.ToString(
                    "G9",
                    CultureInfo.InvariantCulture);

            PositionZTextBox.Text =
                position.Z.ToString(
                    "G9",
                    CultureInfo.InvariantCulture);

            PositionXTextBox.IsEnabled =
                editable;

            PositionYTextBox.IsEnabled =
                editable;

            PositionZTextBox.IsEnabled =
                editable;

            ApplyPositionButton.IsEnabled =
                editable;

            PositionEditHintText.Text =
                editable
                    ? "Enter applies the position."
                    : "This item is currently read-only.";
        }

        private void PopulateRotationEditor(
            object item)
        {
            Vector3 forward;

            bool editable;

            switch (item)
            {
                case ScenarioObject obj:

                    forward =
                        obj.Forward;

                    editable =
                        true;

                    break;


                case ScenarioPlayerStart start:

                    forward =
                        start.Forward;

                    editable =
                        true;

                    break;


                default:

                    forward =
                        Vector3.Zero;

                    editable =
                        false;

                    break;
            }

            float yaw =
                Ensemble.Controls.MapCanvas
                    .GetYawDegrees(
                        forward);

            YawTextBox.Text =
                yaw.ToString(
                    "0.####",
                    CultureInfo.InvariantCulture);

            ApplyRotationButton.IsEnabled =
                editable;

            YawTextBox.IsEnabled =
                editable;

            RotationEditHintText.Text =
                editable
                    ? "Enter applies rotation. 0° = +Z."
                    : "This item has no editable orientation.";
        }

        private void ApplyPosition_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApplyEditedPosition();
        }

        private void ApplyEditedPosition()
        {
            if (_selectedScenarioItem == null)
                return;

            if (!TryReadEditorFloat(
                    PositionXTextBox.Text,
                    out float x))
            {
                ShowInvalidCoordinate(
                    "X",
                    PositionXTextBox);

                return;
            }

            if (!TryReadEditorFloat(
                    PositionYTextBox.Text,
                    out float y))
            {
                ShowInvalidCoordinate(
                    "Y",
                    PositionYTextBox);

                return;
            }

            if (!TryReadEditorFloat(
                    PositionZTextBox.Text,
                    out float z))
            {
                ShowInvalidCoordinate(
                    "Z",
                    PositionZTextBox);

                return;
            }

            System.Numerics.Vector3 newPosition =
                new System.Numerics.Vector3(
                    x,
                    y,
                    z);

            try
            {
                ScenarioMapCanvas.MoveItemFromEditor(
                    _selectedScenarioItem,
                    newPosition);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to Change Position",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplyRotation_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApplyEditedRotation();
        }

        private void YawTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key !=
                Key.Enter)
            {
                return;
            }

            ApplyEditedRotation();

            e.Handled =
                true;
        }

        private void ApplyEditedRotation()
        {
            if (_selectedScenarioItem == null)
                return;

            if (!TryReadEditorFloat(
                    YawTextBox.Text,
                    out float yaw))
            {
                MessageBox.Show(
                    this,
                    $"'{YawTextBox.Text}' is not a valid yaw angle.",
                    "Invalid Rotation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                YawTextBox.Focus();

                YawTextBox.SelectAll();

                return;
            }

            try
            {
                ScenarioMapCanvas.RotateItemFromEditor(
                    _selectedScenarioItem,
                    yaw);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to Change Rotation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PositionTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key !=
                Key.Enter)
            {
                return;
            }

            ApplyEditedPosition();

            e.Handled =
                true;
        }

        private void DuplicateObject_Click(
            object sender,
            RoutedEventArgs e)
        {
            DuplicateSelectedObject();
        }

        private void DuplicateSelectedObject()
        {
            if (_selectedScenarioItem
                    is not ScenarioObject source ||
                ScenarioMapCanvas.Scenario ==
                    null)
            {
                return;
            }

            ScenarioMap map =
                ScenarioMapCanvas.Scenario;

            int newId =
                ++map.MaxKnownId;

            int templateId =
                source.IsNewObject
                    ? source.SourceObjectId
                    : source.Id;

            ScenarioObject duplicate =
                new ScenarioObject
                {
                    Id =
                        newId,

                    IsNewObject =
                        true,

                    SourceObjectId =
                        templateId,

                    IsSquad =
                        source.IsSquad,

                    Player =
                        source.Player,

                    TintValue =
                        source.TintValue,

                    EditorName =
                        source.EditorName,

                    Type =
                        source.Type,

                    Position =
                        source.Position +
                        new Vector3(
                            12,
                            0,
                            12),

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
                duplicate.Flags.Add(
                    flag);
            }

            ScenarioMapCanvas
                .AddScenarioObjectFromEditor(
                    duplicate);
        }

        private void ScenarioMapCanvas_ItemMoved(
            object? sender,
            Ensemble.Controls.ScenarioItemMovedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new MoveHistoryAction(
                    e.Item,
                    e.OldPosition,
                    e.NewPosition,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Moved {GetItemDisplayName(e.Item)} | " +
                $"X {e.NewPosition.X:0.##}, " +
                $"Y {e.NewPosition.Y:0.##}, " +
                $"Z {e.NewPosition.Z:0.##}";
        }

        private void ScenarioMapCanvas_ItemRotated(
            object? sender,
            Ensemble.Controls.ScenarioItemRotatedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new RotationHistoryAction(
                    e.Item,
                    e.OldForward,
                    e.OldRight,
                    e.NewForward,
                    e.NewRight,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            float yaw =
                Ensemble.Controls.MapCanvas
                    .GetYawDegrees(
                        e.NewForward);

            StatusText.Text =
                $"Rotated {GetItemDisplayName(e.Item)} | " +
                $"Yaw {yaw:0.##}°";
        }

        private void ShowScenarioObject(
            ScenarioObject obj)
        {

            ObjectPropertiesEditorPanel.Visibility =
                Visibility.Visible;

            ObjectEditorNameTextBox.Text =
                obj.EditorName;

            ObjectPlayerTextBox.Text =
                obj.Player.ToString(
                    CultureInfo.InvariantCulture);

            ObjectGroupTextBox.Text =
                obj.Group.ToString(
                    CultureInfo.InvariantCulture);

            ObjectVisualVariationTextBox.Text =
                obj.VisualVariationIndex.ToString(
                    CultureInfo.InvariantCulture);

            SphereRadiusEditorPanel.Visibility =
                Visibility.Collapsed;

            RightPanelTitle.Text =
                "OBJECT PROPERTIES";

            SelectedNameText.Text =
                obj.EditorName;

            SelectedTypeText.Text =
                $"{obj.Type} ({obj.Category})";

            SelectedIdText.Text =
                obj.Id.ToString();

            SelectedPositionText.Text =
                FormatVector(
                    obj.Position);

            SelectedForwardText.Text =
                FormatVector(
                    obj.Forward);

            SelectedRightText.Text =
                FormatVector(
                    obj.Right);

            SelectedYawText.Text =
                $"{Ensemble.Controls.MapCanvas.GetYawDegrees(obj.Forward):0.####}°";

            string flags =
                obj.Flags.Count == 0
                    ? "None"
                    : string.Join(
                        ", ",
                        obj.Flags);

            SelectedDetailsText.Text =
                $"Player: {obj.Player}\n" +
                $"Group: {obj.Group}\n" +
                $"Visual Variation: {obj.VisualVariationIndex}\n" +
                $"Is Squad: {obj.IsSquad}\n" +
                $"Flags: {flags}";
        }

        private void UpdateDirtyState()
        {
            _isDirty =
                _currentRevisionId !=
                _savedRevisionId
                ||
                ScenarioMapCanvas
                .HasTerrainPreviewChanges;

            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            if (_currentArchive == null)
            {
                Title =
                    "Ensemble - Halo Wars Map Editor";

                return;
            }

            string fileName;

            if (!string.IsNullOrWhiteSpace(
                    _currentSavePath))
            {
                fileName =
                    System.IO.Path.GetFileName(
                        _currentSavePath);
            }
            else
            {
                fileName =
                    _currentArchive.FileName;
            }

            Title =
                $"Ensemble - {fileName}" +
                (_isDirty
                    ? " *"
                    : string.Empty);
        }

        private void ShowPlayerStart(
            ScenarioPlayerStart start)
        {

            ObjectPropertiesEditorPanel.Visibility =
                Visibility.Collapsed;

            RightPanelTitle.Text =
                "PLAYER START";

            SelectedNameText.Text =
                $"Player Start {start.Number}";

            SelectedTypeText.Text =
                "Scenario Start Position";

            SelectedIdText.Text =
                start.Number.ToString();

            SelectedPositionText.Text =
                FormatVector(
                    start.Position);

            SelectedForwardText.Text =
                FormatVector(
                    start.Forward);

            SelectedRightText.Text = "-";

            SelectedYawText.Text =
                $"{Ensemble.Controls.MapCanvas.GetYawDegrees(start.Forward):0.####}°";

            SelectedDetailsText.Text =
                $"Player: {start.Player}\n" +
                $"Default Camera: {start.DefaultCamera}\n" +
                $"Camera Yaw: {start.CameraYaw:0.####}\n" +
                $"Camera Pitch: {start.CameraPitch:0.####}\n" +
                $"Camera Zoom: {start.CameraZoom:0.####}";
        }

        // =========================================================
        // Design Sphere
        // =========================================================

        private void ShowScenarioSphere(
            ScenarioSphere sphere)
        {

            ObjectPropertiesEditorPanel.Visibility =
                Visibility.Collapsed;

            RightPanelTitle.Text =
                "DESIGN SPHERE";

            SphereRadiusEditorPanel.Visibility =
                Visibility.Visible;

            SphereRadiusTextBox.Text =
                sphere.Radius.ToString(
                    "G9",
                    CultureInfo.InvariantCulture);

            SelectedNameText.Text =
                sphere.Name;

            SelectedTypeText.Text =
                sphere.Type;

            SelectedIdText.Text =
                sphere.Id.ToString();

            SelectedPositionText.Text =
                FormatVector(
                    sphere.Position);

            SelectedForwardText.Text =
                "-";

            SelectedRightText.Text =
                "-";

            SelectedYawText.Text =
                "-";

            SelectedDetailsText.Text =
                $"Radius: {sphere.Radius:0.####}";
        }

        private void ApplySphereRadius_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApplyEditedSphereRadius();
        }

        private void SphereRadiusTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            ApplyEditedSphereRadius();

            e.Handled =
                true;
        }

        private void ApplyEditedSphereRadius()
        {
            if (_selectedScenarioItem
                is not ScenarioSphere sphere)
            {
                return;
            }

            if (!TryReadEditorFloat(
                    SphereRadiusTextBox.Text,
                    out float radius))
            {
                MessageBox.Show(
                    this,
                    $"'{SphereRadiusTextBox.Text}' is not a valid radius.",
                    "Invalid Radius",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SphereRadiusTextBox.Focus();
                SphereRadiusTextBox.SelectAll();

                return;
            }

            if (radius < 0)
            {
                MessageBox.Show(
                    this,
                    "Sphere radius cannot be negative.",
                    "Invalid Radius",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            ScenarioMapCanvas.ChangeSphereRadiusFromEditor(
                sphere,
                radius);
        }

        private void ScenarioMapCanvas_SphereRadiusChanged(
            object? sender,
            Ensemble.Controls.ScenarioSphereRadiusChangedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new SphereRadiusHistoryAction(
                    e.Sphere,
                    e.OldRadius,
                    e.NewRadius,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();
            UpdateDirtyState();

            StatusText.Text =
                $"Changed {e.Sphere.Name} radius | " +
                $"{e.OldRadius:0.##} → {e.NewRadius:0.##}";
        }

        // ========================================================
        // Design Path
        // ========================================================

        private void ShowScenarioPath(
            ScenarioPath path)
        {

            ObjectPropertiesEditorPanel.Visibility =
                Visibility.Collapsed;

            SphereRadiusEditorPanel.Visibility =
                Visibility.Collapsed;

            RightPanelTitle.Text =
                "DESIGN PATH";

            SelectedNameText.Text =
                path.Name;

            SelectedTypeText.Text =
                path.Type;

            SelectedIdText.Text =
                path.Id.ToString();

            SelectedPositionText.Text =
                FormatVector(
                    path.Position);

            SelectedForwardText.Text =
                "-";

            SelectedRightText.Text =
                "-";

            SelectedYawText.Text =
                "-";

            SelectedDetailsText.Text =
                $"Points: {path.Points.Count}\n" +
                "Drag the white vertex handles on the map " +
                "to edit this path.";
        }

        private void ShowArchiveInfo_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowArchiveInformation();
        }

        private void ShowArchiveInformation()
        {

            SphereRadiusEditorPanel.Visibility =
                Visibility.Collapsed;

            RightPanelTitle.Text =
                "ARCHIVE INFORMATION";

            SelectionInfoPanel.Visibility =
                Visibility.Collapsed;

            ArchiveInfoPanel.Visibility =
                Visibility.Visible;
        }

        private static string GetItemDisplayName(
            object item)
        {
            return item switch
            {
                ScenarioObject obj =>
                    obj.EditorName,

                ScenarioPlayerStart start =>
                    $"Player Start {start.Number}",

                ScenarioSphere sphere =>
                    sphere.Name,

                ScenarioPath path =>
                    path.Name,

                _ =>
                    "Map Item"
            };
        }

        private static string FormatVector(
            System.Numerics.Vector3 vector)
        {
            return
                $"X: {vector.X:0.####}\n" +
                $"Y: {vector.Y:0.####}\n" +
                $"Z: {vector.Z:0.####}";
        }

        private static bool TryReadEditorFloat(
            string text,
            out float value)
        {
            // Halo Wars files use '.' as the decimal separator,
            // so try invariant culture first.

            if (float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }

            // Also accept the user's Windows regional format.
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value);
        }

        private void ShowInvalidCoordinate(
            string coordinate,
            System.Windows.Controls.TextBox textBox)
        {
            MessageBox.Show(
                this,
                $"'{textBox.Text}' is not a valid {coordinate} coordinate.",
                "Invalid Position",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            textBox.Focus();

            textBox.SelectAll();
        }

        // =========================================================
        // Object Properties Editor
        // =========================================================

        private void ScenarioMapCanvas_ObjectPropertiesChanged(
            object? sender,
            Ensemble.Controls.ScenarioObjectPropertiesChangedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new ObjectPropertiesHistoryAction(
        e.Object,

        e.OldEditorName,

        e.OldPlayer,
        e.OldGroup,
        e.OldVisualVariationIndex,

        e.NewEditorName,

        e.NewPlayer,
        e.NewGroup,
        e.NewVisualVariationIndex,

        beforeRevision,
        afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Updated {e.Object.EditorName} properties";
        }

        private void ApplyObjectProperties_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApplyEditedObjectProperties();
        }

        private void ObjectPropertyTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key !=
                Key.Enter)
            {
                return;
            }

            ApplyEditedObjectProperties();

            e.Handled =
                true;
        }

        private void ApplyEditedObjectProperties()
        {
            if (_selectedScenarioItem
                is not ScenarioObject obj)
            {
                return;
            }

            string editorName =
                ObjectEditorNameTextBox.Text;

            if (string.IsNullOrWhiteSpace(
                editorName))
            {
                MessageBox.Show(
                    this,
                    "Editor Name cannot be empty.",
                    "Invalid Object Property",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ObjectEditorNameTextBox.Focus();

                return;
            }

            if (!int.TryParse(
                    ObjectPlayerTextBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int player))
            {
                ShowInvalidInteger(
                    "Player",
                    ObjectPlayerTextBox);

                return;
            }

            if (!int.TryParse(
                    ObjectGroupTextBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int group))
            {
                ShowInvalidInteger(
                    "Group",
                    ObjectGroupTextBox);

                return;
            }

            if (!int.TryParse(
                    ObjectVisualVariationTextBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int variation))
            {
                ShowInvalidInteger(
                    "Visual Variation",
                    ObjectVisualVariationTextBox);

                return;
            }

            ScenarioMapCanvas.ChangeObjectPropertiesFromEditor(
                obj,
                editorName,
                player,
                group,
                variation);
        }

        private void ShowInvalidInteger(
            string propertyName,
            TextBox textBox)
        {
            MessageBox.Show(
                this,
                $"'{textBox.Text}' is not a valid {propertyName} value.",
                "Invalid Object Property",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            textBox.Focus();

            textBox.SelectAll();
        }


        private void BuildArchiveTree()
        {
            ArchiveTree.Items.Clear();

            if (_currentArchive == null)
                return;

            TreeViewItem root =
                new TreeViewItem
                {
                    Header =
                        _currentArchive.FileName,

                    IsExpanded =
                        true
                };

            // =========================================================
            // Archive metadata
            // =========================================================

            TreeViewItem infoNode =
                new TreeViewItem
                {
                    Header = "Archive Information"
                };

            infoNode.Items.Add(
                CreateTreeItem(
                    "Format",
                    "Halo Wars ECF Archive"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Status",
                    "Valid ERA"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Encryption",
                    _currentArchive.IsEncrypted
                        ? "Encrypted"
                        : "Unencrypted"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "ECF Magic",
                    $"0x{_currentArchive.Magic:X8}"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Archive ID",
                    $"0x{_currentArchive.ArchiveId:X8}"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Archive Magic",
                    $"0x{_currentArchive.ArchiveHeaderMagic:X8}"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Header Size",
                    $"{_currentArchive.HeaderSize:N0} bytes"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Chunks",
                    _currentArchive.NumChunks.ToString("N0")));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Chunk Extra Data",
                    $"{_currentArchive.ChunkExtraDataSize:N0} bytes"));

            infoNode.Items.Add(
                CreateTreeItem(
                    "Signature Size",
                    $"{_currentArchive.SignatureSize:N0} bytes"));

            root.Items.Add(
                infoNode);

            // =========================================================
            // Internal archive data
            // =========================================================

            EraChunkInfo filenameChunk =
                _currentArchive.Chunks[0];

            TreeViewItem internalNode =
                new TreeViewItem
                {
                    Header = "Internal"
                };

            TreeViewItem filenameNode =
                new TreeViewItem
                {
                    Header =
                        "Filename Table"
                };

            filenameNode.Items.Add(
                CreateTreeItem(
                    "Chunk",
                    "0"));

            filenameNode.Items.Add(
                CreateTreeItem(
                    "Offset",
                    $"0x{filenameChunk.Offset:X8} " +
                    $"({filenameChunk.Offset:N0})"));

            filenameNode.Items.Add(
                CreateTreeItem(
                    "Compressed",
                    FormatBytes(
                        filenameChunk.CompressedSize)));

            filenameNode.Items.Add(
                CreateTreeItem(
                    "Decompressed",
                    FormatBytes(
                        filenameChunk.DecompressedSize)));

            filenameNode.Items.Add(
                CreateTreeItem(
                    "Compression",
                    filenameChunk.CompressionName));

            internalNode.Items.Add(
                filenameNode);

            root.Items.Add(
                internalNode);

            // =========================================================
            // Actual archived files
            // =========================================================

            int fileCount =
                _currentArchive.Chunks.Count - 1;

            TreeViewItem filesNode =
                new TreeViewItem
                {
                    Header =
                        $"Archive Files ({fileCount})",

                    IsExpanded =
                        true
                };

            for (int i = 1;
                 i < _currentArchive.Chunks.Count;
                 i++)
            {
                EraChunkInfo chunk =
                    _currentArchive.Chunks[i];

                string name =
                    string.IsNullOrWhiteSpace(
                        chunk.FileName)
                        ? $"Chunk {chunk.Index}"
                        : chunk.FileName;

                TreeViewItem fileNode =
                    new TreeViewItem
                    {
                        Header = name,
                        Tag = chunk
                    };

                fileNode.MouseDoubleClick +=
                    ArchiveFile_MouseDoubleClick;

                ContextMenu contextMenu =
                    new ContextMenu();

                MenuItem extractItem =
                    new MenuItem
                    {
                        Header =
                            "Extract File..."
                    };

                extractItem.Click +=
                    ExtractFile_Click;

                contextMenu.Items.Add(
                    extractItem);

                fileNode.ContextMenu =
                    contextMenu;

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Chunk",
                        chunk.Index.ToString()));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Offset",
                        $"0x{chunk.Offset:X8} " +
                        $"({chunk.Offset:N0})"));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Compressed Size",
                        FormatBytes(
                            chunk.CompressedSize)));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Original Size",
                        FormatBytes(
                            chunk.DecompressedSize)));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Compression",
                        chunk.CompressionName));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "Name Offset",
                        chunk.NameOffset
                            .ToString("N0")));

                fileNode.Items.Add(
                    CreateTreeItem(
                        "ID",
                        $"0x{chunk.Id:X16}"));

                filesNode.Items.Add(
                    fileNode);
            }

            root.Items.Add(
                filesNode);

            ArchiveTree.Items.Add(
                root);
        }

        private static TreeViewItem
            CreateTreeItem(
                string name,
                string value)
        {
            return new TreeViewItem
            {
                Header =
                    $"{name}: {value}"
            };
        }

        private static string FormatBytes(
            long bytes)
        {
            string[] suffixes =
            {
                "B",
                "KB",
                "MB",
                "GB",
                "TB"
            };

            double value =
                bytes;

            int suffix =
                0;

            while (
                value >= 1024 &&
                suffix <
                suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }

            return
                $"{value:0.##} {suffixes[suffix]}";
        }

        // =========================================================
        // Tools menu bar
        // ========================================================

        private void PatchHaloWarsExe_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog =
                new OpenFileDialog
                {
                    Title =
                        "Select Halo Wars Definitive Edition Executable",

                    FileName =
                        "xgameFinal.exe",

                    Filter =
                        "Halo Wars Executable (xgameFinal.exe)|xgameFinal.exe|" +
                        "Executable Files (*.exe)|*.exe|" +
                        "All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false
                };


            if (dialog.ShowDialog(this) !=
                true)
            {
                return;
            }


            MessageBoxResult confirmation =
                MessageBox.Show(
                    this,

                    "Ensemble will install its full Halo Wars modular " +
                    "map patch.\n\n" +

                    "Supported features:\n" +
                    "• Modified ERA archive support\n" +
                    "• Loose-file support\n" +
                    "• Self-contained ENSMAP1 map manifests\n" +
                    "• Automatic custom-map discovery\n" +
                    "• Dynamic map registration\n" +
                    "• Dynamic custom-map localization\n\n" +

                    "Stock executables and older Ensemble-patched " +
                    "executables are supported.\n\n" +

                    "If the current modular patch is already installed, " +
                    "the executable will only be verified and will not " +
                    "be rewritten.\n\n" +

                    "An untouched stock backup will be preserved.\n\n" +

                    "Continue?",

                    "Install Ensemble Modular Patch",

                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);


            if (confirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }


            try
            {
                StatusText.Text =
                    "Checking Halo Wars modular patch...";


                HaloWarsExePatchResult result =
                    HaloWarsExePatchService.Patch(
                        dialog.FileName);


                string eraStatus =
                    result.EraSignaturePatchChanged
                        ? "Applied now"
                        : "Already present";


                string looseStatus =
                    result.LooseFilesPatchChanged
                        ? "Applied now"
                        : "Already present";


                string modularStatus =
                    result.ModularPatchChanged
                        ? "Installed / upgraded now"
                        : "Already present";


                string overallStatus =
                    result.WasModified
                        ? "Ensemble updated the Halo Wars executable."
                        : "The executable already contains the current " +
                          "Ensemble modular patch.";


                string backupText =
                    string.IsNullOrWhiteSpace(
                        result.BackupPath)
                        ? "Existing untouched backup preserved."
                        : $"Untouched backup:\n{result.BackupPath}";


                MessageBox.Show(
                    this,

                    $"{overallStatus}\n\n" +

                    "ERA signature bypass:\n" +
                    $"{eraStatus}\n" +
                    $"Offset: 0x{result.EraSignaturePatchOffset:X}\n\n" +

                    "Loose-file support:\n" +
                    $"{looseStatus}\n" +
                    $"Offset: 0x{result.LooseFilesPatchOffset:X}\n\n" +

                    "ERA-only modular map support:\n" +
                    $"{modularStatus}\n" +
                    $"Entry point RVA: 0x{result.ModularEntryPointRva:X8}\n" +
                    $"Payload file offset: " +
                    $"0x{result.ModularPayloadFileOffset:X}\n\n" +

                    $"{backupText}\n\n" +

                    "SHA1 Before:\n" +
                    $"{result.Sha1Before}\n\n" +

                    "SHA1 After:\n" +
                    $"{result.Sha1After}",

                    "Ensemble Modular Patch",

                    MessageBoxButton.OK,
                    MessageBoxImage.Information);


                StatusText.Text =
                    result.WasModified
                        ? "Halo Wars updated with Ensemble ERA-only modular support."
                        : "Halo Wars already has the current Ensemble modular patch.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),

                    "EXE Patch Failed",

                    MessageBoxButton.OK,
                    MessageBoxImage.Error);


                StatusText.Text =
                    "Halo Wars modular patch failed.";
            }
        }

        private void RegisterCustomMap_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive ==
                    null ||
                _currentScenarioChunk ==
                    null)
            {
                return;
            }


            // =========================================================
            // Save current map edits before installation.
            // =========================================================

            if (_isDirty)
            {
                MessageBoxResult saveResult =
                    MessageBox.Show(
                        this,

                        "The current map contains unsaved changes.\n\n" +
                        "Save them before installing the custom map?",

                        "Install Custom Map",

                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);


                if (saveResult !=
                    MessageBoxResult.Yes)
                {
                    return;
                }


                if (!SaveCurrentDocument())
                {
                    return;
                }
            }


            try
            {
                // =========================================================
                // Determine ScenarioDescriptions registration path.
                // =========================================================

                string registrationFile =
                    ScenarioDescriptionsService
                        .BuildScenarioRegistrationPath(
                            _currentScenarioChunk);


                string scenarioBasename =
                    Path.GetFileNameWithoutExtension(
                        registrationFile);


                string eraBasename =
                    Path.GetFileNameWithoutExtension(
                        _currentArchive.FileName);


                // =========================================================
                // ERA basename and internal SCN basename MUST match.
                // =========================================================

                if (!string.Equals(
                        scenarioBasename,
                        eraBasename,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,

                        "This ERA is not ready to be installed as a " +
                        "separate custom map.\n\n" +

                        "The ERA filename and internal scenario basename " +
                        "must match.\n\n" +

                        $"ERA:\n{eraBasename}\n\n" +

                        $"Scenario:\n{scenarioBasename}\n\n" +

                        "Use 'Save As...' to create a standalone custom ERA first.",

                        "Custom Map Naming Mismatch",

                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);


                    return;
                }


                // =========================================================
                // Select Halo Wars executable.
                // =========================================================

                OpenFileDialog dialog =
                    new OpenFileDialog
                    {
                        Title =
                            "Select Halo Wars Definitive Edition Executable",

                        FileName =
                            "xgameFinal.exe",

                        Filter =
                            "Halo Wars Executable (xgameFinal.exe)|xgameFinal.exe|" +
                            "Executable Files (*.exe)|*.exe|" +
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


                string exePath =
                    dialog.FileName;


                string gameDirectory =
                    Path.GetDirectoryName(
                        exePath)
                    ?? throw new InvalidDataException(
                        "Unable to determine the Halo Wars game directory.");


                string rootPath =
                    Path.Combine(
                        gameDirectory,
                        "root.era");


                if (!File.Exists(
                        rootPath))
                {
                    throw new FileNotFoundException(
                        "Ensemble could not find root.era beside " +
                        "xgameFinal.exe.\n\n" +
                        rootPath,
                        rootPath);
                }


                // =========================================================
                // Patch / verify xgameFinal.exe.
                //
                // This operation is idempotent.
                // =========================================================

                StatusText.Text =
                    "Checking Ensemble modding patches...";


                HaloWarsExePatchResult patchResult =
                    HaloWarsExePatchService.Patch(
                        exePath);


                if (!patchResult.EraSignatureBypassEnabled ||
                    !patchResult.LooseFilesEnabled)
                {
                    throw new InvalidDataException(
                        "The Halo Wars executable does not contain " +
                        "all required Ensemble patches.");
                }


                string patchStatus =
                    patchResult.WasModified
                        ? "Applied during this export"
                        : "Already installed";


                // =========================================================
                // Read STOCK ScenarioDescriptions and DE StringTables
                // from root.era.
                //
                // root.era itself is NEVER modified.
                // =========================================================

                StatusText.Text =
                    "Reading stock Halo Wars data...";


                EraArchiveInfo rootArchive =
                    EraArchiveService.Open(
                        rootPath);


                EraChunkInfo descriptionsChunk =
                    ScenarioDescriptionsService
                        .FindScenarioDescriptionsChunk(
                            rootArchive);


                byte[] stockDescriptionsXmb =
                    EraExtractionService.ExtractChunk(
                        rootArchive,
                        descriptionsChunk);


                // =========================================================
                // Prepare loose data directory.
                // =========================================================

                string dataDirectory =
                    Path.Combine(
                        gameDirectory,
                        "data");


                Directory.CreateDirectory(
                    dataDirectory);


                string loosePath =
                    Path.Combine(
                        dataDirectory,
                        "scenariodescriptions.xml");


                // IMPORTANT:
                //
                // Preserve the current modular ScenarioDescriptions document.
                // We no longer rebuild from stock on every export.
                //
                // This allows several custom maps to coexist.
                string? existingLooseXml =
                    File.Exists(
                        loosePath)
                        ? File.ReadAllText(
                            loosePath)
                        : null;


                // =========================================================
                // Find all Halo Wars DE language StringTables.
                // =========================================================

                StatusText.Text =
                    "Reading Halo Wars localization tables...";


                List<LocalizedStringTableSource>
                    stringTableSources =
                        StringTableService
                            .FindLocalizedStringTables(
                                rootArchive);


                Dictionary<string, byte[]>
                    stockStringTables =
                        new(
                            StringComparer.OrdinalIgnoreCase);


                Dictionary<string, string?>
                    existingLooseStringTables =
                        new(
                            StringComparer.OrdinalIgnoreCase);


                foreach (LocalizedStringTableSource source
                         in stringTableSources)
                {
                    byte[] stockXmb =
                        EraExtractionService.ExtractChunk(
                            rootArchive,
                            source.Chunk);


                    stockStringTables[
                        source.LanguageCode] =
                            stockXmb;


                    string looseTablePath =
                        Path.Combine(
                            dataDirectory,
                            source.LooseFileName);


                    existingLooseStringTables[
                        source.LanguageCode] =
                            File.Exists(
                                looseTablePath)
                                ? File.ReadAllText(
                                    looseTablePath)
                                : null;
                }


                // =========================================================
                // Load Ensemble map metadata.
                //
                // If the map has never had metadata before, generate sane
                // defaults from the ERA filename.
                // =========================================================

                StatusText.Text =
                    "Reading custom map metadata...";


                MapMetadata metadata =
                    _currentMapMetadata
                    ?? MapMetadataService.Load(
                        _currentArchive.FilePath)
                    ?? MapMetadataService.CreateDefault(
                        _currentArchive.FilePath);


                _currentMapMetadata =
                    metadata;


                MapMetadataService.Save(
                    _currentArchive.FilePath,
                    metadata);


                string displayName =
                    metadata.DisplayName
                        .Trim();


                if (string.IsNullOrWhiteSpace(
                        displayName))
                {
                    throw new InvalidDataException(
                        "The custom map Display Name is empty.\n\n" +
                        "Use Map > Map Metadata to set a name.");
                }


                string mapDescription =
                    string.IsNullOrWhiteSpace(
                        metadata.Description)
                        ? "Custom map created with Ensemble."
                        : metadata.Description
                            .Trim();


                // =========================================================
                // Check whether THIS map has already been installed.
                //
                // Existing custom NameStringID / InfoStringID values are
                // reused so re-exporting a map keeps a stable identity.
                // =========================================================

                ScenarioLocalizationIds existingIds =
                    ScenarioDescriptionsService
                        .GetExistingLocalizationIds(
                            existingLooseXml,
                            registrationFile);


                long? existingNameId =
                    existingIds.NameStringId;


                long? existingInfoId =
                    existingIds.InfoStringId;


                // =========================================================
                // Older Ensemble builds gave the custom map a custom
                // NameStringID but retained the stock map's InfoStringID.
                //
                // Never intentionally overwrite a retail StringTable entry.
                // =========================================================

                if (existingNameId.HasValue &&
                    !StringTableService.IsCustomStringId(
                        existingNameId.Value))
                {
                    existingNameId =
                        null;
                }


                if (existingInfoId.HasValue &&
                    !StringTableService.IsCustomStringId(
                        existingInfoId.Value))
                {
                    existingInfoId =
                        null;
                }


                // =========================================================
                // Allocate / reuse localization IDs.
                // =========================================================

                long customNameStringId =
                    existingNameId
                    ?? StringTableService
                        .FindFreeCustomStringId(
                            stockStringTables.Values,
                            existingLooseStringTables.Values);


                long customInfoStringId =
                    existingInfoId
                    ?? StringTableService
                        .FindFreeCustomStringId(
                            stockStringTables.Values,
                            existingLooseStringTables.Values,
                            new[]
                            {
                        customNameStringId
                            });


                // Name and description must never share an ID.
                if (customNameStringId ==
                    customInfoStringId)
                {
                    throw new InvalidDataException(
                        "The map NameStringID and InfoStringID are identical.\n\n" +
                        $"ID: {customNameStringId}");
                }


                // =========================================================
                // Generate every language-specific loose StringTable.
                //
                // Existing loose content is preserved.
                // =========================================================

                StatusText.Text =
                    "Building custom map localization...";


                Dictionary<string, string>
                    generatedStringTables =
                        new(
                            StringComparer.OrdinalIgnoreCase);


                foreach (LocalizedStringTableSource source
                         in stringTableSources)
                {
                    string generatedXml =
                        StringTableService
                            .BuildOrUpdateLooseStringTable(
                                stockStringTables[
                                    source.LanguageCode],

                                existingLooseStringTables[
                                    source.LanguageCode],

                                customNameStringId,
                                displayName);


                    // Feed the just-generated XML back into the service so
                    // the description is added to the same document.
                    generatedXml =
                        StringTableService
                            .BuildOrUpdateLooseStringTable(
                                stockStringTables[
                                    source.LanguageCode],

                                generatedXml,

                                customInfoStringId,
                                mapDescription);


                    generatedStringTables[
                        source.LanguageCode] =
                            generatedXml;
                }


                // =========================================================
                // Build / update modular ScenarioDescriptions.
                // =========================================================

                StatusText.Text =
                    "Registering custom scenario...";


                LooseScenarioRegistrationResult registration =
                    ScenarioDescriptionsService
                        .BuildOrUpdateLooseScenarioDescriptions(
                            stockDescriptionsXmb,
                            existingLooseXml,
                            registrationFile,
                            customNameStringId,
                            customInfoStringId);


                // =========================================================
                // Install custom ERA FIRST.
                //
                // ScenarioDescriptions is only replaced after the ERA has
                // been copied and verified successfully.
                // =========================================================

                string installedEraPath =
                    Path.Combine(
                        gameDirectory,
                        _currentArchive.FileName);


                if (!string.Equals(
                        _currentArchive.FilePath,
                        installedEraPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    string tempEraPath =
                        installedEraPath +
                        ".ensemble.tmp";


                    try
                    {
                        File.Copy(
                            _currentArchive.FilePath,
                            tempEraPath,
                            overwrite: true);


                        // -------------------------------------------------
                        // Verify copied ERA.
                        // -------------------------------------------------

                        EraArchiveInfo verificationArchive =
                            EraArchiveService.Open(
                                tempEraPath);


                        string expectedInternalScenario =
                            "scenario\\" +
                            registrationFile +
                            ".xmb";


                        string normalizedExpected =
                            expectedInternalScenario
                                .Replace(
                                    '/',
                                    '\\');


                        bool containsScenario =
                            verificationArchive
                                .Chunks
                                .Any(
                                    chunk =>
                                        string.Equals(
                                            chunk.FileName
                                                .Replace(
                                                    '/',
                                                    '\\'),
                                            normalizedExpected,
                                            StringComparison.OrdinalIgnoreCase));


                        if (!containsScenario)
                        {
                            throw new InvalidDataException(
                                "Custom ERA verification failed.\n\n" +

                                "The installed archive does not contain:\n\n" +

                                expectedInternalScenario);
                        }


                        if (File.Exists(
                                installedEraPath))
                        {
                            CreateSaveBackupIfNeeded(
                                installedEraPath);
                        }


                        File.Copy(
                            tempEraPath,
                            installedEraPath,
                            overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(
                                tempEraPath))
                        {
                            File.Delete(
                                tempEraPath);
                        }
                    }
                }
                else
                {
                    // -----------------------------------------------------
                    // ERA already lives in Halo Wars directory.
                    // Verify it anyway.
                    // -----------------------------------------------------

                    EraArchiveInfo verificationArchive =
                        EraArchiveService.Open(
                            installedEraPath);


                    string expectedInternalScenario =
                        "scenario\\" +
                        registrationFile +
                        ".xmb";


                    string normalizedExpected =
                        expectedInternalScenario
                            .Replace(
                                '/',
                                '\\');


                    bool containsScenario =
                        verificationArchive
                            .Chunks
                            .Any(
                                chunk =>
                                    string.Equals(
                                        chunk.FileName
                                            .Replace(
                                                '/',
                                                '\\'),
                                        normalizedExpected,
                                        StringComparison.OrdinalIgnoreCase));


                    if (!containsScenario)
                    {
                        throw new InvalidDataException(
                            "Custom ERA verification failed.\n\n" +

                            "The archive does not contain:\n\n" +

                            expectedInternalScenario);
                    }
                }


                // =========================================================
                // Install ALL language-specific loose StringTables.
                // =========================================================

                StatusText.Text =
                    "Installing custom map localization...";


                foreach (LocalizedStringTableSource source
                         in stringTableSources)
                {
                    string looseTablePath =
                        Path.Combine(
                            dataDirectory,
                            source.LooseFileName);


                    string backupTablePath =
                        Path.Combine(
                            dataDirectory,

                            Path.GetFileNameWithoutExtension(
                                source.LooseFileName)

                            + ".pre_ensemble_backup.xml");


                    // Preserve the state before Ensemble first touched it.
                    if (File.Exists(
                            looseTablePath) &&
                        !File.Exists(
                            backupTablePath))
                    {
                        File.Copy(
                            looseTablePath,
                            backupTablePath,
                            overwrite: false);
                    }


                    string tempTablePath =
                        looseTablePath +
                        ".ensemble.tmp";


                    try
                    {
                        string generatedXml =
                            generatedStringTables[
                                source.LanguageCode];


                        File.WriteAllText(
                            tempTablePath,

                            generatedXml,

                            new System.Text.UTF8Encoding(
                                encoderShouldEmitUTF8Identifier: false));


                        string verificationXml =
                            File.ReadAllText(
                                tempTablePath);


                        bool containsName =
                            StringTableService
                                .ContainsString(
                                    verificationXml,
                                    customNameStringId,
                                    displayName);


                        bool containsDescription =
                            StringTableService
                                .ContainsString(
                                    verificationXml,
                                    customInfoStringId,
                                    mapDescription);


                        if (!containsName ||
                            !containsDescription)
                        {
                            throw new InvalidDataException(
                                "Loose StringTable verification failed.\n\n" +

                                $"Language: {source.LanguageCode}\n" +
                                $"Map name: {displayName}\n" +
                                $"Name ID: {customNameStringId}\n" +
                                $"Info ID: {customInfoStringId}\n\n" +

                                $"Name present: {containsName}\n" +
                                $"Description present: {containsDescription}");
                        }


                        File.Copy(
                            tempTablePath,
                            looseTablePath,
                            overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(
                                tempTablePath))
                        {
                            File.Delete(
                                tempTablePath);
                        }
                    }


                    // Loose XML should remain newer than archived XMB.
                    File.SetLastWriteTimeUtc(
                        looseTablePath,
                        DateTime.UtcNow);
                }


                // =========================================================
                // Preserve pre-Ensemble ScenarioDescriptions once.
                // =========================================================

                string looseBackupPath =
                    Path.Combine(
                        dataDirectory,
                        "scenariodescriptions.pre_ensemble_backup.xml");


                if (File.Exists(
                        loosePath) &&
                    !File.Exists(
                        looseBackupPath))
                {
                    File.Copy(
                        loosePath,
                        looseBackupPath,
                        overwrite: false);
                }


                // =========================================================
                // Write ScenarioDescriptions LAST.
                // =========================================================

                StatusText.Text =
                    "Writing custom map registry...";


                string tempLoosePath =
                    loosePath +
                    ".ensemble.tmp";


                try
                {
                    File.WriteAllText(
                        tempLoosePath,

                        registration.Xml,

                        new System.Text.UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false));


                    string verificationXml =
                        File.ReadAllText(
                            tempLoosePath);


                    if (!ScenarioDescriptionsService
                            .ContainsScenarioFile(
                                verificationXml,
                                registrationFile))
                    {
                        throw new InvalidDataException(
                            "Loose ScenarioDescriptions verification failed.\n\n" +

                            "The custom map entry could not be found " +
                            "after writing the file.");
                    }


                    File.Copy(
                        tempLoosePath,
                        loosePath,
                        overwrite: true);
                }
                finally
                {
                    if (File.Exists(
                            tempLoosePath))
                    {
                        File.Delete(
                            tempLoosePath);
                    }
                }


                // Keep loose XML newer than archived XMB.
                File.SetLastWriteTimeUtc(
                    loosePath,
                    DateTime.UtcNow);


                // =========================================================
                // Success.
                // =========================================================

                string registryAction =
                    registration.Added
                        ? "Added new map entry"
                        : "Updated existing map entry";


                string duplicateText =
                    registration.RemovedDuplicateCount >
                        0
                        ? "\nRemoved duplicate entries: " +
                          $"{registration.RemovedDuplicateCount}\n"
                        : string.Empty;


                StatusText.Text =
                    $"Installed custom map: {displayName}";


                MessageBox.Show(
                    this,

                    "Custom map installed successfully.\n\n" +

                    $"EXE modding patch:\n" +
                    $"{patchStatus}\n\n" +

                    $"{registryAction}\n" +
                    $"Scenario entries: {registration.ScenarioCount}\n" +

                    duplicateText +

                    $"\nInstalled ERA:\n" +
                    $"{installedEraPath}\n\n" +

                    $"Scenario:\n" +
                    $"{registration.TargetScenarioFile}\n\n" +

                    $"Template:\n" +
                    $"{registration.TemplateScenarioFile}\n\n" +

                    $"Display name:\n" +
                    $"{displayName}\n\n" +

                    $"Description:\n" +
                    $"{mapDescription}\n\n" +

                    $"NameStringID:\n" +
                    $"{customNameStringId}\n\n" +

                    $"InfoStringID:\n" +
                    $"{customInfoStringId}\n\n" +

                    $"Localized tables installed:\n" +
                    $"{stringTableSources.Count}\n\n" +

                    $"Loose registry:\n" +
                    $"{loosePath}\n\n" +

                    "root.era was read only and was not modified.",

                    "Custom Map Installed",

                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),

                    "Custom Map Installation Failed",

                    MessageBoxButton.OK,
                    MessageBoxImage.Error);


                StatusText.Text =
                    "Custom map installation failed.";
            }
        }

        private static bool IsSafeScenarioBasename(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length >
                    64)
            {
                return false;
            }


            foreach (char c
                     in value)
            {
                if (!char.IsLetterOrDigit(
                        c) &&
                    c !=
                        '_' &&
                    c !=
                        '-')
                {
                    return false;
                }
            }


            return true;
        }

        private static string BuildDefaultMapDisplayName(
            string eraBasename)
        {
            string value =
                eraBasename
                    .Replace(
                        "_",
                        " ")
                    .Replace(
                        "-",
                        " - ");


            while (value.Contains(
                "  ",
                StringComparison.Ordinal))
            {
                value =
                    value.Replace(
                        "  ",
                        " ",
                        StringComparison.Ordinal);
            }


            return value
                .Trim()
                .ToUpperInvariant();
        }

        private string BuildManifestScenarioFile(
            string targetPath,
            bool renameScenarioCompanions)
        {
            if (_currentScenarioChunk ==
                null)
            {
                throw new InvalidOperationException(
                    "No scenario is loaded.");
            }


            string path =
                _currentScenarioChunk
                    .FileName
                    .Replace(
                        '/',
                        '\\');


            const string prefix =
                "scenario\\";

            if (path.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                path =
                    path[
                        prefix.Length..];
            }


            if (!path.EndsWith(
                    ".scn.xmb",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The current scenario does not end in .scn.xmb.");
            }


            if (renameScenarioCompanions)
            {
                int slash =
                    path.LastIndexOf(
                        '\\');


                string directory =
                    slash >= 0
                        ? path[
                            ..(slash + 1)]
                        : string.Empty;


                string newBasename =
                    Path.GetFileNameWithoutExtension(
                        targetPath);


                path =
                    directory +
                    newBasename +
                    ".scn.xmb";
            }


            // Manifest uses ScenarioDescriptions form:
            //
            // skirmish\design\blood_gulch\small_gulch.scn
            //
            // not scenario\... and not .xmb.
            return path[
                ..^4];
        }


        private static int ResolveManifestMaxPlayers(
            ScenarioMap map)
        {
            int starts =
                map.PlayerStarts.Count;


            if (starts is
                2 or 4 or 6)
            {
                return starts;
            }


            throw new InvalidDataException(
                "Ensemble cannot determine the Halo Wars " +
                "MaxPlayers bucket for this map.\n\n" +
                $"Player starts: {starts}\n\n" +
                "Supported values are 2, 4, or 6.");
        }

        private static void AddScenarioCompanionRename(
            EraArchiveInfo archive,
            Dictionary<int, string> renames,
            string directory,
            string oldBasename,
            string newBasename,
            string suffix,
            bool required)
        {
            string oldName =
                directory +
                oldBasename +
                suffix;


            EraChunkInfo? chunk =
                archive.Chunks
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.FileName,
                                oldName,
                                StringComparison.OrdinalIgnoreCase));


            if (chunk ==
                null)
            {
                if (required)
                {
                    throw new InvalidDataException(
                        "Required scenario archive file " +
                        "was not found:\n\n" +
                        oldName);
                }

                return;
            }


            renames[
                chunk.Index] =
                    directory +
                    newBasename +
                    suffix;
        }

        private static Dictionary<int, string>
            BuildScenarioCompanionRenames(
        EraArchiveInfo archive,
        EraChunkInfo scenarioChunk,
        string newBasename)
        {
            string sourceScenarioPath =
                scenarioChunk
                    .FileName
                    .Replace(
                        '/',
                        '\\');


            const string scnSuffix =
                ".scn.xmb";


            if (!sourceScenarioPath.EndsWith(
                    scnSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The current scenario filename does not " +
                    "end in .scn.xmb.");
            }


            int slash =
                sourceScenarioPath
                    .LastIndexOf(
                        '\\');


            string directory =
                slash >= 0
                    ? sourceScenarioPath[
                        ..(slash + 1)]
                    : string.Empty;


            string leaf =
                slash >= 0
                    ? sourceScenarioPath[
                        (slash + 1)..]
                    : sourceScenarioPath;


            string oldBasename =
                leaf[
                    ..^scnSuffix.Length];


            Dictionary<int, string> renames =
                new();


            // Saving under the same basename requires no
            // internal filename changes.
            if (string.Equals(
                    oldBasename,
                    newBasename,
                    StringComparison.OrdinalIgnoreCase))
            {
                return renames;
            }


            // Main scenario - mandatory.
            AddScenarioCompanionRename(
                archive,
                renames,
                directory,
                oldBasename,
                newBasename,
                ".scn.xmb",
                required: true);


            // Optional scenario companion data.
            AddScenarioCompanionRename(
                archive,
                renames,
                directory,
                oldBasename,
                newBasename,
                ".sc2.xmb",
                required: false);


            AddScenarioCompanionRename(
                archive,
                renames,
                directory,
                oldBasename,
                newBasename,
                ".sc3.xmb",
                required: false);


            // Halo Wars derives this directly from the
            // scenario basename, so it must match.
            AddScenarioCompanionRename(
                archive,
                renames,
                directory,
                oldBasename,
                newBasename,
                ".xsd",
                required: true);


            // Present on some scenarios/maps.
            AddScenarioCompanionRename(
                archive,
                renames,
                directory,
                oldBasename,
                newBasename,
                ".lrp",
                required: false);


            return renames;
        }

        // =========================================================
        // Terrain
        // ========================================================
        private void TerrainTexture_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainMode(
                Ensemble.Controls
                    .TerrainDisplayMode.Texture);
        }

        private void TerrainHeightMap_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainMode(
                Ensemble.Controls
                    .TerrainDisplayMode.HeightMap);
        }

        private void TerrainHidden_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainMode(
                Ensemble.Controls
                    .TerrainDisplayMode.Hidden);
        }

        private void SetTerrainMode(
            Ensemble.Controls.TerrainDisplayMode mode)
        {
            ScenarioMapCanvas
                .SetTerrainDisplayMode(
                    mode);

            TerrainTextureMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainDisplayMode.Texture;

            TerrainHeightMapMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainDisplayMode.HeightMap;

            TerrainHiddenMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainDisplayMode.Hidden;

            StatusText.Text =
                mode switch
                {
                    Ensemble.Controls.TerrainDisplayMode.Texture =>
                        "Terrain view: XTT texture.",

                    Ensemble.Controls.TerrainDisplayMode.HeightMap =>
                        "Terrain view: XTD heightmap.",

                    _ =>
                        "Terrain view hidden."
                };
        }

        private void TerrainSculptOff_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainSculptMode(
                Ensemble.Controls
                    .TerrainSculptMode.None);
        }


        private void TerrainSculptRaise_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainSculptMode(
                Ensemble.Controls
                    .TerrainSculptMode.Raise);
        }


        private void TerrainSculptLower_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainSculptMode(
                Ensemble.Controls
                    .TerrainSculptMode.Lower);
        }

        private void SetTerrainSculptMode(Ensemble.Controls.TerrainSculptMode mode)
        {
            if (mode !=
                Ensemble.Controls
                    .TerrainSculptMode.None)
            {
                // Sculpting is easiest to see against
                // the live XTD heightmap.
                SetTerrainMode(
                    Ensemble.Controls
                        .TerrainDisplayMode.HeightMap);
            }


            if (!ScenarioMapCanvas
                    .SetTerrainSculptMode(
                        mode))
            {
                MessageBox.Show(
                    this,
                    "No XTD terrain heightmap is currently loaded.",
                    "Terrain Sculpting",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }


            TerrainSculptOffMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainSculptMode.None;

            TerrainSculptRaiseMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainSculptMode.Raise;

            TerrainSculptLowerMenuItem.IsChecked =
                mode ==
                Ensemble.Controls
                    .TerrainSculptMode.Lower;


            StatusText.Text =
                mode switch
                {
                    Ensemble.Controls.TerrainSculptMode.Raise =>
                        "Terrain sculpt: Raise (preview only).",

                    Ensemble.Controls.TerrainSculptMode.Lower =>
                        "Terrain sculpt: Lower (preview only).",

                    _ =>
                        "Terrain sculpt disabled."
                };
        }

        private void SetTerrainRadius(
    float radius)
        {
            ScenarioMapCanvas
                .SetTerrainBrushRadius(
                    radius);

            TerrainRadius20MenuItem.IsChecked =
                radius ==
                20;

            TerrainRadius40MenuItem.IsChecked =
                radius ==
                40;

            TerrainRadius80MenuItem.IsChecked =
                radius ==
                80;

            StatusText.Text =
                $"Terrain brush radius: {radius:0}.";
        }


        private void TerrainRadius20_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainRadius(
                20);
        }

        private void TerrainRadius40_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainRadius(
                40);
        }

        private void TerrainRadius80_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainRadius(
                80);
        }

        private void SetTerrainStrength(
    float strength)
        {
            ScenarioMapCanvas
                .SetTerrainBrushStrength(
                    strength);

            TerrainStrength1MenuItem.IsChecked =
                strength ==
                1;

            TerrainStrength3MenuItem.IsChecked =
                strength ==
                3;

            TerrainStrength8MenuItem.IsChecked =
                strength ==
                8;

            StatusText.Text =
                $"Terrain brush strength: {strength:0.##}.";
        }


        private void TerrainStrength1_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainStrength(
                1);
        }

        private void TerrainStrength3_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainStrength(
                3);
        }

        private void TerrainStrength8_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetTerrainStrength(
                8);
        }

        private void TerrainUndoSculpt_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (ScenarioMapCanvas
                .UndoTerrainPreview())
            {
                StatusText.Text =
                    "Undid terrain sculpt preview.";
            }
            else
            {
                StatusText.Text =
                    "No terrain sculpt preview to undo.";
            }
        }


        private void TerrainRedoSculpt_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (ScenarioMapCanvas
                .RedoTerrainPreview())
            {
                StatusText.Text =
                    "Redid terrain sculpt preview.";
            }
            else
            {
                StatusText.Text =
                    "No terrain sculpt preview to redo.";
            }
        }


        private void TerrainResetSculpt_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (ScenarioMapCanvas
                .ResetTerrainPreview())
            {
                StatusText.Text =
                    "Terrain sculpt preview reset.";
            }
        }

        private void TerrainGrid_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScenarioMapCanvas.SetGridVisible(
                TerrainGridMenuItem.IsChecked);

            StatusText.Text =
                TerrainGridMenuItem.IsChecked
                    ? "Terrain grid visible."
                    : "Terrain grid hidden.";
        }

        private void TerrainOpacity50_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScenarioMapCanvas
                .SetTerrainOpacity(
                    0.50);

            StatusText.Text =
                "Terrain opacity: 50%.";
        }

        private void TerrainOpacity75_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScenarioMapCanvas
                .SetTerrainOpacity(
                    0.75);

            StatusText.Text =
                "Terrain opacity: 75%.";
        }

        private void TerrainOpacity100_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScenarioMapCanvas
                .SetTerrainOpacity(
                    1.0);

            StatusText.Text =
                "Terrain opacity: 100%.";
        }

        // =========================================================
        // File - Save / Save As
        // ========================================================

        private void Save_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaveCurrentDocument();
        }

        private bool SaveCurrentDocument()
        {
            if (string.IsNullOrWhiteSpace(
                    _currentSavePath))
            {
                return SaveCurrentDocumentAs();
            }

            return SaveModifiedEraToPath(
                _currentSavePath,
                showSuccessDialog: false,
                renameScenarioCompanions: false);
        }

        private void SaveAs_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaveCurrentDocumentAs();
        }

        private bool SaveCurrentDocumentAs()
        {
            if (_currentArchive == null)
                return false;

            string sourceName =
                System.IO.Path
                    .GetFileNameWithoutExtension(
                        _currentArchive.FileName);

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title =
                        "Save Modified Halo Wars ERA",

                    FileName =
                        $"{sourceName}_ensemble.era",

                    Filter =
                        "Halo Wars ERA (*.era)|*.era|" +
                        "All Files (*.*)|*.*"
                };

            if (dialog.ShowDialog(this) !=
                true)
            {
                return false;
            }

            string targetPath =
                dialog.FileName;

            // Extra protection against accidentally destroying
            // the shipping archive.
            if (string.Equals(
                    targetPath,
                    _currentArchive.FilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBoxResult result =
                    MessageBox.Show(
                        this,

                        "You are about to overwrite the ERA that " +
                        "Ensemble originally opened.\n\n" +

                        "A backup will be created first.\n\n" +

                        "Continue?",

                        "Overwrite Source ERA?",

                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (result !=
                    MessageBoxResult.Yes)
                {
                    return false;
                }
            }

            return SaveModifiedEraToPath(
                targetPath,
                showSuccessDialog: true,
                renameScenarioCompanions: true);
        }

        private bool SaveModifiedEraToPath(
            string targetPath,
            bool showSuccessDialog,
            bool renameScenarioCompanions)
        {
            if (_currentArchive == null ||
                _currentScenarioChunk == null ||
                _currentScenarioOriginalXmbData == null ||
                ScenarioMapCanvas.Scenario == null)
            {
                return false;
            }


            string tempPath =
                targetPath +
                ".ensemble.tmp";


            try
            {
                // =========================================================
                // SCENARIO
                // =========================================================

                StatusText.Text =
                    "Building modified scenario...";


                ScenarioMap expected =
                    ScenarioMapCanvas.Scenario;


                bool hadTerrainChanges =
                    ScenarioMapCanvas
                        .HasTerrainPreviewChanges;


                TerrainHeightMap? expectedTerrain =
                    ScenarioMapCanvas
                        .TerrainHeightMap;


                byte[]? modifiedXtd =
                    null;


                byte[]? modifiedXsd =
                    null;


                // =========================================================
                // TERRAIN
                // =========================================================

                if (hadTerrainChanges)
                {
                    if (_currentTerrainChunk ==
                            null ||
                        _currentTerrainOriginalXtdData ==
                            null ||
                        expectedTerrain ==
                            null)
                    {
                        throw new InvalidDataException(
                            "Terrain was edited, but Ensemble no longer " +
                            "has the source XTD required to save it.");
                    }


                    StatusText.Text =
                        "Encoding modified XTD terrain...";


                    modifiedXtd =
                        TerrainXtdService.WriteHeights(
                            _currentTerrainOriginalXtdData,
                            expectedTerrain);


                    if (_currentSimulationChunk ==
                            null ||
                        _currentSimulationOriginalXsdData ==
                            null)
                    {
                        throw new InvalidDataException(
                            "Terrain was sculpted, but this map's XSD " +
                            "simulation file could not be found.\n\n" +

                            "Ensemble will not save visual terrain without " +
                            "also updating gameplay terrain.");
                    }


                    TerrainHeightMap originalTerrain =
                        TerrainXtdService.Read(
                            _currentTerrainOriginalXtdData);


                    StatusText.Text =
                        "Synchronizing XSD simulation terrain...";


                    modifiedXsd =
                        TerrainXsdService
                            .WriteSynchronizedHeights(
                                _currentSimulationOriginalXsdData,
                                originalTerrain,
                                expectedTerrain);


                    TerrainHeightMap encodedTerrain =
                        TerrainXtdService.Read(
                            modifiedXtd);


                    ValidateTerrainRoundTrip(
                        expectedTerrain,
                        encodedTerrain);
                }


                // =========================================================
                // STRUCTURAL CHANGE STATE
                // =========================================================

                bool hadStructuralChanges =
                    expected.Objects.Any(
                        x =>
                            x.IsNewObject
                            ||
                            !string.Equals(
                                x.EditorName,
                                x.OriginalEditorName,
                                StringComparison.Ordinal))
                    ||
                    expected.DeletedObjectIds.Count >
                        0
                    ||
                    expected.Paths.Any(
                        x =>
                            x.HasPointChanges);


                // =========================================================
                // BUILD SCENARIO XMB
                // =========================================================

                byte[] modifiedXmb =
                    XmbDocumentService.WriteScenario(
                        _currentScenarioOriginalXmbData,
                        expected);


                // =========================================================
                // BUILD REPLACEMENTS
                // =========================================================

                StatusText.Text =
                    "Rebuilding and encrypting ERA...";


                Dictionary<int, byte[]> replacements =
                    new()
                    {
                        [_currentScenarioChunk.Index] =
                            modifiedXmb
                    };


                if (modifiedXtd !=
                        null &&
                    _currentTerrainChunk !=
                        null)
                {
                    replacements[
                        _currentTerrainChunk.Index] =
                            modifiedXtd;
                }


                if (modifiedXsd !=
                        null &&
                    _currentSimulationChunk !=
                        null)
                {
                    replacements[
                        _currentSimulationChunk.Index] =
                            modifiedXsd;
                }


                // =========================================================
                // SAVE AS:
                // Rename scenario-derived companion files.
                // =========================================================

                Dictionary<int, string> fileRenames =
                    new();


                if (renameScenarioCompanions)
                {
                    string targetBasename =
                        Path.GetFileNameWithoutExtension(
                            targetPath)
                        .Trim();


                    if (!IsSafeScenarioBasename(
                            targetBasename))
                    {
                        throw new InvalidDataException(
                            "The ERA filename cannot be used as a Halo Wars " +
                            "scenario basename.\n\n" +

                            "Use only letters, numbers, underscores and hyphens.");
                    }


                    fileRenames =
                        BuildScenarioCompanionRenames(
                            _currentArchive,
                            _currentScenarioChunk,
                            targetBasename);
                }


                // =========================================================
                // BUILD NORMAL HALO WARS ERA
                // =========================================================

                byte[] modifiedEra =
                    EraRebuildService.BuildModifiedEra(
                        _currentArchive,
                        replacements,
                        fileRenames);


                // =========================================================
                // BUILD ENSEMBLE EMBEDDED MAP METADATA
                //
                // New Save As:
                // use metadata already edited by the user if present,
                // otherwise generate it from the new ERA name.
                //
                // Existing ERA:
                // metadata may already have been loaded from ENSMAP1.
                //
                // Old Ensemble maps:
                // MapMetadataService remains a migration fallback.
                // =========================================================

                MapMetadata metadata =
                    _currentMapMetadata
                    ?? MapMetadataService.Load(
                        _currentArchive.FilePath)
                    ?? MapMetadataService.CreateDefault(
                        targetPath);


                _currentMapMetadata =
                    metadata;


                string displayName =
                    metadata.DisplayName?
                        .Trim()
                    ?? string.Empty;


                if (string.IsNullOrWhiteSpace(
                        displayName))
                {
                    throw new InvalidDataException(
                        "The map Display Name is empty.\n\n" +
                        "Use Map > Map Metadata to enter a map name.");
                }


                string description =
                    metadata.Description?
                        .Trim()
                    ?? string.Empty;


                // =========================================================
                // DETERMINE MANIFEST SCENARIO PATH
                //
                // Archive path:
                //
                // scenario\skirmish\design\blood_gulch\
                // small_gulch.scn.xmb
                //
                // Manifest path:
                //
                // skirmish\design\blood_gulch\
                // small_gulch.scn
                // =========================================================

                string manifestScenarioPath =
                    _currentScenarioChunk
                        .FileName
                        .Replace(
                            '/',
                            '\\');


                const string scenarioPrefix =
                    "scenario\\";


                if (manifestScenarioPath.StartsWith(
                        scenarioPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    manifestScenarioPath =
                        manifestScenarioPath[
                            scenarioPrefix.Length..];
                }


                if (!manifestScenarioPath.EndsWith(
                        ".scn.xmb",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The currently loaded scenario filename does not " +
                        "end in .scn.xmb.\n\n" +

                        manifestScenarioPath);
                }


                // Save As changes the internal scenario basename.
                if (renameScenarioCompanions)
                {
                    int slash =
                        manifestScenarioPath
                            .LastIndexOf(
                                '\\');


                    string directory =
                        slash >=
                            0
                            ? manifestScenarioPath[
                                ..(slash + 1)]
                            : string.Empty;


                    string targetBasename =
                        Path.GetFileNameWithoutExtension(
                            targetPath)
                        .Trim();


                    manifestScenarioPath =
                        directory +
                        targetBasename +
                        ".scn.xmb";
                }


                // Remove ".xmb", leaving ".scn".
                manifestScenarioPath =
                    manifestScenarioPath[
                        ..^4];


                // =========================================================
                // MAX PLAYERS
                //
                // Halo Wars' ScenarioInfo uses the 2 / 4 / 6-player
                // buckets.
                // =========================================================

                int manifestMaxPlayers =
                    expected.PlayerStarts.Count;


                if (manifestMaxPlayers is not
                    (2 or 4 or 6))
                {
                    throw new InvalidDataException(
                        "Ensemble cannot determine the Halo Wars " +
                        "MaxPlayers value for this scenario.\n\n" +

                        $"Player starts found: {manifestMaxPlayers}\n\n" +

                        "Supported ScenarioInfo values are 2, 4, and 6.");
                }


                // =========================================================
                // CREATE ENSMAP1 MANIFEST
                //
                // Preserve optional values if this ERA already had them.
                // =========================================================

                EraManifestFooterService.Manifest manifest =
                    new EraManifestFooterService.Manifest
                    {
                        ScenarioFile =
                            manifestScenarioPath,

                        DisplayName =
                            displayName,

                        Description =
                            description,

                        MaxPlayers =
                            manifestMaxPlayers,

                        LoadingScreen =
                            _currentEraManifest?
                                .LoadingScreen
                            ?? string.Empty,

                        MapName =
                            _currentEraManifest?
                                .MapName
                            ?? string.Empty
                    };


                // =========================================================
                // ATTACH MANIFEST TO ERA
                //
                // The service writes ENSMAP1 into the reserved trailing
                // ERA footer area without changing archive length.
                // =========================================================

                StatusText.Text =
                    "Embedding modular map manifest...";


                modifiedEra =
                    EraManifestFooterService.Attach(
                        modifiedEra,
                        manifest);


                // =========================================================
                // WRITE TEMP FILE FIRST
                // =========================================================

                File.WriteAllBytes(
                    tempPath,
                    modifiedEra);


                // =========================================================
                // VERIFY ENSMAP1 FOOTER
                // =========================================================

                StatusText.Text =
                    "Verifying embedded map manifest...";


                EraManifestFooterService.Manifest?
                    verificationManifest =
                        EraManifestFooterService.TryRead(
                            tempPath);


                if (verificationManifest ==
                    null)
                {
                    throw new InvalidDataException(
                        "Saved ERA does not contain a valid " +
                        "Ensemble ENSMAP1 manifest.");
                }


                if (!string.Equals(
                        verificationManifest.ScenarioFile,
                        manifest.ScenarioFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "ERA manifest ScenarioFile failed verification.\n\n" +

                        $"Expected:\n{manifest.ScenarioFile}\n\n" +

                        $"Actual:\n{verificationManifest.ScenarioFile}");
                }


                if (!string.Equals(
                        verificationManifest.DisplayName,
                        manifest.DisplayName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "ERA manifest DisplayName failed verification.");
                }


                if (!string.Equals(
                        verificationManifest.Description,
                        manifest.Description,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "ERA manifest Description failed verification.");
                }


                if (verificationManifest.MaxPlayers !=
                    manifest.MaxPlayers)
                {
                    throw new InvalidDataException(
                        "ERA manifest MaxPlayers failed verification.");
                }


                // =========================================================
                // VERIFY NORMAL ERA CONTENTS
                // =========================================================

                StatusText.Text =
                    "Verifying saved ERA...";


                EraArchiveInfo verificationArchive =
                    EraArchiveService.Open(
                        tempPath);


                if (_currentScenarioChunk.Index >=
                    verificationArchive.Chunks.Count)
                {
                    throw new InvalidDataException(
                        "Saved ERA lost the scenario chunk.");
                }


                EraChunkInfo verificationChunk =
                    verificationArchive.Chunks[
                        _currentScenarioChunk.Index];


                byte[] verificationXmb =
                    EraExtractionService.ExtractChunk(
                        verificationArchive,
                        verificationChunk);


                string verificationXml =
                    XmbDocumentService.Read(
                        verificationXmb);


                ScenarioMap verificationScenario =
                    ScenarioParserService.Parse(
                        verificationXml);


                ValidateScenarioRoundTrip(
                    expected,
                    verificationScenario);


                // =========================================================
                // VERIFY TERRAIN
                // =========================================================

                if (hadTerrainChanges)
                {
                    if (_currentTerrainChunk ==
                            null ||
                        expectedTerrain ==
                            null)
                    {
                        throw new InvalidDataException(
                            "Terrain verification state is missing.");
                    }


                    if (_currentTerrainChunk.Index >=
                        verificationArchive.Chunks.Count)
                    {
                        throw new InvalidDataException(
                            "Saved ERA lost the terrain XTD chunk.");
                    }


                    EraChunkInfo verificationTerrainChunk =
                        verificationArchive.Chunks[
                            _currentTerrainChunk.Index];


                    byte[] verificationXtd =
                        EraExtractionService.ExtractChunk(
                            verificationArchive,
                            verificationTerrainChunk);


                    if (modifiedXsd !=
                            null &&
                        _currentSimulationChunk !=
                            null)
                    {
                        if (_currentSimulationChunk.Index >=
                            verificationArchive.Chunks.Count)
                        {
                            throw new InvalidDataException(
                                "Saved ERA lost the simulation XSD chunk.");
                        }


                        EraChunkInfo verificationSimulationChunk =
                            verificationArchive.Chunks[
                                _currentSimulationChunk.Index];


                        byte[] verificationXsd =
                            EraExtractionService.ExtractChunk(
                                verificationArchive,
                                verificationSimulationChunk);


                        if (!verificationXsd
                                .AsSpan()
                                .SequenceEqual(
                                    modifiedXsd))
                        {
                            throw new InvalidDataException(
                                "XSD simulation terrain failed " +
                                "ERA round-trip verification.");
                        }


                        // Also prove the resulting XSD is parseable.
                        TerrainXsdService.Read(
                            verificationXsd,
                            expectedTerrain);
                    }


                    TerrainHeightMap verificationTerrain =
                        TerrainXtdService.Read(
                            verificationXtd);


                    ValidateTerrainRoundTrip(
                        expectedTerrain,
                        verificationTerrain);
                }


                // =========================================================
                // COMMIT VERIFIED FILE
                // =========================================================

                CreateSaveBackupIfNeeded(
                    targetPath);


                File.Copy(
                    tempPath,
                    targetPath,
                    overwrite: true);


                File.Delete(
                    tempPath);


                // =========================================================
                // REOPEN SAVED ERA
                //
                // This becomes the new working baseline for future Ctrl+S.
                // =========================================================

                EraArchiveInfo savedArchive =
                    EraArchiveService.Open(
                        targetPath);


                if (_currentScenarioChunk.Index >=
                    savedArchive.Chunks.Count)
                {
                    throw new InvalidDataException(
                        "Saved ERA lost the scenario chunk after reopening.");
                }


                EraChunkInfo savedScenarioChunk =
                    savedArchive.Chunks[
                        _currentScenarioChunk.Index];


                byte[] savedScenarioXmb =
                    EraExtractionService.ExtractChunk(
                        savedArchive,
                        savedScenarioChunk);


                _currentArchive =
                    savedArchive;


                _currentScenarioChunk =
                    savedScenarioChunk;


                _currentScenarioOriginalXmbData =
                    savedScenarioXmb;


                // Manifest is now canonical metadata stored inside ERA.
                _currentEraManifest =
                    verificationManifest;


                _currentMapMetadata =
                    new MapMetadata
                    {
                        DisplayName =
                            verificationManifest.DisplayName,

                        Description =
                            verificationManifest.Description
                    };


                // =========================================================
                // REFRESH TERRAIN BASELINES
                // =========================================================

                if (_currentTerrainChunk !=
                    null)
                {
                    if (_currentTerrainChunk.Index >=
                        savedArchive.Chunks.Count)
                    {
                        throw new InvalidDataException(
                            "Saved ERA lost the terrain chunk after reopening.");
                    }


                    EraChunkInfo savedTerrainChunk =
                        savedArchive.Chunks[
                            _currentTerrainChunk.Index];


                    byte[] savedTerrainXtd =
                        EraExtractionService.ExtractChunk(
                            savedArchive,
                            savedTerrainChunk);


                    _currentTerrainChunk =
                        savedTerrainChunk;


                    _currentTerrainOriginalXtdData =
                        savedTerrainXtd;


                    if (_currentSimulationChunk !=
                        null)
                    {
                        if (_currentSimulationChunk.Index >=
                            savedArchive.Chunks.Count)
                        {
                            throw new InvalidDataException(
                                "Saved ERA lost the simulation chunk after reopening.");
                        }


                        EraChunkInfo savedSimulationChunk =
                            savedArchive.Chunks[
                                _currentSimulationChunk.Index];


                        byte[] savedSimulationXsd =
                            EraExtractionService.ExtractChunk(
                                savedArchive,
                                savedSimulationChunk);


                        _currentSimulationChunk =
                            savedSimulationChunk;


                        _currentSimulationOriginalXsdData =
                            savedSimulationXsd;
                    }


                    if (hadTerrainChanges)
                    {
                        TerrainHeightMap savedTerrain =
                            TerrainXtdService.Read(
                                savedTerrainXtd);


                        ScenarioMapCanvas
                            .SetTerrainHeightMap(
                                savedTerrain);
                    }
                }


                // =========================================================
                // ACCEPT CURRENT STRUCTURAL STATE AS SAVED BASELINE
                // =========================================================

                foreach (ScenarioObject obj
                         in expected.Objects)
                {
                    obj.IsNewObject =
                        false;


                    obj.SourceObjectId =
                        obj.Id;


                    obj.OriginalEditorName =
                        obj.EditorName;
                }


                expected.DeletedObjectIds.Clear();


                foreach (ScenarioPath path
                         in expected.Paths)
                {
                    path.AcceptPointChangesAsBaseline();
                }


                // =========================================================
                // HISTORY
                // =========================================================

                if (hadStructuralChanges)
                {
                    // Structural XMX topology has changed, therefore old
                    // structural undo actions are no longer valid.

                    _undoStack.Clear();


                    _redoStack.Clear();


                    _nextRevisionId =
                        0;


                    _currentRevisionId =
                        0;


                    _savedRevisionId =
                        0;


                    UpdateUndoRedoUi();
                }
                else
                {
                    _savedRevisionId =
                        _currentRevisionId;
                }


                // =========================================================
                // UPDATE DOCUMENT STATE
                // =========================================================

                _currentSavePath =
                    targetPath;


                _savedRevisionId =
                    _currentRevisionId;


                UpdateDirtyState();


                // Saved archive has new offsets/sizes/checksums/hashes.
                // Rebuild TreeView tags from the newly reopened archive.
                BuildArchiveTree();


                ShowArchiveInformation();


                StatusText.Text =
                    $"Saved {Path.GetFileName(targetPath)} " +
                    "with embedded ENSMAP1 manifest.";


                // =========================================================
                // SUCCESS
                // =========================================================

                if (showSuccessDialog)
                {
                    MessageBox.Show(
                        this,

                        "Halo Wars ERA saved successfully.\n\n" +

                        "Ensemble rebuilt, encrypted, reopened and " +
                        "verified the archive.\n\n" +

                        "Embedded map manifest:\n" +

                        $"Display name: {manifest.DisplayName}\n" +
                        $"Scenario: {manifest.ScenarioFile}\n" +
                        $"Max players: {manifest.MaxPlayers}\n\n" +

                        "This ERA now contains its Ensemble map metadata.",

                        "Save Complete",

                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }


                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(
                            tempPath))
                    {
                        File.Delete(
                            tempPath);
                    }
                }
                catch
                {
                    // Do not mask the original save error.
                }


                MessageBox.Show(
                    this,
                    ex.ToString(),

                    "Save Failed",

                    MessageBoxButton.OK,
                    MessageBoxImage.Error);


                StatusText.Text =
                    "Save failed.";


                return false;
            }
        }

        private static void CreateSaveBackupIfNeeded(
            string targetPath)
        {
            if (!File.Exists(
                    targetPath))
            {
                return;
            }

            string directory =
                System.IO.Path.GetDirectoryName(
                    targetPath)
                ?? string.Empty;

            string name =
                System.IO.Path.GetFileNameWithoutExtension(
                    targetPath);

            string extension =
                System.IO.Path.GetExtension(
                    targetPath);

            string backupPath =
                System.IO.Path.Combine(
                    directory,
                    $"{name}.pre_ensemble_backup{extension}");

            // Preserve the FIRST version Ensemble overwrote.
            // Do not destroy it on subsequent Ctrl+S saves.
            if (!File.Exists(
                    backupPath))
            {
                File.Copy(
                    targetPath,
                    backupPath,
                    overwrite: false);
            }
        }

        // =========================================================
        // View 
        // =========================================================
        private void FitMap_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScenarioMapCanvas.FitMapView();

            StatusText.Text =
                "Map view reset.";
        }

        // =========================================================
        // Add Object
        // =========================================================

        private void AddObject_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenAddObjectDialog();
        }

        private void OpenAddObjectDialog()
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            if (map.Objects.Count ==
                0)
            {
                MessageBox.Show(
                    this,
                    "This scenario does not contain any existing " +
                    "objects that can be used as templates.",
                    "No Object Templates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            AddObjectWindow dialog =
                new AddObjectWindow(
                    map.Objects)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog() !=
                true)
            {
                return;
            }

            if (dialog.SelectedTemplate
                is not ScenarioObject source)
            {
                return;
            }

            _pendingAddTemplate = source;

            SetTerrainSculptMode(Ensemble.Controls.TerrainSculptMode.None);

            ScenarioMapCanvas.BeginObjectPlacement(
                source.Type);

            StatusText.Text =
                $"Click the map to place {source.Type}. " +
                "Right-click or press Esc to cancel.";
        }

        private void AddObjectFromTemplate(
            ScenarioObject source,
            float worldX,
            float worldZ)
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            int newId =
                ++map.MaxKnownId;

            // Structural XMX must always ultimately clone an
            // original persisted source node.
            int templateId =
                source.IsNewObject
                    ? source.SourceObjectId
                    : source.Id;

            ScenarioObject added =
                new ScenarioObject
                {
                    Id =
                        newId,

                    IsNewObject =
                        true,

                    SourceObjectId =
                        templateId,

                    IsSquad =
                        source.IsSquad,

                    Player =
                        source.Player,

                    TintValue =
                        source.TintValue,

                    EditorName =
                        source.EditorName,

                    Type =
                        source.Type,

                    // Preserve the template's Y value because we
                    // do not yet sample actual terrain height.
                    Position =
                    new Vector3(
                        worldX,
                        source.Position.Y,
                        worldZ),

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
                added.Flags.Add(
                    flag);
            }

            ScenarioMapCanvas
                .AddScenarioObjectFromEditor(
                    added);
        }

        private void ScenarioMapCanvas_PlacementRequested(
            object? sender,
            Ensemble.Controls.ScenarioPlacementRequestedEventArgs e)
        {
            if (_pendingAddTemplate
                is not ScenarioObject source)
            {
                return;
            }

            _pendingAddTemplate =
                null;

            AddObjectFromTemplate(
                source,
                e.WorldX,
                e.WorldZ);
        }

        // =========================================================
        // Delete
        // =========================================================

        private void DeleteObject_Click(
            object sender,
            RoutedEventArgs e)
        {
            DeleteSelectedObject();
        }

        private void DeleteSelectedObject()
        {
            if (_selectedScenarioItem
                is not ScenarioObject obj)
            {
                return;
            }

            ScenarioMapCanvas
                .DeleteScenarioObjectFromEditor(
                    obj);
        }

        private void ScenarioMapCanvas_ObjectDeleted(
            object? sender,
            Ensemble.Controls.ScenarioObjectDeletedEventArgs e)
        {
            long beforeRevision =
                _currentRevisionId;

            long afterRevision =
                ++_nextRevisionId;

            _undoStack.Push(
                new DeleteObjectHistoryAction(
                    e.Object,
                    e.WasNewObject,
                    beforeRevision,
                    afterRevision));

            _redoStack.Clear();

            _currentRevisionId =
                afterRevision;

            UpdateUndoRedoUi();

            UpdateDirtyState();

            StatusText.Text =
                $"Deleted {e.Object.EditorName} | " +
                $"ID {e.Object.Id}";
        }

        // =========================================================
        // Map
        // =========================================================
        private void MapMetadata_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive ==
                    null ||
                _currentScenarioChunk ==
                    null)
            {
                return;
            }


            MapMetadata metadata =
                _currentMapMetadata
                ?? MapMetadataService.CreateDefault(
                    _currentArchive.FilePath);


            MapMetadataWindow dialog =
                new MapMetadataWindow(
                    _currentArchive.FileName,
                    metadata)
                {
                    Owner =
                        this
                };


            if (dialog.ShowDialog() !=
                    true ||
                dialog.Metadata ==
                    null)
            {
                return;
            }


            _currentMapMetadata =
                dialog.Metadata;


            MapMetadataService.Save(
                _currentArchive.FilePath,
                _currentMapMetadata);


            StatusText.Text =
                $"Map metadata updated: " +
                $"{_currentMapMetadata.DisplayName}";
        }

        // =========================================================
        // Help
        // =======================================================
        private void LinkToSource_Click(object sender, RoutedEventArgs e)
        {

        }

        // =========================================================
        // Exit
        // ========================================================

        private void Exit_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}