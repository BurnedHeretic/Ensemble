using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Input;
using System.Globalization;
using System.IO;
using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;

namespace Ensemble
{
    public partial class MainWindow : Window
    {
        private EraArchiveInfo? _currentArchive;

        private bool _isDirty;

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

            PreviewKeyDown +=
                MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(
    object sender,
    KeyEventArgs e)
        {
            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.Z)
            {
                UndoLastMove();

                e.Handled =
                    true;

                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.Y)
            {
                RedoLastMove();

                e.Handled =
                    true;
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

            _undoStack.Clear();

            _redoStack.Clear();

            UpdateUndoRedoUi();

            SetDirty(
                false);

            _currentScenarioOriginalXmbData =
                null;

            _currentScenarioChunk =
                null;

            ExportScenarioXmbMenuItem.IsEnabled =
                false;

            ExportModifiedEraMenuItem.IsEnabled =
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

            UpdateUndoRedoUi();

            SetDirty(
                _undoStack.Count > 0);

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

            UpdateUndoRedoUi();

            SetDirty(
                true);

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
                Vector3 newPosition)
            {
                Item =
                    item;

                OldPosition =
                    oldPosition;

                NewPosition =
                    newPosition;
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
                Vector3 newRight)
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

                    ExportModifiedEraMenuItem.IsEnabled =
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
            }
        }

        private void ExportModifiedEra_Click(
            object sender,RoutedEventArgs e)
        {
            if (_currentArchive == null ||
                _currentScenarioChunk == null ||
                _currentScenarioOriginalXmbData == null ||
                ScenarioMapCanvas.Scenario == null)
            {
                return;
            }

            string sourceName =
                System.IO.Path
                    .GetFileNameWithoutExtension(
                        _currentArchive.FileName);

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title =
                        "Export Modified Halo Wars ERA",

                    FileName =
                        $"{sourceName}_ensemble.era",

                    Filter =
                        "Halo Wars ERA (*.era)|*.era|" +
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
                    "Building modified SCN XMB...";

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

                File.WriteAllBytes(
                    dialog.FileName,
                    modifiedEra);

                // =====================================================
                // FULL ERA ROUND-TRIP VALIDATION
                // =====================================================

                StatusText.Text =
                    "Verifying exported ERA...";

                EraArchiveInfo verificationArchive =
                    EraArchiveService.Open(
                        dialog.FileName);

                if (_currentScenarioChunk.Index >=
                    verificationArchive.Chunks.Count)
                {
                    throw new InvalidDataException(
                        "Exported ERA lost the scenario chunk.");
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

                StatusText.Text =
                    $"Exported and verified " +
                    $"{System.IO.Path.GetFileName(dialog.FileName)}";

                MessageBox.Show(
                    this,
                    "Modified ERA exported successfully.\n\n" +
                    "" +
                    "" +
                    "",
                    "ERA Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "ERA Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "ERA export failed.";
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
            _undoStack.Push(
                new MoveHistoryAction(
                    e.Item,
                    e.OldPosition,
                    e.NewPosition));

            _redoStack.Clear();

            UpdateUndoRedoUi();

            SetDirty(
                true);

            StatusText.Text =
                $"Moved {GetItemDisplayName(e.Item)} | " +
                $"X {e.NewPosition.X:0.##}, " +
                $"Y {e.NewPosition.Y:0.##}, " +
                $"Z {e.NewPosition.Z:0.##} | " +
                $"Undo history: {_undoStack.Count}";
        }

        private void ScenarioMapCanvas_ItemRotated(
            object? sender,
            Ensemble.Controls.ScenarioItemRotatedEventArgs e)
        {
            _undoStack.Push(
                new RotationHistoryAction(
                    e.Item,
                    e.OldForward,
                    e.OldRight,
                    e.NewForward,
                    e.NewRight));

            _redoStack.Clear();

            UpdateUndoRedoUi();

            SetDirty(
                true);

            float yaw =
                Ensemble.Controls.MapCanvas
                    .GetYawDegrees(
                        e.NewForward);

            StatusText.Text =
                $"Rotated {GetItemDisplayName(e.Item)} | " +
                $"Yaw {yaw:0.##}° | " +
                $"Undo history: {_undoStack.Count}";
        }

        private void ShowScenarioObject(
            ScenarioObject obj)
        {
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

        private void SetDirty(
            bool dirty)
        {
            _isDirty =
                dirty;

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

            Title =
                $"Ensemble - {_currentArchive.FileName}" +
                (_isDirty
                    ? " *"
                    : string.Empty);
        }

        private void ShowPlayerStart(
            ScenarioPlayerStart start)
        {
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

        private void ShowScenarioSphere(
            ScenarioSphere sphere)
        {
            RightPanelTitle.Text =
                "DESIGN SPHERE";

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

        private void ShowScenarioPath(
            ScenarioPath path)
        {
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

        private void Exit_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}