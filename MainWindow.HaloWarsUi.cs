using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

        private long
            _lastLiveSculptRefreshMs;

        private MenuItem?
            _eraInfoToggleMenuItemV19;

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
            // its dynamic Objects / ERA Info / game-assets menu entries.
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

            // TerrainPreviewChanged is useful at stroke boundaries, but the
            // 2D terrain editor mutates the live height buffer while dragging.
            // Poll that live buffer during rendering so the 3D mesh visibly
            // follows the brush instead of waiting for save/reopen.
            CompositionTarget.Rendering +=
                HaloWarsUiV19_Rendering;

            Closed +=
                HaloWarsUiV19_Closed;
        }

        private void HaloWarsUiV19_Closed(
            object? sender,
            EventArgs e)
        {
            CompositionTarget.Rendering -=
                HaloWarsUiV19_Rendering;
        }

        private void HaloWarsUiV19_Rendering(
            object? sender,
            EventArgs e)
        {
            if (!ScenarioMapCanvas
                    .IsTerrainSculptActive
                ||
                ScenarioMapCanvas
                    .TerrainHeightMap ==
                    null
                ||
                _ensemble3DViewport ==
                    null)
            {
                return;
            }

            long now =
                Environment.TickCount64;

            // Rebuilding the WPF 3D terrain on every compositor frame is
            // unnecessarily expensive. ~15 FPS is smooth enough for a sculpt
            // preview while keeping object/camera interaction responsive.
            if (now -
                    _lastLiveSculptRefreshMs <
                66)
            {
                return;
            }

            _lastLiveSculptRefreshMs =
                now;

            _ensemble3DViewport.RefreshTerrain(
                ScenarioMapCanvas
                    .TerrainHeightMap);
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
