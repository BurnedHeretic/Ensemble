using Ensemble.Controls;
using Ensemble.Models;
using Ensemble.Services;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v35.8 player-start workflow cleanup.
    ///
    /// A Halo Wars starting base and an expansion/base socket must not occupy
    /// the same location.  The older v33 workflow moved a PlayerStart onto the
    /// selected socket but left the socket ScenarioObject behind, which caused
    /// the game to spawn a starting base over a second empty base site.
    ///
    /// This layer replaces the visible Player Starts menu with one unambiguous
    /// operation:
    ///
    ///     Convert Selected Base to Player Start > P1..P6
    ///
    /// Conversion consumes the selected GAME_BASE_SOCKET-style ScenarioObject,
    /// updates the real Scenario/Positions/Position record, and records the
    /// operation as one undo/redo history action.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _playerStartConversionBootstrapV358 =
                RegisterPlayerStartConversionV358();

        private bool
            _playerStartConversionInitializedV358;

        private MenuItem?
            _convertBaseToPlayerStartMenuV358;

        private readonly Dictionary<int, MenuItem>
            _convertBasePlayerItemsV358 =
                new();

        private static bool RegisterPlayerStartConversionV358()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    PlayerStartConversionV358_Loaded),
                true);

            return true;
        }

        private static void PlayerStartConversionV358_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._playerStartConversionInitializedV358)
            {
                return;
            }

            // v33 builds its dynamic Player Starts menu on a deferred normal-
            // priority dispatcher call. Run after that so this patch can replace
            // its old Assign/Summary UI without racing initialization.
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializePlayerStartConversionV358));
        }

        private void InitializePlayerStartConversionV358()
        {
            if (_playerStartConversionInitializedV358)
                return;

            Menu? menu =
                FindVisualDescendantV19<Menu>(
                    this);

            if (menu == null)
                return;

            MenuItem? mapMenu =
                menu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(
                        item =>
                            NormalizeMenuHeaderV33(
                                item.Header) ==
                            "Map");

            if (mapMenu == null)
                return;

            MenuItem? playerStartsMenu =
                _playerStartsMenuV33
                ??
                mapMenu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(
                        item =>
                            NormalizeMenuHeaderV33(
                                item.Header) ==
                            "Player Starts");

            if (playerStartsMenu == null)
            {
                // Initialization order can differ slightly between WPF hosts.
                // Try once more on the next idle turn rather than creating a
                // duplicate Player Starts top-level menu.
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(
                        InitializePlayerStartConversionV358));

                return;
            }

            _playerStartConversionInitializedV358 =
                true;

            _playerStartsMenuV33 =
                playerStartsMenu;

            // Remove the obsolete:
            //   Assign Selected Base
            //   Player Start Summary...
            // menu entries. There is deliberately no "Move Player Start Here"
            // path: selecting a starting base always consumes its base socket.
            playerStartsMenu.Items.Clear();

            _convertBaseToPlayerStartMenuV358 =
                new MenuItem
                {
                    Header =
                        "_Convert Selected Base to Player Start"
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

                        ToolTip =
                            $"Consume the selected base socket and use its transform as Player {player}'s starting base."
                    };

                playerItem.Click +=
                    (
                        _,
                        _) =>
                        ConvertSelectedBaseToPlayerStartV358(
                            capturedPlayer);

                _convertBasePlayerItemsV358[
                    player] =
                        playerItem;

                _convertBaseToPlayerStartMenuV358
                    .Items
                    .Add(
                        playerItem);
            }

            playerStartsMenu.Items.Add(
                _convertBaseToPlayerStartMenuV358);

            playerStartsMenu.SubmenuOpened +=
                PlayerStartConversionV358_SubmenuOpened;

            ScenarioMapCanvas.SelectionChanged +=
                PlayerStartConversionV358_SelectionChanged;

            Closed +=
                PlayerStartConversionV358_Closed;

            UpdatePlayerStartConversionMenuV358();
        }

        private void PlayerStartConversionV358_SubmenuOpened(
            object sender,
            RoutedEventArgs e)
        {
            UpdatePlayerStartConversionMenuV358();
        }

        private void PlayerStartConversionV358_SelectionChanged(
            object? sender,
            ScenarioSelectionChangedEventArgs e)
        {
            UpdatePlayerStartConversionMenuV358();
        }

        private void PlayerStartConversionV358_Closed(
            object? sender,
            EventArgs e)
        {
            if (_playerStartsMenuV33 != null)
            {
                _playerStartsMenuV33.SubmenuOpened -=
                    PlayerStartConversionV358_SubmenuOpened;
            }

            ScenarioMapCanvas.SelectionChanged -=
                PlayerStartConversionV358_SelectionChanged;

            Closed -=
                PlayerStartConversionV358_Closed;
        }

        private void UpdatePlayerStartConversionMenuV358()
        {
            if (_playerStartsMenuV33 == null ||
                _convertBaseToPlayerStartMenuV358 == null)
            {
                return;
            }

            bool mapOpen =
                ScenarioMapCanvas.Scenario != null &&
                _currentScenarioOriginalXmbData != null;

            bool convertibleBaseSelected =
                _selectedScenarioItem
                    is ScenarioObject selected &&
                IsConvertibleBaseSlotV358(
                    selected);

            _playerStartsMenuV33.IsEnabled =
                mapOpen;

            _convertBaseToPlayerStartMenuV358.IsEnabled =
                mapOpen &&
                convertibleBaseSelected;

            int playerCount =
                GetPlayerCountV33();

            foreach (KeyValuePair<int, MenuItem> pair
                     in _convertBasePlayerItemsV358)
            {
                pair.Value.IsEnabled =
                    mapOpen &&
                    convertibleBaseSelected &&
                    pair.Key <=
                        playerCount;
            }
        }

        private void ConvertSelectedBaseToPlayerStartV358(
            int playerNumber)
        {
            if (ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                _currentScenarioOriginalXmbData ==
                    null)
            {
                MessageBox.Show(
                    this,
                    "Open an editable Halo Wars map before converting a base slot.",
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

            if (_selectedScenarioItem
                    is not ScenarioObject baseSlot ||
                !IsConvertibleBaseSlotV358(
                    baseSlot))
            {
                MessageBox.Show(
                    this,
                    "Select a Halo Wars base socket/site in the viewport first.\n\n" +
                    "Converting a player start now always consumes the selected base slot, so ordinary scenery and existing player-start markers are intentionally not accepted.",
                    "Convert Base to Player Start",
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

            if (start == null)
            {
                MessageBox.Show(
                    this,
                    $"Player {playerNumber} has no Position entry in the current scenario.\n\n" +
                    "Set the map's Player Count again in Map Metadata, then retry the conversion.",
                    "Missing Player Start",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            Vector3 targetPosition =
                baseSlot.Position;

            Vector3 targetForward =
                NormaliseForwardV33(
                    baseSlot.Forward);

            // Prevent two active starting players from being bound to one
            // socket. Unassigned/default Position markers do not count.
            ScenarioPlayerStart? occupiedBy =
                map.PlayerStarts
                    .FirstOrDefault(
                        item =>
                            item.Number !=
                                playerNumber &&
                            item.Player ==
                                item.Number &&
                            Vector3.DistanceSquared(
                                item.Position,
                                targetPosition) <
                            0.25f);

            if (occupiedBy != null)
            {
                MessageBox.Show(
                    this,
                    $"This base is already being used by Player {occupiedBy.Number}.",
                    "Base Already Converted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            byte[] beforeXmb =
                _currentScenarioOriginalXmbData
                    .ToArray();

            int beforePlayer =
                start.Player;

            Vector3 beforePosition =
                start.Position;

            Vector3 beforeForward =
                start.Forward;

            int baseIndex =
                map.Objects.IndexOf(
                    baseSlot);

            if (baseIndex < 0)
            {
                MessageBox.Show(
                    this,
                    "The selected base socket is no longer present in the scenario object list.",
                    "Convert Base to Player Start",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            start.Player =
                playerNumber;

            start.Position =
                targetPosition;

            start.Forward =
                targetForward;

            byte[] afterXmb;

            try
            {
                afterXmb =
                    PlayerStartStructureService
                        .Synchronize(
                            beforeXmb,
                            map.PlayerStarts);
            }
            catch
            {
                // Do not leave the live model half-converted if the packed XMB
                // structural writer rejects an unusual scenario.
                start.Player =
                    beforePlayer;

                start.Position =
                    beforePosition;

                start.Forward =
                    beforeForward;

                throw;
            }

            _currentScenarioOriginalXmbData =
                afterXmb;

            // Consume the expansion/base socket. Do this directly rather than
            // calling DeleteScenarioObjectFromEditor: that method creates a
            // deletion-only history action, while this conversion must undo and
            // redo the PlayerStart transform and socket removal atomically.
            map.Objects.Remove(
                baseSlot);

            if (!baseSlot.IsNewObject)
            {
                map.DeletedObjectIds.Add(
                    baseSlot.Id);
            }

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            HighlightPlacedObjectNext(
                start);

            PushEnsembleNextHistory(
                new PlayerStartConversionHistoryActionV358(
                    this,
                    baseSlot,
                    baseIndex,
                    start,
                    beforePlayer,
                    beforePosition,
                    beforeForward,
                    playerNumber,
                    targetPosition,
                    targetForward,
                    beforeXmb,
                    afterXmb));

            UpdatePlayerStartMenuV33();
            UpdatePlayerStartConversionMenuV358();

            string displayName =
                string.IsNullOrWhiteSpace(
                    baseSlot.EditorName)
                    ? baseSlot.Type
                    : baseSlot.EditorName;

            StatusText.Text =
                $"Converted {displayName} to Player {playerNumber} start | " +
                $"base socket removed | " +
                $"X {targetPosition.X:0.##}, " +
                $"Y {targetPosition.Y:0.##}, " +
                $"Z {targetPosition.Z:0.##}";
        }

        private static bool IsConvertibleBaseSlotV358(
            ScenarioObject obj)
        {
            // ScenarioObject.Category already recognizes GAME_BASE_SOCKET.
            // Keep the older name/type heuristic as a compatibility fallback
            // for modded/custom base-site prototypes.
            return obj.Category.Equals(
                       "Base",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   LooksLikeBaseSlotV33(
                       obj);
        }

        private void RestorePlayerStartConversionStateV358(
            ScenarioObject baseSlot,
            int baseIndex,
            ScenarioPlayerStart start,
            int player,
            Vector3 position,
            Vector3 forward,
            byte[] xmbData,
            bool converted)
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            _currentScenarioOriginalXmbData =
                xmbData.ToArray();

            start.Player =
                player;

            start.Position =
                position;

            start.Forward =
                forward;

            if (converted)
            {
                map.Objects.Remove(
                    baseSlot);

                if (!baseSlot.IsNewObject)
                {
                    map.DeletedObjectIds.Add(
                        baseSlot.Id);
                }
            }
            else
            {
                if (!map.Objects.Contains(
                        baseSlot))
                {
                    map.Objects.Insert(
                        Math.Clamp(
                            baseIndex,
                            0,
                            map.Objects.Count),
                        baseSlot);
                }

                if (!baseSlot.IsNewObject)
                {
                    map.DeletedObjectIds.Remove(
                        baseSlot.Id);
                }
            }

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            HighlightPlacedObjectNext(
                converted
                    ? start
                    : baseSlot);

            UpdatePlayerStartMenuV33();
            UpdatePlayerStartConversionMenuV358();
        }

        private sealed class PlayerStartConversionHistoryActionV358 :
            IScenarioHistoryAction
        {
            private readonly MainWindow
                _owner;

            private readonly ScenarioObject
                _baseSlot;

            private readonly int
                _baseIndex;

            private readonly ScenarioPlayerStart
                _start;

            private readonly int
                _beforePlayer;

            private readonly Vector3
                _beforePosition;

            private readonly Vector3
                _beforeForward;

            private readonly int
                _afterPlayer;

            private readonly Vector3
                _afterPosition;

            private readonly Vector3
                _afterForward;

            private readonly byte[]
                _beforeXmb;

            private readonly byte[]
                _afterXmb;

            public PlayerStartConversionHistoryActionV358(
                MainWindow owner,
                ScenarioObject baseSlot,
                int baseIndex,
                ScenarioPlayerStart start,
                int beforePlayer,
                Vector3 beforePosition,
                Vector3 beforeForward,
                int afterPlayer,
                Vector3 afterPosition,
                Vector3 afterForward,
                byte[] beforeXmb,
                byte[] afterXmb)
            {
                _owner =
                    owner;

                _baseSlot =
                    baseSlot;

                _baseIndex =
                    baseIndex;

                _start =
                    start;

                _beforePlayer =
                    beforePlayer;

                _beforePosition =
                    beforePosition;

                _beforeForward =
                    beforeForward;

                _afterPlayer =
                    afterPlayer;

                _afterPosition =
                    afterPosition;

                _afterForward =
                    afterForward;

                _beforeXmb =
                    beforeXmb.ToArray();

                _afterXmb =
                    afterXmb.ToArray();

                BeforeRevisionId =
                    owner._currentRevisionId;

                AfterRevisionId =
                    ++owner._nextRevisionId;
            }

            public string Description =>
                $"Convert base to P{_afterPlayer} start";

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
                    .RestorePlayerStartConversionStateV358(
                        _baseSlot,
                        _baseIndex,
                        _start,
                        _beforePlayer,
                        _beforePosition,
                        _beforeForward,
                        _beforeXmb,
                        converted: false);
            }

            public void Redo(
                MapCanvas canvas)
            {
                _owner
                    .RestorePlayerStartConversionStateV358(
                        _baseSlot,
                        _baseIndex,
                        _start,
                        _afterPlayer,
                        _afterPosition,
                        _afterForward,
                        _afterXmb,
                        converted: true);
            }
        }
    }
}
