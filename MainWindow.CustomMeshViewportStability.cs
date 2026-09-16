using Ensemble.Controls;
using Ensemble.Models;
using System.Windows;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v35.7 safety layer for live custom-mesh / imported-BSP viewport updates.
    ///
    /// MapViewport3D rebuilds its stock object layer when an item is dragged.
    /// A custom mesh is represented in that stock layer by the borrowed donor
    /// UGX slot, then CustomMeshViewportPreviewService replaces that visual
    /// with Ensemble's generated mesh. If the preview hook was registered
    /// before the 3D viewport existed, the direct MapViewport3D.ItemMoved path
    /// could miss that replacement and the imported BSP would appear to vanish
    /// until Ensemble was restarted.
    ///
    /// This hook waits for the real 3D viewport, attaches to its direct move
    /// event, reapplies the generated mesh immediately after the stock rebuild,
    /// then performs one deferred pass after layout/input work has settled.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _customMeshViewportStabilityBootstrapV357 =
                RegisterCustomMeshViewportStabilityV357();

        private bool
            _customMeshViewportStabilityInitializedV357;

        private bool
            _customMeshViewportStabilityHookedV357;

        private int
            _customMeshViewportRefreshGenerationV357;

        private DispatcherTimer?
            _customMeshViewportHookTimerV357;

        private int
            _customMeshViewportHookAttemptsV357;

        private static bool RegisterCustomMeshViewportStabilityV357()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    CustomMeshViewportStabilityV357_Loaded),
                true);

            return true;
        }

        private static void CustomMeshViewportStabilityV357_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._customMeshViewportStabilityInitializedV357)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(
                    window.InitializeCustomMeshViewportStabilityV357));
        }

        private void InitializeCustomMeshViewportStabilityV357()
        {
            if (_customMeshViewportStabilityInitializedV357)
                return;

            _customMeshViewportStabilityInitializedV357 = true;

            EnsureCustomMeshViewportMoveHookV357();

            if (!_customMeshViewportStabilityHookedV357)
            {
                _customMeshViewportHookTimerV357 =
                    new DispatcherTimer(
                        DispatcherPriority.Background)
                    {
                        Interval =
                            TimeSpan.FromMilliseconds(100)
                    };

                _customMeshViewportHookTimerV357.Tick +=
                    CustomMeshViewportHookTimerV357_Tick;

                _customMeshViewportHookTimerV357.Start();
            }

            Closed +=
                CustomMeshViewportStabilityV357_Closed;
        }

        private void CustomMeshViewportHookTimerV357_Tick(
            object? sender,
            EventArgs e)
        {
            _customMeshViewportHookAttemptsV357++;

            EnsureCustomMeshViewportMoveHookV357();

            if (_customMeshViewportStabilityHookedV357 ||
                _customMeshViewportHookAttemptsV357 >= 50)
            {
                StopCustomMeshViewportHookTimerV357();
            }
        }

        private void EnsureCustomMeshViewportMoveHookV357()
        {
            if (_customMeshViewportStabilityHookedV357 ||
                _ensemble3DViewport == null)
            {
                return;
            }

            _ensemble3DViewport.ItemMoved +=
                CustomMeshViewportStabilityV357_ItemMoved;

            _customMeshViewportStabilityHookedV357 = true;
        }

        private void CustomMeshViewportStabilityV357_ItemMoved(
            object? sender,
            ScenarioItemMovedEventArgs e)
        {
            ReapplyCustomMeshViewportAfterRefreshV357();
        }

        private void ReapplyCustomMeshViewportAfterRefreshV357()
        {
            EnsureCustomMeshViewportMoveHookV357();

            int generation =
                ++_customMeshViewportRefreshGenerationV357;

            // The normal MapViewport3D.ItemMoved subscriber was registered when
            // the viewport was created, before this stability hook. Its
            // RefreshObjects call therefore runs first. Replace the stock donor
            // visual immediately so the imported BSP stays visible.
            ApplyCustomMeshViewportPreviews();
            _ensemble3DViewport?.InvalidateVisual();

            // One deferred pass wins over any other UI/layout refresh queued by
            // the move commit without continually rebuilding the preview.
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    () =>
                    {
                        if (generation !=
                            _customMeshViewportRefreshGenerationV357)
                        {
                            return;
                        }

                        ApplyCustomMeshViewportPreviews();
                        _ensemble3DViewport?.InvalidateVisual();
                    }));
        }

        private void StopCustomMeshViewportHookTimerV357()
        {
            if (_customMeshViewportHookTimerV357 == null)
                return;

            _customMeshViewportHookTimerV357.Stop();
            _customMeshViewportHookTimerV357.Tick -=
                CustomMeshViewportHookTimerV357_Tick;

            _customMeshViewportHookTimerV357 = null;
        }

        private void CustomMeshViewportStabilityV357_Closed(
            object? sender,
            EventArgs e)
        {
            StopCustomMeshViewportHookTimerV357();

            if (_customMeshViewportStabilityHookedV357 &&
                _ensemble3DViewport != null)
            {
                _ensemble3DViewport.ItemMoved -=
                    CustomMeshViewportStabilityV357_ItemMoved;
            }

            _customMeshViewportStabilityHookedV357 = false;

            Closed -=
                CustomMeshViewportStabilityV357_Closed;
        }
    }
}
