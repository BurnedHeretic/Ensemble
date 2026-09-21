using Ensemble.Models;
using Ensemble.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ensemble
{
    public partial class MainWindow
    {
        private static readonly bool
            _mapValidationBootstrapV37 =
                RegisterMapValidationV37();

        private bool
            _mapValidationInitializedV37;

        private MenuItem?
            _validateMapMenuItemV37;

        private static bool RegisterMapValidationV37()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    MapValidationV37_Loaded),
                true);

            return true;
        }

        private static void MapValidationV37_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._mapValidationInitializedV37)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializeMapValidationV37));
        }

        private void InitializeMapValidationV37()
        {
            if (_mapValidationInitializedV37)
                return;

            MenuItem? toolsMenu =
                FindMenuItemByHeaderV19(
                    "Tools");

            if (toolsMenu ==
                null)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(
                        InitializeMapValidationV37));

                return;
            }

            _mapValidationInitializedV37 =
                true;

            if (toolsMenu.Items.Count >
                0)
            {
                toolsMenu.Items.Add(
                    new Separator());
            }

            _validateMapMenuItemV37 =
                new MenuItem
                {
                    Header =
                        "_Validate Current Map..."
                };

            _validateMapMenuItemV37.Click +=
                ValidateMapV37_Click;

            toolsMenu.Items.Add(
                _validateMapMenuItemV37);

            Closed +=
                MapValidationV37_Closed;
        }

        private void MapValidationV37_Closed(
            object? sender,
            EventArgs e)
        {
            if (_validateMapMenuItemV37 !=
                null)
            {
                _validateMapMenuItemV37.Click -=
                    ValidateMapV37_Click;
            }

            Closed -=
                MapValidationV37_Closed;
        }

        private void ValidateMapV37_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                MessageBox.Show(
                    this,
                    "Open a Halo Wars scenario before running map validation.",
                    "Validate Current Map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            IReadOnlyList<MapValidationItemV37> report =
                BuildMapValidationReportV37(
                    map);

            MapValidationWindowV37 window =
                new MapValidationWindowV37(
                    string.IsNullOrWhiteSpace(
                        map.Name)
                        ? _currentArchive.FileName
                        : map.Name,
                    report)
                {
                    Owner =
                        this
                };

            window.ShowDialog();
        }

        private IReadOnlyList<MapValidationItemV37>
            BuildMapValidationReportV37(
                ScenarioMap map)
        {
            List<MapValidationItemV37> items =
                new();

            void Add(
                MapValidationSeverityV37 severity,
                string title,
                string detail)
            {
                items.Add(
                    new MapValidationItemV37
                    {
                        Severity =
                            severity,

                        Title =
                            title,

                        Detail =
                            detail
                    });
            }

            // ---------------------------------------------------------
            // Player structure
            // ---------------------------------------------------------
            int playerCount =
                GetPlayerCountV363();

            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            if (playerCount
                    is 2 or 4 or 6 &&
                starts.Count ==
                    playerCount)
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Playable start count",
                    $"{starts.Count} editor start slots for a {playerCount}-player map.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Error,
                    "Playable start count",
                    $"Map metadata expects {playerCount} player(s), but the scenario exposes {starts.Count} playable start(s).");
            }

            if (_currentScenarioOriginalXmbData !=
                    null &&
                IsPlayerCountSynchronizedV363(
                    playerCount))
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Halo Wars player structure",
                    $"<Players> and grouped <Positions> are synchronized for {playerCount} players.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Error,
                    "Halo Wars player structure",
                    "The real scenario <Players>/<Positions> structure is not synchronized. Open Map Metadata, choose the intended player count, click Save, then save the ERA.");
            }

            for (int slot = 1;
                 slot <= starts.Count;
                 slot++)
            {
                ScenarioPlayerStart start =
                    starts[slot - 1];

                bool inBounds =
                    start.Position.X >=
                        map.MinX &&
                    start.Position.X <=
                        map.MaxX &&
                    start.Position.Z >=
                        map.MinZ &&
                    start.Position.Z <=
                        map.MaxZ;

                if (!inBounds)
                {
                    Add(
                        MapValidationSeverityV37.Warning,
                        $"P{slot} start outside map bounds",
                        $"Position {start.Position} is outside X {map.MinX:0.##}–{map.MaxX:0.##}, Z {map.MinZ:0.##}–{map.MaxZ:0.##}.");
                }
            }

            if (playerCount
                is 2 or 4 or 6)
            {
                List<int> noBase =
                    new();

                for (int slot = 1;
                     slot <= playerCount;
                     slot++)
                {
                    bool hasOwnedBase =
                        map.Objects.Any(
                            obj =>
                                obj.Player ==
                                    slot &&
                                LooksLikeBaseSlotV33(
                                    obj));

                    if (!hasOwnedBase)
                    {
                        noBase.Add(
                            slot);
                    }
                }

                if (noBase.Count ==
                    0)
                {
                    Add(
                        MapValidationSeverityV37.Pass,
                        "Starting-base ownership",
                        "Every active player slot has an explicitly owned base/socket object.");
                }
                else
                {
                    Add(
                        MapValidationSeverityV37.Warning,
                        "Starting-base ownership",
                        "No explicitly owned base/socket was detected for: " +
                        string.Join(
                            ", ",
                            noBase.Select(
                                slot =>
                                    $"P{slot}")) +
                        ". This can be intentional on unusual maps, but ordinary skirmish maps should be checked.");
                }
            }

            // ---------------------------------------------------------
            // Object identity / structure
            // ---------------------------------------------------------
            List<int> ids =
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
                            0)
                    .ToList();

            List<int> duplicates =
                ids
                    .GroupBy(
                        id =>
                            id)
                    .Where(
                        group =>
                            group.Count() >
                            1)
                    .Select(
                        group =>
                            group.Key)
                    .OrderBy(
                        id =>
                            id)
                    .ToList();

            if (duplicates.Count ==
                0)
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Object IDs",
                    $"{ids.Count:N0} positive SCN/SC2 object IDs are unique across the editable layers.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Error,
                    "Duplicate object IDs",
                    "Duplicate IDs detected across SCN/SC2: " +
                    string.Join(
                        ", ",
                        duplicates.Take(
                            20)) +
                    (
                        duplicates.Count >
                            20
                            ? " ..."
                            : string.Empty
                    ));
            }

            const int MaxSignedInt24 =
                0x007FFFFF;

            List<int> outOfRangeIds =
                ids
                    .Where(
                        id =>
                            id >
                            MaxSignedInt24)
                    .Distinct()
                    .OrderBy(
                        id =>
                            id)
                    .ToList();

            if (outOfRangeIds.Count ==
                0)
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Object ID range",
                    "Editable object IDs fit Halo Wars' signed Int24 scenario-object range.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Error,
                    "Object ID range",
                    $"{outOfRangeIds.Count} object ID(s) exceed 0x007FFFFF.");
            }

            // ---------------------------------------------------------
            // Terrain / simulation pair
            // ---------------------------------------------------------
            TerrainHeightMap? terrain =
                ScenarioMapCanvas
                    .TerrainHeightMap;

            bool hasXtd =
                _currentTerrainChunk !=
                    null &&
                _currentTerrainOriginalXtdData !=
                    null;

            bool hasXsd =
                _currentSimulationChunk !=
                    null &&
                _currentSimulationOriginalXsdData !=
                    null;

            if (hasXtd &&
                hasXsd &&
                terrain !=
                    null)
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Gameplay terrain pair",
                    $"XTD {terrain.Width}×{terrain.Height} and XSD simulation data are both loaded.");
            }
            else if (!hasXtd &&
                     !hasXsd)
            {
                Add(
                    MapValidationSeverityV37.Warning,
                    "Gameplay terrain pair",
                    "No editable XTD/XSD gameplay terrain pair is loaded.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Error,
                    "Gameplay terrain pair",
                    $"Terrain pair is incomplete. XTD: {(hasXtd ? "present" : "missing")}; XSD: {(hasXsd ? "present" : "missing")}.");
            }

            if (terrain !=
                null)
            {
                int invalidHeights =
                    terrain.Heights.Count(
                        value =>
                            !float.IsFinite(
                                value) ||
                            value <
                                terrain.EncodableMinHeight -
                                0.001f ||
                            value >
                                terrain.EncodableMaxHeight +
                                0.001f);

                if (invalidHeights ==
                    0)
                {
                    Add(
                        MapValidationSeverityV37.Pass,
                        "Terrain height range",
                        $"All {terrain.Heights.Length:N0} XTD vertices fit the encodable range {terrain.EncodableMinHeight:0.##} → {terrain.EncodableMaxHeight:0.##}.");
                }
                else
                {
                    Add(
                        MapValidationSeverityV37.Error,
                        "Terrain height range",
                        $"{invalidHeights:N0} terrain vertices are non-finite or outside the XTD encoding range.");
                }

                if (ScenarioMapCanvas
                        .HasTerrainPreviewChanges &&
                    !hasXsd)
                {
                    Add(
                        MapValidationSeverityV37.Error,
                        "Pending terrain save",
                        "Terrain has unsaved changes, but no XSD simulation companion is available. Ensemble intentionally refuses to save visual-only terrain changes.");
                }
            }

            // ---------------------------------------------------------
            // Metadata / save state
            // ---------------------------------------------------------
            string displayName =
                _currentMapMetadata?.DisplayName
                ??
                _currentEraManifest?.DisplayName
                ??
                string.Empty;

            if (!string.IsNullOrWhiteSpace(
                    displayName))
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Map metadata",
                    $"Display name: {displayName}");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Warning,
                    "Map metadata",
                    "No custom display name is currently set.");
            }

            if (_isDirty)
            {
                Add(
                    MapValidationSeverityV37.Warning,
                    "Unsaved changes",
                    "The current map contains unsaved editor changes. Save the ERA before final in-game validation.");
            }
            else
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Save state",
                    "No unsaved editor changes are currently flagged.");
            }

            if (items.All(
                    item =>
                        item.Severity !=
                            MapValidationSeverityV37.Error))
            {
                Add(
                    MapValidationSeverityV37.Pass,
                    "Structural preflight",
                    "No blocking structural errors were detected by Ensemble's current validators. In-game testing is still required for scripts, object dependencies and gameplay balance.");
            }

            return items;
        }
    }
}
