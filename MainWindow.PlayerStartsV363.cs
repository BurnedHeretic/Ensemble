using Ensemble.Models;
using Ensemble.Services;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v36.3 editor-side skirmish start model.
    ///
    /// P1..P6 are EDITOR SLOT labels. Halo Wars itself groups skirmish
    /// Positions by Number 1 / Number 2 (team-side spawn pools), so the UI must
    /// not use Position.Number as a unique player identity.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _playerStartsV363Bootstrap =
                RegisterPlayerStartsV363();

        private bool
            _playerStartsV363Initialized;

        private DispatcherTimer?
            _playerStartLabelTimerV363;

        private static bool RegisterPlayerStartsV363()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    PlayerStartsV363_Loaded),
                true);

            return true;
        }

        private static void PlayerStartsV363_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._playerStartsV363Initialized)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializePlayerStartsV363));
        }

        private void InitializePlayerStartsV363()
        {
            if (_playerStartsV363Initialized)
                return;

            _playerStartsV363Initialized =
                true;

            // v33's label fixer labels from Position.Number. Number is now
            // correctly understood as the 1/2 spawn-group identifier, so stop
            // that timer and replace it with sequential editor-slot labels.
            if (_playerStartLabelVisualTimerV33 !=
                null)
            {
                _playerStartLabelVisualTimerV33.Stop();

                _playerStartLabelVisualTimerV33 =
                    null;
            }

            _playerStartLabelTimerV363 =
                new DispatcherTimer(
                    DispatcherPriority.Background)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            300)
                };

            _playerStartLabelTimerV363.Tick +=
                PlayerStartLabelTimerV363_Tick;

            _playerStartLabelTimerV363.Start();

            Closed +=
                PlayerStartsV363_Closed;

            Dispatcher.BeginInvoke(
                new Action(
                    NormalizePlayerStartLabelsV363));
        }

        private void PlayerStartLabelTimerV363_Tick(
            object? sender,
            EventArgs e)
        {
            NormalizePlayerStartLabelsV363();
        }

        private void PlayerStartsV363_Closed(
            object? sender,
            EventArgs e)
        {
            if (_playerStartLabelTimerV363 !=
                null)
            {
                _playerStartLabelTimerV363.Stop();

                _playerStartLabelTimerV363.Tick -=
                    PlayerStartLabelTimerV363_Tick;

                _playerStartLabelTimerV363 =
                    null;
            }

            Closed -=
                PlayerStartsV363_Closed;
        }

        internal int GetPlayerCountV363()
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
                int playable =
                    GetPlayableStartsV363(
                        map)
                    .Count;

                if (playable
                    is 2 or 4 or 6)
                {
                    return playable;
                }
            }

            return 2;
        }

        internal bool IsPlayerCountSynchronizedV363(
            int playerCount)
        {
            if (playerCount
                    is not (2 or 4 or 6) ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                _currentScenarioOriginalXmbData ==
                    null)
            {
                return false;
            }

            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            if (starts.Count !=
                playerCount)
            {
                return false;
            }

            int teamSize =
                playerCount /
                2;

            bool modelGroupingCorrect =
                starts
                    .Take(teamSize)
                    .All(
                        start =>
                            start.Player ==
                                -1 &&
                            start.Number ==
                                1)
                &&
                starts
                    .Skip(teamSize)
                    .Take(teamSize)
                    .All(
                        start =>
                            start.Player ==
                                -1 &&
                            start.Number ==
                                2);

            if (!modelGroupingCorrect)
                return false;

            return PlayerStartStructureService
                .IsSkirmishStructureSynchronized(
                    _currentScenarioOriginalXmbData,
                    playerCount);
        }

        internal void ApplyPlayerCountV363(
            int playerCount)
        {
            if (playerCount
                is not (2 or 4 or 6))
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

            List<ScenarioPlayerStart> previous =
                GetPlayableStartsV363(
                    map);

            int teamSize =
                playerCount /
                2;

            bool sourceAlreadyGrouped =
                previous.Count > 0 &&
                previous.All(
                    start =>
                        start.Number
                            is 1 or 2) &&
                previous.Any(
                    start =>
                        start.Number ==
                            1) &&
                previous.Any(
                    start =>
                        start.Number ==
                            2);

            List<ScenarioPlayerStart> desired =
                new(
                    playerCount);

            if (sourceAlreadyGrouped)
            {
                List<ScenarioPlayerStart> group1 =
                    previous
                        .Where(
                            start =>
                                start.Number ==
                                    1)
                        .ToList();

                List<ScenarioPlayerStart> group2 =
                    previous
                        .Where(
                            start =>
                                start.Number ==
                                    2)
                        .ToList();

                for (int i = 0;
                     i < teamSize;
                     i++)
                {
                    ScenarioPlayerStart start =
                        i < group1.Count
                            ? group1[i]
                            : CreateDefaultPlayerStartV33(
                                map,
                                i + 1,
                                playerCount,
                                group1.FirstOrDefault()
                                ??
                                previous.FirstOrDefault());

                    desired.Add(
                        start);
                }

                for (int i = 0;
                     i < teamSize;
                     i++)
                {
                    int editorSlot =
                        teamSize +
                        i +
                        1;

                    ScenarioPlayerStart start =
                        i < group2.Count
                            ? group2[i]
                            : CreateDefaultPlayerStartV33(
                                map,
                                editorSlot,
                                playerCount,
                                group2.FirstOrDefault()
                                ??
                                previous.FirstOrDefault());

                    desired.Add(
                        start);
                }
            }
            else
            {
                // Recovery path for maps created by the earlier v33/v35.8
                // implementation where Number was incorrectly made unique
                // (1,2,3,4,5,6).
                List<ScenarioPlayerStart> sequential =
                    previous
                        .OrderBy(
                            start =>
                                start.Number >= 1 &&
                                start.Number <= 6
                                    ? start.Number
                                    : int.MaxValue)
                        .ToList();

                for (int slot = 1;
                     slot <= playerCount;
                     slot++)
                {
                    ScenarioPlayerStart start =
                        slot <= sequential.Count
                            ? sequential[
                                slot - 1]
                            : CreateDefaultPlayerStartV33(
                                map,
                                slot,
                                playerCount,
                                sequential.FirstOrDefault());

                    desired.Add(
                        start);
                }
            }

            NormalizeSkirmishStartModelV363(
                desired,
                playerCount);

            byte[] synchronizedXmb =
                PlayerStartStructureService
                    .SynchronizeSkirmish(
                        _currentScenarioOriginalXmbData,
                        desired,
                        playerCount);

            _currentScenarioOriginalXmbData =
                synchronizedXmb;

            map.PlayerStarts.Clear();

            map.PlayerStarts.AddRange(
                desired);

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

            NormalizePlayerStartLabelsV363();

            StatusText.Text =
                $"Player count set to {playerCount} | " +
                $"{teamSize} Number=1 starts + {teamSize} Number=2 starts | " +
                $"Player1-Player{playerCount} scenario entries synchronized.";
        }

        internal List<ScenarioPlayerStart> GetPlayableStartsV363(
            ScenarioMap map)
        {
            List<ScenarioPlayerStart> playable =
                map.PlayerStarts
                    .Where(
                        start =>
                            start.Player !=
                                7 &&
                            start.Number !=
                                7)
                    .ToList();

            if (playable.Count == 0)
                return playable;

            int expectedHalf =
                playable.Count /
                2;

            bool grouped =
                playable.Count
                    is 2 or 4 or 6 &&
                playable.All(
                    start =>
                        start.Number
                            is 1 or 2) &&
                playable.Count(
                    start =>
                        start.Number ==
                            1) ==
                    expectedHalf &&
                playable.Count(
                    start =>
                        start.Number ==
                            2) ==
                    expectedHalf;

            if (grouped)
            {
                return playable
                    .Where(
                        start =>
                            start.Number ==
                                1)
                    .Concat(
                        playable.Where(
                            start =>
                                start.Number ==
                                    2))
                    .ToList();
            }

            bool legacyUnique =
                playable.All(
                    start =>
                        start.Number >=
                            1 &&
                        start.Number <=
                            6) &&
                playable
                    .Select(
                        start =>
                            start.Number)
                    .Distinct()
                    .Count() ==
                playable.Count;

            if (legacyUnique)
            {
                return playable
                    .OrderBy(
                        start =>
                            start.Number)
                    .ToList();
            }

            return playable;
        }

        internal void EnsureSkirmishPlayerStructureV363(
            ScenarioMap map,
            int playerCount)
        {
            NormalizeSkirmishStartModelV363(
                map,
                playerCount);

            if (_currentScenarioOriginalXmbData ==
                null)
            {
                throw new InvalidOperationException(
                    "No editable scenario XMB is loaded.");
            }

            if (!PlayerStartStructureService
                    .IsSkirmishStructureSynchronized(
                        _currentScenarioOriginalXmbData,
                        playerCount))
            {
                _currentScenarioOriginalXmbData =
                    PlayerStartStructureService
                        .SynchronizeSkirmish(
                            _currentScenarioOriginalXmbData,
                            GetPlayableStartsV363(
                                map),
                            playerCount);

                _metadataDirty =
                    true;
            }
        }

        internal ScenarioPlayerStart? GetPlayerStartBySlotV363(
            ScenarioMap map,
            int slot)
        {
            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            return slot >= 1 &&
                   slot <= starts.Count
                ? starts[
                    slot - 1]
                : null;
        }

        internal int GetPlayerStartSlotV363(
            ScenarioMap map,
            ScenarioPlayerStart start)
        {
            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            int index =
                starts.FindIndex(
                    candidate =>
                        ReferenceEquals(
                            candidate,
                            start));

            return index >= 0
                ? index + 1
                : 0;
        }

        internal int GetStartGroupNumberV363(
            int slot,
            int playerCount)
        {
            if (playerCount
                    is not (2 or 4 or 6) ||
                slot < 1 ||
                slot > playerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot));
            }

            return slot <=
                   playerCount / 2
                ? 1
                : 2;
        }

        internal void NormalizeSkirmishStartModelV363(
            ScenarioMap map,
            int playerCount)
        {
            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            if (starts.Count !=
                playerCount)
            {
                throw new InvalidOperationException(
                    $"The editor currently has {starts.Count} playable start(s), but this map is configured for {playerCount} players.");
            }

            NormalizeSkirmishStartModelV363(
                starts,
                playerCount);

            // Keep the in-memory list in editor-slot order. Observer Position 7
            // is intentionally not represented by ScenarioMap after v36.3.
            map.PlayerStarts.Clear();

            map.PlayerStarts.AddRange(
                starts);
        }

        private static void NormalizeSkirmishStartModelV363(
            IReadOnlyList<ScenarioPlayerStart> starts,
            int playerCount)
        {
            int teamSize =
                playerCount /
                2;

            for (int i = 0;
                 i < starts.Count;
                 i++)
            {
                starts[i].Player =
                    -1;

                starts[i].Number =
                    i < teamSize
                        ? 1
                        : 2;

                starts[i].Forward =
                    NormaliseForwardV33(
                        starts[i].Forward);
            }
        }

        private void NormalizePlayerStartLabelsV363()
        {
            if (_ensemble3DViewport ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                return;
            }

            List<ScenarioPlayerStart> starts =
                GetPlayableStartsV363(
                    map);

            if (starts.Count == 0)
                return;

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

            for (int i = 0;
                 i < starts.Count;
                 i++)
            {
                ScenarioPlayerStart start =
                    starts[i];

                int slot =
                    i +
                    1;

                if (!itemModels.TryGetValue(
                        start,
                        out List<GeometryModel3D>? models))
                {
                    continue;
                }

                foreach (GeometryModel3D model
                         in models)
                {
                    if (model.Material
                        is not MaterialGroup group ||
                        !group.Children
                            .OfType<DiffuseMaterial>()
                            .Any(
                                diffuse =>
                                    diffuse.Brush
                                        is DrawingBrush))
                    {
                        continue;
                    }

                    if (model.Geometry
                            is MeshGeometry3D mesh &&
                        mesh.Positions.Count ==
                            8)
                    {
                        model.Geometry =
                            BuildSinglePlayerStartLabelPlaneV33(
                                mesh);
                    }

                    Material material =
                        CreatePlayerStartLabelMaterialV33(
                            "P" +
                            slot.ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                            GetPlayerStartSlotColorV33(
                                slot));

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
                _ensemble3DViewport.SelectItem(
                    _selectedScenarioItem);
            }
        }
    }
}
