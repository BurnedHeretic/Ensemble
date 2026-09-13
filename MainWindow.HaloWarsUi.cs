using Ensemble.Controls;
using Ensemble.Models;
using Ensemble.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NumericsVector3 = System.Numerics.Vector3;

namespace Ensemble
{
    /// <summary>
    /// Halo Wars presentation and live-viewport behaviour layered over the
    /// current editor without replacing MainWindow.xaml.cs.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _haloWarsUiV19Bootstrap =
                RegisterHaloWarsUiV19();

        private bool
            _haloWarsUiV19Initialized;

        private bool
            _viewportTerrainStrokeV20;

        // v22: preserve the target map terrain across the in-place SetMap
        // refresh used by cross-ERA SCN object imports.
        private MapCanvasTerrainStateService.Snapshot?
            _terrainCanvasStateV22;

        private DispatcherTimer?
            _terrainStateTimerV22;

        private MenuItem?
            _eraInfoToggleMenuItemV19;

        private TextBlock?
            _viewportMapTitleV23;

        private Border?
            _viewportChromeV23;

        private Border?
            _welcomeFrameV23;

        private ViewportTransformGizmo?
            _transformGizmoV25;

        private static bool RegisterHaloWarsUiV19()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    HaloWarsUiV19_Loaded),
                true);

            return true;
        }

        private static void HaloWarsUiV19_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                is not MainWindow window
                ||
                window._haloWarsUiV19Initialized)
            {
                return;
            }

            window._haloWarsUiV19Initialized =
                true;

            // Defer one dispatcher turn so EnsembleNext has already created
            // its dynamic Objects / ERA Info / game-assets menu entries and
            // the 3D viewport exists before we wire sculpt input into it.
            window.Dispatcher.BeginInvoke(
                new Action(
                    window.InitializeHaloWarsUiV19));
        }

        private void InitializeHaloWarsUiV19()
        {
            TerrainGridMenuItem.Click +=
                ToggleMenuStateChangedV19;

            TerrainTextureMenuItem.Click +=
                ToggleMenuStateChangedV19;

            TerrainHeightMapMenuItem.Click +=
                ToggleMenuStateChangedV19;

            TerrainHiddenMenuItem.Click +=
                ToggleMenuStateChangedV19;

            _eraInfoToggleMenuItemV19 =
                FindMenuItemByHeaderV19(
                    "Show ERA Info")
                ??
                FindMenuItemByHeaderV19(
                    "Hide ERA Info");

            if (_eraInfoToggleMenuItemV19 !=
                null)
            {
                _eraInfoToggleMenuItemV19.Click +=
                    ToggleMenuStateChangedV19;
            }

            UpdateToggleMenuHeadersV19();

            ApplyMainWindowChromeV23();

            Wire3DViewportSculptInputV20();

            WireTransformGizmoV25();

            // Cross-ERA SCN imports currently refresh the same ScenarioMap by
            // calling MapCanvas.SetMap. SetMap clears its terrain fields because
            // it was designed for opening a different map. Cache the live target
            // terrain and restore it only when that exact same map instance is
            // refreshed. This keeps sculpt edits and the source XTD save path
            // intact while importing objects from another ERA.
            ScenarioMapCanvas.SelectionChanged +=
                HaloWarsUiV22_CanvasSelectionChanged;

            ScenarioMapCanvas.TerrainPreviewChanged +=
                HaloWarsUiV22_TerrainPreviewChanged;

            _terrainStateTimerV22 =
                new DispatcherTimer(
                    DispatcherPriority.Background)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            250)
                };

            _terrainStateTimerV22.Tick +=
                HaloWarsUiV22_TerrainStateTimerTick;

            _terrainStateTimerV22.Start();

            CaptureTerrainCanvasStateV22();

            Closed +=
                HaloWarsUiV19_Closed;
        }

        private void WireTransformGizmoV25()
        {
            if (_transformGizmoV25 !=
                null)
            {
                return;
            }

            if (_ensemble3DViewport ==
                null)
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        WireTransformGizmoV25));

                return;
            }

            _transformGizmoV25 =
                new ViewportTransformGizmo(
                    _ensemble3DViewport);

            _transformGizmoV25.LiveMoved +=
                HaloWarsUiV25_GizmoLiveMoved;

            _transformGizmoV25.MoveCommitted +=
                HaloWarsUiV25_GizmoMoveCommitted;
        }

        private void HaloWarsUiV25_GizmoLiveMoved(
            object? sender,
            ScenarioItemMovedEventArgs e)
        {
            if (_viewportTerrainStrokeV20 ||
                ScenarioMapCanvas
                    .IsTerrainSculptActive)
            {
                return;
            }

            // Rebuild only the object layer so the selected native UGX follows
            // the handle continuously without touching terrain or camera state.
            Refresh3DViewportNext(
                false);
        }

        private void HaloWarsUiV25_GizmoMoveCommitted(
            object? sender,
            ScenarioItemMovedEventArgs e)
        {
            if (_viewportTerrainStrokeV20 ||
                ScenarioMapCanvas
                    .IsTerrainSculptActive)
            {
                return;
            }

            // Mirror the existing MapViewport3D drag commit path so the move
            // enters Ensemble's normal undo/redo + dirty-state pipeline.
            ScenarioMapCanvas
                .ApplyHistoryPosition(
                    e.Item,
                    e.NewPosition);

            ScenarioMapCanvas_ItemMoved(
                ScenarioMapCanvas,
                e);

            Refresh3DViewportNext(
                false);
        }

        private void Wire3DViewportSculptInputV20()
        {
            if (_ensemble3DViewport ==
                null)
            {
                // EnsembleNext normally creates the viewport before this file's
                // deferred initializer runs. If WPF initialization ordering is
                // different on a machine, try once more on the next UI turn.
                Dispatcher.BeginInvoke(
                    new Action(
                        Wire3DViewportSculptInputV20));

                return;
            }

            _ensemble3DViewport.PreviewMouseLeftButtonDown +=
                HaloWarsUiV20_ViewportLeftDown;

            _ensemble3DViewport.PreviewMouseMove +=
                HaloWarsUiV20_ViewportMouseMove;

            _ensemble3DViewport.PreviewMouseLeftButtonUp +=
                HaloWarsUiV20_ViewportLeftUp;

            _ensemble3DViewport.LostMouseCapture +=
                HaloWarsUiV20_ViewportLostMouseCapture;
        }

        private void HaloWarsUiV19_Closed(
            object? sender,
            EventArgs e)
        {
            ScenarioMapCanvas.SelectionChanged -=
                HaloWarsUiV22_CanvasSelectionChanged;

            ScenarioMapCanvas.TerrainPreviewChanged -=
                HaloWarsUiV22_TerrainPreviewChanged;

            if (_terrainStateTimerV22 !=
                null)
            {
                _terrainStateTimerV22.Stop();
                _terrainStateTimerV22.Tick -=
                    HaloWarsUiV22_TerrainStateTimerTick;

                _terrainStateTimerV22 =
                    null;
            }

            if (_transformGizmoV25 !=
                null)
            {
                _transformGizmoV25.LiveMoved -=
                    HaloWarsUiV25_GizmoLiveMoved;

                _transformGizmoV25.MoveCommitted -=
                    HaloWarsUiV25_GizmoMoveCommitted;

                _transformGizmoV25.Dispose();
                _transformGizmoV25 =
                    null;
            }

            if (_ensemble3DViewport ==
                null)
            {
                return;
            }

            _ensemble3DViewport.PreviewMouseLeftButtonDown -=
                HaloWarsUiV20_ViewportLeftDown;

            _ensemble3DViewport.PreviewMouseMove -=
                HaloWarsUiV20_ViewportMouseMove;

            _ensemble3DViewport.PreviewMouseLeftButtonUp -=
                HaloWarsUiV20_ViewportLeftUp;

            _ensemble3DViewport.LostMouseCapture -=
                HaloWarsUiV20_ViewportLostMouseCapture;
        }

        private void HaloWarsUiV20_ViewportLeftDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_ensemble3DViewport ==
                    null ||
                !ScenarioMapCanvas
                    .IsTerrainSculptActive ||
                ScenarioMapCanvas
                    .TerrainHeightMap ==
                    null)
            {
                return;
            }

            // While sculpt mode is active, LMB belongs to terrain editing and
            // must not fall through to normal object selection / dragging.
            e.Handled =
                true;

            if (!ViewportTerrainSculptBridge
                    .TryGetTerrainPoint(
                        _ensemble3DViewport,
                        e.GetPosition(
                            _ensemble3DViewport),
                        out NumericsVector3 hit))
            {
                return;
            }

            if (!ViewportTerrainSculptBridge
                    .BeginStroke(
                        ScenarioMapCanvas,
                        hit.X,
                        hit.Z))
            {
                return;
            }

            _viewportTerrainStrokeV20 =
                true;

            _ensemble3DViewport.CaptureMouse();
            _ensemble3DViewport.Cursor =
                Cursors.Cross;

            MarkLiveTerrainDirtyV20();
            RefreshLiveTerrainV20();
        }

        private void HaloWarsUiV20_ViewportMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (_ensemble3DViewport ==
                    null ||
                !ScenarioMapCanvas
                    .IsTerrainSculptActive)
            {
                return;
            }

            _ensemble3DViewport.Cursor =
                Cursors.Cross;

            if (!_viewportTerrainStrokeV20 ||
                e.LeftButton !=
                    MouseButtonState.Pressed)
            {
                return;
            }

            e.Handled =
                true;

            if (!ViewportTerrainSculptBridge
                    .TryGetTerrainPoint(
                        _ensemble3DViewport,
                        e.GetPosition(
                            _ensemble3DViewport),
                        out NumericsVector3 hit))
            {
                return;
            }

            if (!ViewportTerrainSculptBridge
                    .ContinueStroke(
                        ScenarioMapCanvas,
                        hit.X,
                        hit.Z))
            {
                return;
            }

            MarkLiveTerrainDirtyV20();
            RefreshLiveTerrainV20();
        }

        private void HaloWarsUiV20_ViewportLeftUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_viewportTerrainStrokeV20)
            {
                return;
            }

            e.Handled =
                true;

            FinishViewportTerrainStrokeV20(
                releaseViewportCapture:
                    true);
        }

        private void HaloWarsUiV20_ViewportLostMouseCapture(
            object sender,
            MouseEventArgs e)
        {
            if (!_viewportTerrainStrokeV20)
            {
                return;
            }

            FinishViewportTerrainStrokeV20(
                releaseViewportCapture:
                    false);
        }

        private void FinishViewportTerrainStrokeV20(
            bool releaseViewportCapture)
        {
            if (!_viewportTerrainStrokeV20)
            {
                return;
            }

            // Clear this first because ReleaseMouseCapture raises
            // LostMouseCapture synchronously on some WPF configurations.
            _viewportTerrainStrokeV20 =
                false;

            ViewportTerrainSculptBridge.EndStroke(
                ScenarioMapCanvas);

            if (_ensemble3DViewport !=
                null)
            {
                if (releaseViewportCapture &&
                    _ensemble3DViewport
                        .IsMouseCaptured)
                {
                    _ensemble3DViewport
                        .ReleaseMouseCapture();
                }

                _ensemble3DViewport.Cursor =
                    ScenarioMapCanvas
                        .IsTerrainSculptActive
                        ? Cursors.Cross
                        : Cursors.Arrow;
            }

            // FinishTerrainStroke raises TerrainPreviewChanged and creates the
            // existing terrain undo record. Refresh once more so the final
            // geometry is guaranteed to match the committed preview buffer.
            RefreshLiveTerrainV20();
            UpdateDirtyState();
        }

        private void MarkLiveTerrainDirtyV20()
        {
            // HasTerrainPreviewChanges becomes true only when the stroke is
            // finalized. Make the title reflect the edit immediately while
            // the mouse is still down; the normal dirty calculation takes over
            // at stroke end.
            if (!_isDirty)
            {
                _isDirty =
                    true;

                UpdateWindowTitle();
            }

            StatusText.Text =
                "Terrain modified — save required.";
        }

        private void RefreshLiveTerrainV20()
        {
            if (_ensemble3DViewport ==
                null)
            {
                return;
            }

            _ensemble3DViewport.RefreshTerrain(
                ScenarioMapCanvas
                    .TerrainHeightMap);
        }

        private void HaloWarsUiV22_TerrainStateTimerTick(
            object? sender,
            EventArgs e)
        {
            CaptureTerrainCanvasStateV22();

            if (_transformGizmoV25 !=
                null)
            {
                _transformGizmoV25.IsInteractionEnabled =
                    !ScenarioMapCanvas
                        .IsTerrainSculptActive;

                _transformGizmoV25.SetSelectedItem(
                    _selectedScenarioItem);
            }

            ApplyPendingObjectGroundingV27();

            UpdateViewportChromeV23();
        }

        private void ApplyPendingObjectGroundingV27()
        {
            string status =
                StatusText.Text
                ??
                string.Empty;

            bool placementCompleted =
                status.StartsWith(
                    "Placed SCN object ",
                    StringComparison.Ordinal)
                ||
                status.StartsWith(
                    "Placed SC2 ArtObject ",
                    StringComparison.Ordinal);

            if (!placementCompleted ||
                !ObjectPlacementContextService
                    .TryTake(
                        out ObjectCatalogEntry? entry,
                        out bool preserveDonorPosition) ||
                entry ==
                    null ||
                _selectedScenarioItem ==
                    null ||
                ScenarioMapCanvas.TerrainHeightMap
                    is not TerrainHeightMap terrain ||
                !TryGetPlacedObjectPositionV27(
                    _selectedScenarioItem,
                    out NumericsVector3 oldPosition))
            {
                return;
            }

            float groundY =
                SampleTerrainHeightV27(
                    terrain,
                    oldPosition.X,
                    oldPosition.Z);

            // Normal Add Object placement should land on the target map rather
            // than reusing a donor map's absolute Y. Preserve-position imports
            // keep their donor height unless that would bury the object below
            // the target terrain.
            bool needsGrounding =
                !preserveDonorPosition
                ||
                oldPosition.Y <
                    groundY -
                    0.05f;

            if (!needsGrounding ||
                MathF.Abs(
                    oldPosition.Y -
                    groundY) <
                0.001f)
            {
                return;
            }

            NumericsVector3 newPosition =
                new NumericsVector3(
                    oldPosition.X,
                    groundY,
                    oldPosition.Z);

            ScenarioMapCanvas
                .ApplyHistoryPosition(
                    _selectedScenarioItem,
                    newPosition);

            ScenarioItemMovedEventArgs move =
                new ScenarioItemMovedEventArgs(
                    _selectedScenarioItem,
                    oldPosition,
                    newPosition);

            ScenarioMapCanvas_ItemMoved(
                ScenarioMapCanvas,
                move);

            Refresh3DViewportNext(
                false);

            _transformGizmoV25?
                .SetSelectedItem(
                    _selectedScenarioItem);

            StatusText.Text =
                status +
                $" | grounded at Y {groundY:0.##}";
        }

        private static bool TryGetPlacedObjectPositionV27(
            object item,
            out NumericsVector3 position)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    position =
                        obj.Position;
                    return true;

                case ScenarioArtObject art:
                    position =
                        art.Position;
                    return true;

                case ScenarioPlayerStart start:
                    position =
                        start.Position;
                    return true;

                default:
                    position =
                        default;
                    return false;
            }
        }

        private static float SampleTerrainHeightV27(
            TerrainHeightMap terrain,
            float worldX,
            float worldZ)
        {
            if (terrain.Width <
                    2 ||
                terrain.Height <
                    2 ||
                terrain.Heights.Length <
                    terrain.Width *
                    terrain.Height)
            {
                return 0;
            }

            double fx =
                (worldX -
                 terrain.WorldMin.X)
                /
                Math.Max(
                    0.0001,
                    terrain.TileScale);

            double fz =
                (worldZ -
                 terrain.WorldMin.Z)
                /
                Math.Max(
                    0.0001,
                    terrain.TileScale);

            fx =
                Math.Clamp(
                    fx,
                    0,
                    terrain.Width -
                    1);

            fz =
                Math.Clamp(
                    fz,
                    0,
                    terrain.Height -
                    1);

            int x0 =
                (int)Math.Floor(
                    fx);

            int z0 =
                (int)Math.Floor(
                    fz);

            int x1 =
                Math.Min(
                    x0 +
                    1,
                    terrain.Width -
                    1);

            int z1 =
                Math.Min(
                    z0 +
                    1,
                    terrain.Height -
                    1);

            double tx =
                fx -
                x0;

            double tz =
                fz -
                z0;

            float h00 =
                terrain.Heights[
                    z0 *
                    terrain.Width +
                    x0];

            float h10 =
                terrain.Heights[
                    z0 *
                    terrain.Width +
                    x1];

            float h01 =
                terrain.Heights[
                    z1 *
                    terrain.Width +
                    x0];

            float h11 =
                terrain.Heights[
                    z1 *
                    terrain.Width +
                    x1];

            double h0 =
                h00 +
                (h10 -
                 h00) *
                tx;

            double h1 =
                h01 +
                (h11 -
                 h01) *
                tx;

            return (float)(
                h0 +
                (h1 -
                 h0) *
                tz);
        }

        private void HaloWarsUiV22_TerrainPreviewChanged(
            object? sender,
            EventArgs e)
        {
            // A completed sculpt stroke is the most important moment to cache:
            // this preserves the exact edited TerrainHeightMap that must later
            // be encoded back into the target map's XTD/XSD during save.
            CaptureTerrainCanvasStateV22();
        }

        private void CaptureTerrainCanvasStateV22()
        {
            MapCanvasTerrainStateService.Snapshot? snapshot =
                MapCanvasTerrainStateService.Capture(
                    ScenarioMapCanvas);

            if (snapshot !=
                null)
            {
                _terrainCanvasStateV22 =
                    snapshot;
            }
        }

        private void HaloWarsUiV22_CanvasSelectionChanged(
            object? sender,
            ScenarioSelectionChangedEventArgs e)
        {
            if (!MapCanvasTerrainStateService
                    .RestoreIfCleared(
                        ScenarioMapCanvas,
                        _terrainCanvasStateV22))
            {
                // Normal selection changes and genuinely new maps land here.
                CaptureTerrainCanvasStateV22();

                return;
            }

            // Do not call SetTerrainHeightMap here: that API intentionally
            // clears the terrain undo/redo stacks. The state service restores
            // the exact fields SetMap cleared, leaving the sculpt history and
            // HasTerrainPreviewChanges flag untouched for SaveModifiedEraToPath.
            CaptureTerrainCanvasStateV22();

            if (_ensemble3DViewport !=
                null)
            {
                _ensemble3DViewport.RefreshTerrain(
                    ScenarioMapCanvas
                        .TerrainHeightMap);
            }
        }

        private void ApplyMainWindowChromeV23()
        {
            Brush background =
                GetThemeBrushV23(
                    "HwBackgroundBrush",
                    Color.FromRgb(
                        0x07,
                        0x1B,
                        0x2B));

            Brush deep =
                GetThemeBrushV23(
                    "HwBackgroundDeepBrush",
                    Color.FromRgb(
                        0x04,
                        0x11,
                        0x1C));

            Brush panel =
                GetThemeBrushV23(
                    "HwPanelBrush",
                    Color.FromRgb(
                        0x0B,
                        0x26,
                        0x38));

            Brush raised =
                GetThemeBrushV23(
                    "HwPanelRaisedBrush",
                    Color.FromRgb(
                        0x17,
                        0x38,
                        0x4C));

            Brush line =
                GetThemeBrushV23(
                    "HwLineBrush",
                    Color.FromRgb(
                        0x4B,
                        0x6D,
                        0x7E));

            Brush accent =
                GetThemeBrushV23(
                    "HwAccentBrush",
                    Color.FromRgb(
                        0x6E,
                        0xA8,
                        0xC3));

            Brush accentBright =
                GetThemeBrushV23(
                    "HwAccentBrightBrush",
                    Color.FromRgb(
                        0xB9,
                        0xE5,
                        0xF2));

            Brush muted =
                GetThemeBrushV23(
                    "HwMutedTextBrush",
                    Color.FromRgb(
                        0x82,
                        0x9C,
                        0xAB));

            Background =
                background;

            ArchiveTree.Background =
                deep;

            ArchiveTree.Foreground =
                accentBright;

            ArchiveTree.BorderThickness =
                new Thickness(
                    0);

            ArchiveTree.Padding =
                new Thickness(
                    8,
                    6,
                    8,
                    8);

            if (ArchiveTree.Parent
                    is DockPanel archiveDock &&
                archiveDock.Parent
                    is Border archivePanel)
            {
                archivePanel.Background =
                    panel;

                archivePanel.BorderBrush =
                    line;

                archivePanel.BorderThickness =
                    new Thickness(
                        0,
                        0,
                        1,
                        0);

                Border? header =
                    archiveDock.Children
                        .OfType<Border>()
                        .FirstOrDefault();

                if (header !=
                    null)
                {
                    header.Background =
                        raised;

                    header.BorderBrush =
                        accent;

                    header.BorderThickness =
                        new Thickness(
                            0,
                            0,
                            0,
                            1);

                    header.Padding =
                        new Thickness(
                            12,
                            10,
                            12,
                            10);

                    if (header.Child
                        is TextBlock archiveHeaderText)
                    {
                        archiveHeaderText.Foreground =
                            accentBright;

                        archiveHeaderText.FontSize =
                            14;

                        archiveHeaderText.FontWeight =
                            FontWeights.SemiBold;
                    }
                }
            }

            // v28: MainWindow.xaml gives the top Menu a legacy dark local
            // background, which overrides the implicit Halo Wars Menu style.
            // Restyle it explicitly so the navigation bar uses the same
            // blue technical panel treatment as the rest of the editor.
            Menu? mainMenu =
                FindVisualDescendantV19<Menu>(
                    this);

            if (mainMenu !=
                null)
            {
                Brush menuBackground =
                    GetThemeBrushV23(
                        "HwPanelGradientBrush",
                        Color.FromRgb(
                            0x17,
                            0x38,
                            0x4C));

                mainMenu.Background =
                    menuBackground;

                mainMenu.Foreground =
                    accentBright;

                mainMenu.BorderBrush =
                    accent;

                mainMenu.BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1);

                mainMenu.Padding =
                    new Thickness(
                        8,
                        2,
                        8,
                        2);

                foreach (MenuItem topLevelItem
                         in mainMenu.Items
                             .OfType<MenuItem>())
                {
                    topLevelItem.Foreground =
                        accentBright;

                    topLevelItem.Background =
                        Brushes.Transparent;

                    topLevelItem.Padding =
                        new Thickness(
                            16,
                            6,
                            16,
                            6);
                }
            }

            XmbPreviewText.Background =
                deep;

            XmbPreviewText.Foreground =
                accentBright;

            XmbPreviewText.BorderBrush =
                line;

            StatusBar? statusBar =
                FindVisualDescendantV19<StatusBar>(
                    this);

            if (statusBar !=
                null)
            {
                statusBar.Background =
                    raised;

                statusBar.Foreground =
                    accentBright;

                statusBar.BorderBrush =
                    line;

                statusBar.BorderThickness =
                    new Thickness(
                        0,
                        1,
                        0,
                        0);
            }

            StyleWelcomePanelV23(
                panel,
                raised,
                line,
                accent,
                accentBright,
                muted);

            AddViewportChromeV23(
                raised,
                accent,
                accentBright,
                muted);
        }

        private void StyleWelcomePanelV23(
            Brush panel,
            Brush raised,
            Brush line,
            Brush accent,
            Brush accentBright,
            Brush muted)
        {
            if (_welcomeFrameV23 ==
                    null &&
                WelcomePanel.Parent
                    is Grid host)
            {
                int index =
                    host.Children.IndexOf(
                        WelcomePanel);

                host.Children.Remove(
                    WelcomePanel);

                _welcomeFrameV23 =
                    new Border
                    {
                        Width =
                            560,

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        VerticalAlignment =
                            VerticalAlignment.Center,

                        Background =
                            panel,

                        BorderBrush =
                            line,

                        BorderThickness =
                            new Thickness(
                                1),

                        Padding =
                            new Thickness(
                                42,
                                34,
                                42,
                                38),

                        Child =
                            WelcomePanel
                    };

                _welcomeFrameV23.SetBinding(
                    UIElement.VisibilityProperty,
                    new Binding(
                        "Visibility")
                    {
                        Source =
                            WelcomePanel,

                        Mode =
                            BindingMode.OneWay
                    });

                Panel.SetZIndex(
                    _welcomeFrameV23,
                    20);

                host.Children.Insert(
                    Math.Max(
                        0,
                        index),
                    _welcomeFrameV23);
            }

            if (WelcomePanel.Children.Count >=
                3)
            {
                if (WelcomePanel.Children[
                        0]
                    is TextBlock title)
                {
                    title.Text =
                        "ENSEMBLE";

                    title.FontSize =
                        44;

                    title.FontWeight =
                        FontWeights.SemiBold;

                    title.Foreground =
                        accentBright;

                    title.Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            4);
                }

                if (WelcomePanel.Children[
                        1]
                    is TextBlock subtitle)
                {
                    subtitle.Text =
                        "HALO WARS MAP EDITOR";

                    subtitle.FontSize =
                        17;

                    subtitle.Foreground =
                        muted;

                    subtitle.Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            28);
                }

                if (WelcomePanel.Children[
                        2]
                    is Button openButton)
                {
                    openButton.Content =
                        "OPEN HALO WARS ERA";

                    openButton.Width =
                        260;

                    openButton.Height =
                        48;

                    openButton.HorizontalAlignment =
                        HorizontalAlignment.Center;
                }
            }

            if (!WelcomePanel.Children
                    .OfType<Border>()
                    .Any(
                        border =>
                            string.Equals(
                                border.Tag
                                    as string,
                                "ENSEMBLE_WELCOME_ACCENT_V23",
                                StringComparison.Ordinal)))
            {
                Border topAccent =
                    new Border
                    {
                        Tag =
                            "ENSEMBLE_WELCOME_ACCENT_V23",

                        Height =
                            3,

                        Width =
                            300,

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        Margin =
                            new Thickness(
                                0,
                                0,
                                0,
                                24),

                        Background =
                            accent
                    };

                WelcomePanel.Children.Insert(
                    0,
                    topAccent);

                TextBlock strapline =
                    new TextBlock
                    {
                        Text =
                            "SCENARIO // TERRAIN // OBJECT WORKFLOW",

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        Foreground =
                            muted,

                        FontSize =
                            11,

                        Margin =
                            new Thickness(
                                0,
                                18,
                                0,
                                0)
                    };

                WelcomePanel.Children.Add(
                    strapline);
            }
        }

        private void AddViewportChromeV23(
            Brush raised,
            Brush accent,
            Brush accentBright,
            Brush muted)
        {
            if (_viewportChromeV23 !=
                    null ||
                _ensemble3DViewport ==
                    null ||
                _ensemble3DViewport.Parent
                    is not Grid host)
            {
                return;
            }

            _viewportMapTitleV23 =
                new TextBlock
                {
                    Text =
                        "MAP",

                    Foreground =
                        accentBright,

                    FontSize =
                        12,

                    FontWeight =
                        FontWeights.SemiBold,

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            StackPanel text =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal
                };

            text.Children.Add(
                new TextBlock
                {
                    Text =
                        "SCENARIO VIEWPORT // 3D",

                    Foreground =
                        accentBright,

                    FontSize =
                        12,

                    FontWeight =
                        FontWeights.SemiBold,

                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            text.Children.Add(
                new TextBlock
                {
                    Text =
                        "   |   ",

                    Foreground =
                        muted,

                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            text.Children.Add(
                _viewportMapTitleV23);

            _viewportChromeV23 =
                new Border
                {
                    HorizontalAlignment =
                        HorizontalAlignment.Left,

                    VerticalAlignment =
                        VerticalAlignment.Top,

                    Margin =
                        new Thickness(
                            12),

                    Padding =
                        new Thickness(
                            10,
                            6,
                            10,
                            6),

                    Background =
                        raised,

                    BorderBrush =
                        accent,

                    BorderThickness =
                        new Thickness(
                            1),

                    Child =
                        text,

                    IsHitTestVisible =
                        false
                };

            _viewportChromeV23.SetBinding(
                UIElement.VisibilityProperty,
                new Binding(
                    "Visibility")
                {
                    Source =
                        _ensemble3DViewport,

                    Mode =
                        BindingMode.OneWay
                });

            Panel.SetZIndex(
                _viewportChromeV23,
                150);

            host.Children.Add(
                _viewportChromeV23);

            UpdateViewportChromeV23();
        }

        private void UpdateViewportChromeV23()
        {
            if (_viewportMapTitleV23 ==
                null)
            {
                return;
            }

            string name =
                ScenarioMapCanvas.Scenario?
                    .Name
                ??
                string.Empty;

            _viewportMapTitleV23.Text =
                string.IsNullOrWhiteSpace(
                    name)
                    ? "MAP"
                    : name.ToUpperInvariant();
        }

        private Brush GetThemeBrushV23(
            string key,
            Color fallback)
        {
            return TryFindResource(
                       key)
                   as Brush
                ??
                new SolidColorBrush(
                    fallback);
        }

        private void ToggleMenuStateChangedV19(
            object sender,
            RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    UpdateToggleMenuHeadersV19));
        }

        private void UpdateToggleMenuHeadersV19()
        {
            // Grid is a true on/off toggle, so its text describes the action
            // that clicking it will perform next.
            TerrainGridMenuItem.Header =
                TerrainGridMenuItem.IsChecked
                    ? "_Hide Grid"
                    : "_Show Grid";

            // Keep the three terrain presentation entries action-oriented.
            // The user's renamed "Show Terrain" remains the normal textured
            // terrain mode.
            TerrainTextureMenuItem.Header =
                "_Show Terrain";

            TerrainHeightMapMenuItem.Header =
                "_Show Heightmap";

            TerrainHiddenMenuItem.Header =
                "_Hide Terrain";

            if (_eraInfoToggleMenuItemV19 !=
                null)
            {
                _eraInfoToggleMenuItemV19.Header =
                    _eraInfoToggleMenuItemV19.IsChecked
                        ? "_Hide ERA Info"
                        : "_Show ERA Info";
            }
        }

        private MenuItem? FindMenuItemByHeaderV19(
            string header)
        {
            Menu? menu =
                FindVisualDescendantV19<Menu>(
                    this);

            if (menu ==
                null)
            {
                return null;
            }

            return FindMenuItemRecursiveV19(
                menu.Items,
                header);
        }

        private static MenuItem? FindMenuItemRecursiveV19(
            ItemCollection items,
            string header)
        {
            foreach (object item
                     in items)
            {
                if (item
                    is not MenuItem menuItem)
                {
                    continue;
                }

                string current =
                    (
                        menuItem.Header?
                            .ToString()
                        ??
                        string.Empty
                    )
                    .Replace(
                        "_",
                        string.Empty)
                    .Trim();

                if (current.Equals(
                        header,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return menuItem;
                }

                MenuItem? child =
                    FindMenuItemRecursiveV19(
                        menuItem.Items,
                        header);

                if (child !=
                    null)
                {
                    return child;
                }
            }

            return null;
        }

        private static T? FindVisualDescendantV19<T>(
            DependencyObject root)
            where T : DependencyObject
        {
            if (root
                is T match)
            {
                return match;
            }

            int count =
                VisualTreeHelper
                    .GetChildrenCount(
                        root);

            for (int i = 0;
                 i < count;
                 i++)
            {
                DependencyObject child =
                    VisualTreeHelper
                        .GetChild(
                            root,
                            i);

                T? result =
                    FindVisualDescendantV19<T>(
                        child);

                if (result !=
                    null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
