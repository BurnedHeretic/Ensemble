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
    /// v35.9 restores the useful "Move Player Start Here" workflow without
    /// bringing back the old double-base bug.
    ///
    /// The command is intentionally available when another PlayerStart marker
    /// is selected. Choosing P1 while P2 is selected swaps the two transforms:
    /// P1 moves to P2's position and P2 moves to P1's old position. No base
    /// socket is created or consumed, so creators can rearrange starts after
    /// their base sites have already been converted.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _playerStartMoveBootstrapV359 =
                RegisterPlayerStartMoveV359();

        private bool
            _playerStartMoveInitializedV359;

        private MenuItem?
            _movePlayerStartHereMenuV359;

        private readonly Dictionary<int, MenuItem>
            _movePlayerStartItemsV359 =
                new();

        private static bool RegisterPlayerStartMoveV359()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    PlayerStartMoveV359_Loaded),
                true);

            return true;
        }

        private static void PlayerStartMoveV359_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._playerStartMoveInitializedV359)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializePlayerStartMoveV359));
        }

        private void InitializePlayerStartMoveV359()
        {
            if (_playerStartMoveInitializedV359)
                return;

            if (_playerStartsMenuV33 == null)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(
                        InitializePlayerStartMoveV359));

                return;
            }

            _playerStartMoveInitializedV359 =
                true;

            _playerStartsMenuV33.Items.Add(
                new Separator());

            _movePlayerStartHereMenuV359 =
                new MenuItem
                {
                    Header =
                        "_Move Player Start Here"
                };

            for (int player = 1;
                 player <= 6;
                 player++)
            {
                int capturedPlayer =
                    player;

                MenuItem item =
                    new MenuItem
                    {
                        Header =
                            $"Player _{player}",

                        ToolTip =
                            $"Move Player {player} to the selected player-start position. The two starts are swapped so no player slot is lost."
                    };

                item.Click +=
                    (
                        _,
                        _) =>
                        MovePlayerStartHereV359(
                            capturedPlayer);

                _movePlayerStartItemsV359[
                    player] =
                        item;

                _movePlayerStartHereMenuV359.Items.Add(
                    item);
            }

            _playerStartsMenuV33.Items.Add(
                _movePlayerStartHereMenuV359);

            _playerStartsMenuV33.SubmenuOpened +=
                PlayerStartMoveV359_SubmenuOpened;

            ScenarioMapCanvas.SelectionChanged +=
                PlayerStartMoveV359_SelectionChanged;

            Closed +=
                PlayerStartMoveV359_Closed;

            UpdatePlayerStartMoveMenuV359();
        }

        private void PlayerStartMoveV359_SubmenuOpened(
            object sender,
            RoutedEventArgs e)
        {
            UpdatePlayerStartMoveMenuV359();
        }

        private void PlayerStartMoveV359_SelectionChanged(
            object? sender,
            ScenarioSelectionChangedEventArgs e)
        {
            UpdatePlayerStartMoveMenuV359();
        }

        private void PlayerStartMoveV359_Closed(
            object? sender,
            EventArgs e)
        {
            if (_playerStartsMenuV33 != null)
            {
                _playerStartsMenuV33.SubmenuOpened -=
                    PlayerStartMoveV359_SubmenuOpened;
            }

            ScenarioMapCanvas.SelectionChanged -=
                PlayerStartMoveV359_SelectionChanged;

            Closed -=
                PlayerStartMoveV359_Closed;
        }

        private void UpdatePlayerStartMoveMenuV359()
        {
            if (_movePlayerStartHereMenuV359 == null)
                return;

            bool mapOpen =
                ScenarioMapCanvas.Scenario != null &&
                _currentScenarioOriginalXmbData != null;

            ScenarioPlayerStart? selected =
                _selectedScenarioItem
                    as ScenarioPlayerStart;

            _movePlayerStartHereMenuV359.IsEnabled =
                mapOpen &&
                selected != null;

            int playerCount =
                GetPlayerCountV363();

            int selectedSlot =
                selected != null &&
                ScenarioMapCanvas.Scenario
                    is ScenarioMap selectedMap
                    ? GetPlayerStartSlotV363(
                        selectedMap,
                        selected)
                    : 0;

            foreach (KeyValuePair<int, MenuItem> pair
                     in _movePlayerStartItemsV359)
            {
                pair.Value.IsEnabled =
                    mapOpen &&
                    selected != null &&
                    pair.Key <= playerCount &&
                    pair.Key != selectedSlot;
            }
        }

        private void MovePlayerStartHereV359(
            int playerNumber)
        {
            if (ScenarioMapCanvas.Scenario
                    is not ScenarioMap map ||
                _currentScenarioOriginalXmbData ==
                    null)
            {
                return;
            }

            if (_selectedScenarioItem
                is not ScenarioPlayerStart anchor)
            {
                MessageBox.Show(
                    this,
                    "Select an existing P1-P6 player-start marker in the viewport first.",
                    "Move Player Start Here",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            int playerCount =
                GetPlayerCountV363();

            EnsureSkirmishPlayerStructureV363(
                map,
                playerCount);

            int anchorSlot =
                GetPlayerStartSlotV363(
                    map,
                    anchor);

            ScenarioPlayerStart? moving =
                GetPlayerStartBySlotV363(
                    map,
                    playerNumber);

            if (moving == null)
            {
                MessageBox.Show(
                    this,
                    $"Player {playerNumber} does not exist in the current map's player-start table.",
                    "Move Player Start Here",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (ReferenceEquals(
                    moving,
                    anchor) ||
                anchorSlot <= 0)
            {
                return;
            }

            byte[] beforeXmb =
                _currentScenarioOriginalXmbData
                    .ToArray();

            Vector3 movingBeforePosition =
                moving.Position;

            Vector3 movingBeforeForward =
                moving.Forward;

            Vector3 anchorBeforePosition =
                anchor.Position;

            Vector3 anchorBeforeForward =
                anchor.Forward;

            // Swap complete spatial transforms rather than stacking two starts
            // at one point. This preserves the exact number of valid starts and
            // avoids requiring a temporary expansion/base socket.
            moving.Position =
                anchorBeforePosition;

            moving.Forward =
                NormaliseForwardV33(
                    anchorBeforeForward);

            anchor.Position =
                movingBeforePosition;

            anchor.Forward =
                NormaliseForwardV33(
                    movingBeforeForward);

            byte[] afterXmb;

            try
            {
                afterXmb =
                    PlayerStartStructureService
                        .SynchronizeSkirmish(
                            beforeXmb,
                            GetPlayableStartsV363(
                                map),
                            playerCount);
            }
            catch
            {
                moving.Position =
                    movingBeforePosition;

                moving.Forward =
                    movingBeforeForward;

                anchor.Position =
                    anchorBeforePosition;

                anchor.Forward =
                    anchorBeforeForward;

                throw;
            }

            _currentScenarioOriginalXmbData =
                afterXmb;

            _metadataDirty =
                true;

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            HighlightPlacedObjectNext(
                moving);

            PushEnsembleNextHistory(
                new PlayerStartSwapHistoryActionV359(
                    this,
                    moving,
                    anchor,
                    movingBeforePosition,
                    movingBeforeForward,
                    anchorBeforePosition,
                    anchorBeforeForward,
                    beforeXmb,
                    afterXmb,
                    playerNumber,
                    anchorSlot));

            UpdatePlayerStartMenuV33();
            UpdatePlayerStartMoveMenuV359();

            StatusText.Text =
                $"Swapped P{playerNumber} and P{anchorSlot} starting positions.";
        }

        private void RestorePlayerStartSwapV359(
            ScenarioPlayerStart first,
            ScenarioPlayerStart second,
            Vector3 firstPosition,
            Vector3 firstForward,
            Vector3 secondPosition,
            Vector3 secondForward,
            byte[] xmbData,
            ScenarioPlayerStart selection)
        {
            if (ScenarioMapCanvas.Scenario
                is not ScenarioMap map)
            {
                return;
            }

            _currentScenarioOriginalXmbData =
                xmbData.ToArray();

            first.Position =
                firstPosition;

            first.Forward =
                firstForward;

            second.Position =
                secondPosition;

            second.Forward =
                secondForward;

            RefreshScenarioAfterPlayerStartEditV33(
                map);

            HighlightPlacedObjectNext(
                selection);

            UpdatePlayerStartMenuV33();
            UpdatePlayerStartMoveMenuV359();
        }

        private sealed class PlayerStartSwapHistoryActionV359 :
            IScenarioHistoryAction
        {
            private readonly MainWindow
                _owner;

            private readonly ScenarioPlayerStart
                _moving;

            private readonly ScenarioPlayerStart
                _anchor;

            private readonly Vector3
                _movingBeforePosition;

            private readonly Vector3
                _movingBeforeForward;

            private readonly Vector3
                _anchorBeforePosition;

            private readonly Vector3
                _anchorBeforeForward;

            private readonly byte[]
                _beforeXmb;

            private readonly byte[]
                _afterXmb;

            private readonly int
                _movingSlot;

            private readonly int
                _anchorSlot;

            public PlayerStartSwapHistoryActionV359(
                MainWindow owner,
                ScenarioPlayerStart moving,
                ScenarioPlayerStart anchor,
                Vector3 movingBeforePosition,
                Vector3 movingBeforeForward,
                Vector3 anchorBeforePosition,
                Vector3 anchorBeforeForward,
                byte[] beforeXmb,
                byte[] afterXmb,
                int movingSlot,
                int anchorSlot)
            {
                _owner =
                    owner;

                _moving =
                    moving;

                _anchor =
                    anchor;

                _movingBeforePosition =
                    movingBeforePosition;

                _movingBeforeForward =
                    movingBeforeForward;

                _anchorBeforePosition =
                    anchorBeforePosition;

                _anchorBeforeForward =
                    anchorBeforeForward;

                _beforeXmb =
                    beforeXmb.ToArray();

                _afterXmb =
                    afterXmb.ToArray();

                _movingSlot =
                    movingSlot;

                _anchorSlot =
                    anchorSlot;

                BeforeRevisionId =
                    owner._currentRevisionId;

                AfterRevisionId =
                    ++owner._nextRevisionId;
            }

            public string Description =>
                $"Swap P{_movingSlot} and P{_anchorSlot} starts";

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
                    .RestorePlayerStartSwapV359(
                        _moving,
                        _anchor,
                        _movingBeforePosition,
                        _movingBeforeForward,
                        _anchorBeforePosition,
                        _anchorBeforeForward,
                        _beforeXmb,
                        _anchor);
            }

            public void Redo(
                MapCanvas canvas)
            {
                _owner
                    .RestorePlayerStartSwapV359(
                        _moving,
                        _anchor,
                        _anchorBeforePosition,
                        NormaliseForwardV33(
                            _anchorBeforeForward),
                        _movingBeforePosition,
                        NormaliseForwardV33(
                            _movingBeforeForward),
                        _afterXmb,
                        _moving);
            }
        }
    }
}
