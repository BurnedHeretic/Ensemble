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

        private readonly Stack<IScenarioHistoryAction>
            _undoStack = new();

        private readonly Stack<IScenarioHistoryAction>
            _redoStack = new();

        private byte[]?
            _currentScenarioOriginalXmbData;

        private EraChunkInfo?
            _currentScenarioChunk;

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

        private void LoadEra(
            string filePath)
        {
            StatusText.Text =
                "Decrypting and reading ERA...";

            _currentArchive =
                EraArchiveService.Open(
                    filePath);

            SaveMenuItem.IsEnabled =
                false;

            SaveAsMenuItem.IsEnabled =
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
                is not EraChunkInfo chunk)
                return;

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
                int oldPlayer,
                int oldGroup,
                int oldVariation,
                int newPlayer,
                int newGroup,
                int newVariation,
                long beforeRevisionId,
                long afterRevisionId)
            {
                Object =
                    obj;

                OldPlayer =
                    oldPlayer;

                OldGroup =
                    oldGroup;

                OldVariation =
                    oldVariation;

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

            public int OldPlayer { get; }

            public int OldGroup { get; }

            public int OldVariation { get; }

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
                    OldPlayer,
                    OldGroup,
                    OldVariation);
            }

            public void Redo(
                Ensemble.Controls.MapCanvas canvas)
            {
                canvas.ApplyHistoryObjectProperties(
                    Object,
                    NewPlayer,
                    NewGroup,
                    NewVariation);
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

            if (item.Tag is not EraChunkInfo chunk)
                return;

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

                    ScenarioMap map =
                        ScenarioParserService.Parse(
                            xmlText);

                    ScenarioMapCanvas.SetMap(
                        map);

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
                        $"{map.Paths.Count} design paths";
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

                StatusText.Text =
                    $"Decoded XMB: {chunk.FileName}";
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
                _savedRevisionId;

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
                $"Points: {path.Points.Count}";
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
                    e.OldPlayer,
                    e.OldGroup,
                    e.OldVisualVariationIndex,
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
                        "All Files (*.*)|*.*"
                };

            if (dialog.ShowDialog(this) !=
                true)
            {
                return;
            }

            MessageBoxResult confirmation =
                MessageBox.Show(
                    this,

                    "Ensemble will patch this Halo Wars executable " +
                    "so the game can load unofficial / modified ERA archives.\n\n" +

                    "A backup of the executable will be created first.\n\n" +

                    "Continue?",

                    "Patch Halo Wars Executable",

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
                    "Patching Halo Wars executable...";

                HaloWarsExePatchResult result =
                    HaloWarsExePatchService.Patch(
                        dialog.FileName);

                if (result.AlreadyPatched)
                {
                    MessageBox.Show(
                        this,

                        "This Halo Wars executable already appears " +
                        "to be patched for modified ERA archives.",

                        "Executable Already Patched",

                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    StatusText.Text =
                        "Halo Wars executable is already patched.";

                    return;
                }

                MessageBox.Show(
                    this,

                    "Halo Wars executable patched successfully.\n\n" +

                    $"Patch offset: 0x{result.PatchOffset:X}\n\n" +

                    $"Backup:\n{result.BackupPath}\n\n" +

                    "You can now test Ensemble-generated ERA files.",

                    "Halo Wars EXE Patched",

                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText.Text =
                    "Halo Wars executable patched successfully.";
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
                    "Halo Wars executable patch failed.";
            }
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
                showSuccessDialog: false);
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
                showSuccessDialog: true);
        }

        private bool SaveModifiedEraToPath(
            string targetPath,
            bool showSuccessDialog)
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
                StatusText.Text =
                    "Building modified scenario...";

                ScenarioMap expected =
                    ScenarioMapCanvas.Scenario;

                byte[] modifiedXmb =
                    XmbDocumentService.WriteScenario(
                        _currentScenarioOriginalXmbData,
                        expected);

                StatusText.Text =
                    "Rebuilding and encrypting ERA...";

                byte[] modifiedEra =
                    EraRebuildService.BuildModifiedEra(
                        _currentArchive,
                        _currentScenarioChunk,
                        modifiedXmb);

                // -----------------------------------------------------
                // Write to TEMP first.
                //
                // We never overwrite a working ERA until the newly
                // generated archive has passed Ensemble's verification.
                // -----------------------------------------------------

                File.WriteAllBytes(
                    tempPath,
                    modifiedEra);

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

                // -----------------------------------------------------
                // New archive is now proven readable.
                // Only now do we touch the destination.
                // -----------------------------------------------------

                CreateSaveBackupIfNeeded(
                    targetPath);

                File.Copy(
                    tempPath,
                    targetPath,
                    overwrite: true);

                File.Delete(
                    tempPath);

                // -----------------------------------------------------
                // The saved ERA now becomes our new document base.
                // Future Ctrl+S operations build from this version.
                // -----------------------------------------------------

                EraArchiveInfo savedArchive =
                    EraArchiveService.Open(
                        targetPath);

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

                _currentSavePath =
                    targetPath;

                _savedRevisionId =
                    _currentRevisionId;

                UpdateDirtyState();

                ShowArchiveInformation();

                StatusText.Text =
                    $"Saved {System.IO.Path.GetFileName(targetPath)}";

                if (showSuccessDialog)
                {
                    MessageBox.Show(
                        this,

                        "Halo Wars ERA saved successfully.\n\n" +
                        "Ensemble rebuilt, encrypted, reopened and " +
                        "verified the saved archive.",

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
                    // Don't mask the real save error.
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