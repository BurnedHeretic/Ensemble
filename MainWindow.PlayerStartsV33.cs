using Ensemble.Models;
using Ensemble.Services;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v33 custom-map player-count and starting-base workflow.
    ///
    /// Player count is intentionally driven by the scenario's real Position
    /// entries.  SaveModifiedEraToPath already uses PlayerStarts.Count for the
    /// ENSMAP1 MaxPlayers field, so keeping the XMB and ScenarioMap in sync
    /// makes the existing save/verification pipeline authoritative.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _playerStartsV33Bootstrap =
                RegisterPlayerStartsV33();

        private bool
            _playerStartsV33Initialized;

        // v33.1: MapViewport3D originally rendered player-start labels on
        // two crossed textured quads and derived the label from Player.
        // Halo Wars uses Number as the actual start-slot identity, and the
        // crossed quads made the same text overlap at normal editor angles.
        // This light visual-normalization timer fixes labels after any 3D
        // viewport rebuild without touching scenario/gameplay data.
        private DispatcherTimer?
            _playerStartLabelVisualTimerV33;

        private MenuItem?
            _playerStartsMenuV33;

        private MenuItem?
            _assignPlayerStartMenuV33;

        private readonly Dictionary<int, MenuItem>
            _assignPlayerItemsV33 =
                new();

        private static bool RegisterPlayerStartsV33()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    PlayerStartsV33_Loaded),
                true);

            return true;
        }

        private static void PlayerStartsV33_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                is not MainWindow window ||
                window._playerStartsV33Initialized)
            {
                return;
            }

            window._playerStartsV33Initialized =
                true;

            // Let MainWindow.EnsembleNext and the Halo Wars theme finish their
            // own dynamic menu work first.
            window.Dispatcher.BeginInvoke(
                new Action(
                    window.InitializePlayerStartsV33));
        }

        private void InitializePlayerStartsV33()
        {
            Menu? menu =
                FindVisualDescendantV19<Menu>(
                    this);

            if (menu ==
                null)
            {
                return;
            }

            MenuItem? mapMenu =
                menu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(
                        item =>
                            NormalizeMenuHeaderV33(
                                item.Header) ==
                            "Map");

            if (mapMenu ==
                null)
            {
                return;
            }

            _playerStartsMenuV33 =
                new MenuItem
                {
                    Header =
                        "_Player Starts"
                };

            _assignPlayerStartMenuV33 =
                new MenuItem
                {
                    Header =
                        "_Assign Selected Base"
                };

            for (int player = 1;
                 player <= 6;
                 player++)
            {
                int capturedPlayer =
                    player;

                MenuItem playerItem =
                    new MenuItem
                    {
                        Header =
                            $"Player _{player}",

                        IsCheckable =
                            true
                    };

                playerItem.Click +=
                    (
                        _,
                        _) =>
                        AssignSelectedBaseToPlayerV33(
                            capturedPlayer);

                _assignPlayerItemsV33[
                    player] =
                        playerItem;

                _assignPlayerStartMenuV33.Items.Add(
                    playerItem);
            }

            _assignPlayerStartMenuV33.SubmenuOpened +=
                (
                    _,
                    _) =>
                    UpdatePlayerStartMenuV33();

            MenuItem summaryItem =
                new MenuItem
                {
                    Header =
                        "_Player Start Summary..."
                };

            summaryItem.Click +=
                (
                    _,
                    _) =>
                    ShowPlayerStartSummaryV33();

            _playerStartsMenuV33.Items.Add(
                _assignPlayerStartMenuV33);

            _playerStartsMenuV33.Items.Add(
                new Separator());

            _playerStartsMenuV33.Items.Add(
                summaryItem);

            _playerStartsMenuV33.SubmenuOpened +=
                (
                    _,
                    _) =>
                    UpdatePlayerStartMenuV33();

            mapMenu.Items.Add(
                new Separator());

            mapMenu.Items.Add(
                _playerStartsMenuV33);

            ScenarioMapCanvas.SelectionChanged +=
                (
                    _,
                    _) =>
                    UpdatePlayerStartMenuV33();

            UpdatePlayerStartMenuV33();

            StartPlayerStartLabelVisualFixV33();
        }

        private void StartPlayerStartLabelVisualFixV33()
        {
            if (_playerStartLabelVisualTimerV33 !=
                null)
            {
                return;
            }

            _playerStartLabelVisualTimerV33 =
                new DispatcherTimer(
                    DispatcherPriority.Background)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            350)
                };

            _playerStartLabelVisualTimerV33.Tick +=
                (
                    _,
                    _) =>
                    NormalizePlayerStartLabelsV33();

            _playerStartLabelVisualTimerV33.Start();

            Dispatcher.BeginInvoke(
                new Action(
                    NormalizePlayerStartLabelsV33));
        }

        private void NormalizePlayerStartLabelsV33()
        {
            if (_ensemble3DViewport ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                map.PlayerStarts.Count ==
                    0)
            {
                return;
            }

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.NonPublic;

            FieldInfo? itemModelsField =
                typeof(Ensemble.Controls.MapViewport3D)
                    .GetField(
                        "_itemToModels",
                        flags);

            FieldInfo? baseMaterialsField =
                typeof(Ensemble.Controls.MapViewport3D)
                    .GetField(
                        "_baseMaterials",
                        flags);

            if (itemModelsField?.GetValue(
                    _ensemble3DViewport)
                    is not Dictionary<object, List<GeometryModel3D>> itemModels ||
                baseMaterialsField?.GetValue(
                    _ensemble3DViewport)
                    is not Dictionary<GeometryModel3D, Material> baseMaterials)
            {
                return;
            }

            bool changed =
                false;

            foreach (ScenarioPlayerStart start
                     in map.PlayerStarts)
            {
                if (!itemModels.TryGetValue(
                        start,
                        out List<GeometryModel3D>? models))
                {
                    continue;
                }

                foreach (GeometryModel3D model
                         in models)
                {
                    if (!IsCrossedPlayerStartLabelV33(
                            model))
                    {
                        continue;
                    }

                    MeshGeometry3D source =
                        (MeshGeometry3D)model.Geometry;

                    MeshGeometry3D singlePlane =
                        BuildSinglePlayerStartLabelPlaneV33(
                            source);

                    Color accent =
                        GetPlayerStartSlotColorV33(
                            start.Number);

                    Material material =
                        CreatePlayerStartLabelMaterialV33(
                            "P" +
                            Math.Clamp(
                                start.Number,
                                1,
                                6)
                                .ToString(
                                    System.Globalization.CultureInfo.InvariantCulture),
                            accent);

                    model.Geometry =
                        singlePlane;

                    model.Material =
                        material;

                    model.BackMaterial =
                        material;

                    baseMaterials[model] =
                        material;

                    changed =
                        true;
                }
            }

            if (changed)
            {
                // Reapply the current selection so selected player starts keep
                // their normal highlight using the new single-plane material.
                _ensemble3DViewport.SelectItem(
                    _selectedScenarioItem);
            }
        }

        private static bool IsCrossedPlayerStartLabelV33(
            GeometryModel3D model)
        {
            if (model.Geometry
                    is not MeshGeometry3D mesh ||
                mesh.Positions.Count !=
                    8 ||
                mesh.TextureCoordinates.Count !=
                    8 ||
                mesh.TriangleIndices.Count !=
                    12)
            {
                return false;
            }

            // Player-start labels use a DrawingBrush.  The native base pad
            // in the same item/model list does not, so this prevents an
            // accidental edit of the actual UGX geometry.
            if (model.Material
                is not MaterialGroup group)
            {
                return false;
            }

            return group.Children
                .OfType<DiffuseMaterial>()
                .Any(
                    diffuse =>
                        diffuse.Brush
                            is DrawingBrush);
        }

        private static MeshGeometry3D BuildSinglePlayerStartLabelPlaneV33(
            MeshGeometry3D source)
        {
            MeshGeometry3D mesh =
                new MeshGeometry3D();

            for (int i = 0;
                 i < 4;
                 i++)
            {
                mesh.Positions.Add(
                    source.Positions[i]);

                if (source.TextureCoordinates.Count >
                    i)
                {
                    mesh.TextureCoordinates.Add(
                        source.TextureCoordinates[i]);
                }

                if (source.Normals.Count >
                    i)
                {
                    mesh.Normals.Add(
                        source.Normals[i]);
                }
            }

            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);

            if (mesh.CanFreeze)
            {
                mesh.Freeze();
            }

            return mesh;
        }

        private static Color GetPlayerStartSlotColorV33(
            int playerNumber)
        {
            return playerNumber switch
            {
                1 => Color.FromRgb(0x42, 0xB8, 0xFF),
                2 => Color.FromRgb(0xFF, 0x73, 0x73),
                3 => Color.FromRgb(0x62, 0xE2, 0x86),
                4 => Color.FromRgb(0xD0, 0x86, 0xFF),
                5 => Color.FromRgb(0xFF, 0xB0, 0x4A),
                6 => Color.FromRgb(0x4A, 0xE2, 0xD8),
                _ => Color.FromRgb(0xFF, 0xD4, 0x55)
            };
        }

        private static Material CreatePlayerStartLabelMaterialV33(
            string label,
            Color accent)
        {
            DrawingGroup drawing =
                new DrawingGroup();

            drawing.Children.Add(
                new GeometryDrawing(
                    new SolidColorBrush(
                        Color.FromArgb(
                            238,
                            0x05,
                            0x11,
                            0x1B)),
                    new Pen(
                        new SolidColorBrush(
                            accent),
                        0.06),
                    new RectangleGeometry(
                        new Rect(
                            0.02,
                            0.04,
                            0.96,
                            0.92),
                        0.08,
                        0.08)));

            FormattedText formatted =
                new FormattedText(
                    label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Bahnschrift SemiCondensed"),
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal),
                    0.62,
                    Brushes.White,
                    1.0);

            Geometry textGeometry =
                formatted.BuildGeometry(
                    new System.Windows.Point(
                        0.5 -
                        formatted.Width /
                        2.0,
                        0.5 -
                        formatted.Height /
                        2.0));

            drawing.Children.Add(
                new GeometryDrawing(
                    Brushes.White,
                    null,
                    textGeometry));

            DrawingBrush brush =
                new DrawingBrush(
                    drawing)
                {
                    Stretch =
                        Stretch.Fill
                };

            MaterialGroup material =
                new MaterialGroup();

            material.Children.Add(
                new DiffuseMaterial(
                    brush));

            material.Children.Add(
                new EmissiveMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            58,
                            accent.R,
                            accent.G,
                            accent.B))));

            return material;
        }

        private static string NormalizeMenuHeaderV33(
            object? header)
        {
            return (
                    header?.ToString() ??
                    string.Empty)
                .Replace(
                    "_",
                    string.Empty)
                .Trim();
        }

        internal int GetPlayerCountV33()
        {
            if (_currentMapMetadata?.PlayerCount
                is 2 or 4 or 6)
            {
                return _currentMapMetadata
                    .PlayerCount;
            }

            if (_currentEraManifest?.MaxPlayers
                is 2 or 4 or 6)
            {
                return _currentEraManifest
                    .MaxPlayers;
            }

            if (ScenarioMapCanvas.Scenario
                is ScenarioMap map)
            {
                int uniqueStarts =
                    map.PlayerStarts
                        .Select(
                            start =>
                                start.Number)
                        .Where(
                            number =>
                                number >= 1 &&
                                number <= 6)
                        .Distinct()
                        .Count();

                if (uniqueStarts
                    is 2 or 4 or 6)
                {
                    return uniqueStarts;
                }

                if (map.PlayerStarts.Count
                    is 2 or 4 or 6)
                {
                    return map.PlayerStarts.Count;
                }
            }

            return 2;
        }

        internal bool IsPlayerCountSynchronizedV33(
            int playerCount)
        {
            if (playerCount is not
                (2 or 4 or 6) ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                map.PlayerStarts.Count !=
                    playerCount)
            {
                return false;
            }

            HashSet<int> numbers =
                map.PlayerStarts
                    .Select(
                        start =>
                            start.Number)
                    .ToHashSet();

            if (numbers.Count !=
                playerCount)
            {
                return false;
            }

            for (int player = 1;
                 player <= playerCount;
                 player++)
            {
                if (!numbers.Contains(
                        player))
                {
                    return false;
                }
            }

            return true;
        }

        internal void ApplyPlayerCountV33(
            int playerCount)
        {
            if (playerCount is not
                (2 or 4 or 6))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerCount),
                    "Halo Wars custom maps support 2, 4 or 6 players.");
            }

            if (_currentScenarioOriginalXmbData ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                throw new InvalidOperationException(
                    "Open an editable Halo Wars scenario before changing player count.");
            }

            List<ScenarioPlayerStart> previousStarts =
                map.PlayerStarts
                    .ToList();

            Dictionary<int, ScenarioPlayerStart> existingByNumber =
                previousStarts
                    .Where(
                        start =>
                            start.Number >= 1 &&
                            start.Number <= 6)
                    .GroupBy(
                        start =>
                            start.Number)
                    .ToDictionary(
                        group =>
                            group.Key,
                        group =>
                            group.First());

            List<ScenarioPlayerStart> desired =
                new();

            for (int player = 1;
                 player <= playerCount;
                 player++)
            {
                if (existingByNumber.TryGetValue(
                        player,
                        out ScenarioPlayerStart? existing))
                {
                    desired.Add(
                        existing);

                    continue;
                }

                desired.Add(
                    CreateDefaultPlayerStartV33(
                        map,
                        player,
                        playerCount,
                        previousStarts.FirstOrDefault()));
            }

            byte[] synchronizedXmb =
                PlayerStartStructureService
                    .Synchronize(
                        _currentScenarioOriginalXmbData,
                        desired);

            _currentScenarioOriginalXmbData =
                synchronizedXmb;

            map.PlayerStarts.Clear();
            map.PlayerStarts.AddRange(
                desired);

            // Do not leave explicit P5/P6 ownership on obvious base sockets
            // when a creator reduces the map to 2P/4P.
            foreach (ScenarioObject obj
                     in map.Objects)
            {
                if (obj.Player >
                        playerCount &&
                    obj.Player <=
                        6 &&
                    LooksLikeBaseSlotV33(
                        obj))
                {
                    obj.Player =
                        -1;
                }
            }

            if (_currentMapMetadata !=
                null)
            {
                _currentMapMetadata.PlayerCount =
                    playerCount;

                if (_currentMapMetadata.FormatVersion <=
                    0)
                {
                    _currentMapMetadata.FormatVersion =
                        1;
                }
            }

            _metadataDirty =
                true;

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            UpdateDirtyState();
            UpdatePlayerStartMenuV33();

            StatusText.Text =
                $"Player count set to {playerCount}. " +
                "Assign base slots from Map > Player Starts.";
        }

        private static ScenarioPlayerStart CreateDefaultPlayerStartV33(
            ScenarioMap map,
            int number,
            int totalPlayers,
            ScenarioPlayerStart? template)
        {
            float centreX =
                (map.MinX +
                 map.MaxX) *
                0.5f;

            float centreZ =
                (map.MinZ +
                 map.MaxZ) *
                0.5f;

            float radius =
                Math.Max(
                    25.0f,
                    Math.Min(
                        Math.Abs(
                            map.MaxX -
                            map.MinX),
                        Math.Abs(
                            map.MaxZ -
                            map.MinZ)) *
                    0.18f);

            double angle =
                (
                    number -
                    1
                ) *
                Math.PI *
                2.0 /
                totalPlayers;

            Vector3 position =
                new Vector3(
                    centreX +
                    (float)Math.Cos(
                        angle) *
                    radius,
                    template?.Position.Y ??
                    0,
                    centreZ +
                    (float)Math.Sin(
                        angle) *
                    radius);

            Vector3 towardCentre =
                new Vector3(
                    centreX -
                    position.X,
                    0,
                    centreZ -
                    position.Z);

            if (towardCentre.LengthSquared() <
                0.000001f)
            {
                towardCentre =
                    Vector3.UnitZ;
            }
            else
            {
                towardCentre =
                    Vector3.Normalize(
                        towardCentre);
            }

            return new ScenarioPlayerStart
            {
                Player =
                    -1,

                Number =
                    number,

                Position =
                    position,

                Forward =
                    towardCentre,

                DefaultCamera =
                    template?.DefaultCamera ??
                    true,

                CameraYaw =
                    template?.CameraYaw ??
                    317.2f,

                CameraPitch =
                    template?.CameraPitch ??
                    42.0f,

                CameraZoom =
                    template?.CameraZoom ??
                    85.0f
            };
        }

        private void AssignSelectedBaseToPlayerV33(
            int playerNumber)
        {
            if (ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                _currentScenarioOriginalXmbData ==
                    null)
            {
                MessageBox.Show(
                    this,
                    "Open an editable Halo Wars map before assigning player starts.",
                    "Player Starts",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            int playerCount =
                GetPlayerCountV33();

            if (playerNumber >
                playerCount)
            {
                int requiredCount =
                    playerNumber <= 4
                        ? 4
                        : 6;

                MessageBox.Show(
                    this,
                    $"This map is currently configured for {playerCount} players.\n\n" +
                    $"Set Player Count to {requiredCount} in Map Metadata first.",
                    "Player Slot Disabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (!TryGetSelectedStartAnchorV33(
                    out Vector3 position,
                    out Vector3 forward,
                    out ScenarioObject? selectedScenarioObject,
                    out string displayName))
            {
                MessageBox.Show(
                    this,
                    "Select a base socket/site (or another map object) in the 3D viewport first, then choose the player slot again.",
                    "Assign Player Start",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ScenarioPlayerStart? start =
                map.PlayerStarts
                    .FirstOrDefault(
                        item =>
                            item.Number ==
                            playerNumber);

            if (start ==
                null)
            {
                // Metadata/player-count sync should normally have created it.
                // Recover gracefully if an older map was opened mid-session.
                ApplyPlayerCountV33(
                    playerCount);

                start =
                    map.PlayerStarts
                        .FirstOrDefault(
                            item =>
                                item.Number ==
                                playerNumber);
            }

            if (start ==
                null)
            {
                throw new InvalidOperationException(
                    $"Player {playerNumber} start could not be created.");
            }

            start.Player =
                playerNumber;

            start.Position =
                position;

            start.Forward =
                NormaliseForwardV33(
                    forward);

            // A real SCN base socket/site also gets explicit ownership.  This
            // makes the assignment useful to the gameplay layer rather than
            // merely moving the camera/spawn marker.
            if (selectedScenarioObject !=
                null)
            {
                selectedScenarioObject.Player =
                    playerNumber;
            }

            _currentScenarioOriginalXmbData =
                PlayerStartStructureService
                    .Synchronize(
                        _currentScenarioOriginalXmbData,
                        map.PlayerStarts);

            // v33 player-start assignment is a structural scenario edit.
            // Keep the document dirty without fabricating a revision that has
            // no matching undo-stack action.
            _metadataDirty =
                true;

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            UpdateDirtyState();
            UpdatePlayerStartMenuV33();

            _ensemble3DViewport?
                .SelectItem(
                    start);

            StatusText.Text =
                $"Player {playerNumber} start assigned to {displayName} | " +
                $"X {position.X:0.##}, Y {position.Y:0.##}, Z {position.Z:0.##}";
        }

        private bool TryGetSelectedStartAnchorV33(
            out Vector3 position,
            out Vector3 forward,
            out ScenarioObject? scenarioObject,
            out string displayName)
        {
            position =
                default;

            forward =
                Vector3.UnitZ;

            scenarioObject =
                null;

            displayName =
                "selected object";

            switch (_selectedScenarioItem)
            {
                case ScenarioObject obj:
                    position =
                        obj.Position;

                    forward =
                        obj.Forward;

                    scenarioObject =
                        obj;

                    displayName =
                        string.IsNullOrWhiteSpace(
                            obj.EditorName)
                            ? obj.Type
                            : obj.EditorName;

                    return true;

                case ScenarioArtObject art:
                    position =
                        art.Position;

                    forward =
                        art.Forward;

                    displayName =
                        string.IsNullOrWhiteSpace(
                            art.DisplayName)
                            ? art.Type
                            : art.DisplayName;

                    return true;

                case ScenarioPlayerStart existingStart:
                    position =
                        existingStart.Position;

                    forward =
                        existingStart.Forward;

                    displayName =
                        $"Player {existingStart.Number} marker";

                    return true;

                default:
                    return false;
            }
        }

        private static Vector3 NormaliseForwardV33(
            Vector3 value)
        {
            Vector3 horizontal =
                new Vector3(
                    value.X,
                    0,
                    value.Z);

            if (!float.IsFinite(
                    horizontal.X) ||
                !float.IsFinite(
                    horizontal.Z) ||
                horizontal.LengthSquared() <
                    0.000001f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(
                horizontal);
        }

        private static bool LooksLikeBaseSlotV33(
            ScenarioObject obj)
        {
            string value =
                (
                    obj.EditorName +
                    " " +
                    obj.Type
                )
                .ToLowerInvariant();

            return value.Contains(
                       "base") ||
                   value.Contains(
                       "socket") ||
                   value.Contains(
                       "firebase");
        }

        private void RefreshScenarioAfterPlayerStartEditV33(
            ScenarioMap map)
        {
            CaptureTerrainCanvasStateV22();

            RefreshScenarioCanvas(
                map);

            MapCanvasTerrainStateService
                .RestoreIfCleared(
                    ScenarioMapCanvas,
                    _terrainCanvasStateV22);

            CaptureTerrainCanvasStateV22();

            Refresh3DViewportNext(
                false);
        }

        private void UpdatePlayerStartMenuV33()
        {
            if (_playerStartsMenuV33 ==
                    null ||
                _assignPlayerStartMenuV33 ==
                    null)
            {
                return;
            }

            bool mapOpen =
                ScenarioMapCanvas.Scenario !=
                    null &&
                _currentScenarioOriginalXmbData !=
                    null;

            _playerStartsMenuV33.IsEnabled =
                mapOpen;

            _assignPlayerStartMenuV33.IsEnabled =
                mapOpen &&
                _selectedScenarioItem !=
                    null;

            int playerCount =
                GetPlayerCountV33();

            foreach (KeyValuePair<int, MenuItem> item
                     in _assignPlayerItemsV33)
            {
                int player =
                    item.Key;

                item.Value.IsEnabled =
                    mapOpen &&
                    player <=
                        playerCount;

                item.Value.IsChecked =
                    IsSelectionAssignedToPlayerV33(
                        player);
            }
        }

        private bool IsSelectionAssignedToPlayerV33(
            int playerNumber)
        {
            if (ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                !TryGetSelectedStartAnchorV33(
                    out Vector3 position,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            ScenarioPlayerStart? start =
                map.PlayerStarts
                    .FirstOrDefault(
                        item =>
                            item.Number ==
                            playerNumber &&
                            item.Player ==
                            playerNumber);

            if (start ==
                null)
            {
                return false;
            }

            return Vector3.DistanceSquared(
                       start.Position,
                       position) <
                   0.25f;
        }

        private void ShowPlayerStartSummaryV33()
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            int playerCount =
                GetPlayerCountV33();

            List<string> lines =
                new()
                {
                    $"Configured players: {playerCount}",
                    string.Empty
                };

            for (int player = 1;
                 player <= playerCount;
                 player++)
            {
                ScenarioPlayerStart? start =
                    map.PlayerStarts
                        .FirstOrDefault(
                            item =>
                                item.Number ==
                                player);

                if (start ==
                    null)
                {
                    lines.Add(
                        $"P{player}: MISSING");

                    continue;
                }

                string ownership =
                    start.Player ==
                        player
                        ? "assigned"
                        : "unassigned/flexible";

                lines.Add(
                    $"P{player}: {ownership} | " +
                    $"X {start.Position.X:0.##}, " +
                    $"Y {start.Position.Y:0.##}, " +
                    $"Z {start.Position.Z:0.##}");
            }

            MessageBox.Show(
                this,
                string.Join(
                    Environment.NewLine,
                    lines),
                "Player Starts",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
