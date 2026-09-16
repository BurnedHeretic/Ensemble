using Ensemble.Controls;
using Ensemble.Models;
using Ensemble.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ensemble
{
    public partial class MainWindow
    {
        // Static field initializers run before MainWindow's existing static
        // constructor body.  Registering here lets us safely consume Delete
        // for Ensemble custom meshes before the legacy instance key handler
        // calls its generic SC2 deletion path.
        private static readonly bool
            _customMeshSafetyHooksRegistered =
                RegisterCustomMeshSafetyHooks();

        private bool
            _customMeshPreviewHooksInitialized;

        private static bool RegisterCustomMeshSafetyHooks()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(
                    CustomMeshSafety_PreviewKeyDown),
                true);

            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    CustomMeshSafety_Loaded),
                true);

            return true;
        }

        private static void CustomMeshSafety_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            // EnsembleNext's Loaded class handler creates the 3D viewport.
            // Queue this one turn later so all cumulative UI layers exist.
            window.Dispatcher.BeginInvoke(
                new Action(
                    window.InitializeCustomMeshPreviewHooks));
        }

        private static void CustomMeshSafety_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Handled ||
                sender is not MainWindow window ||
                e.Key != Key.Delete ||
                Keyboard.Modifiers != ModifierKeys.None ||
                Keyboard.FocusedElement is TextBox ||
                window._selectedScenarioItem is not ScenarioArtObject artObject)
            {
                return;
            }

            try
            {
                if (!window.TryDeleteCustomMeshPlacementFromKeyboard(
                        artObject))
                {
                    return;
                }

                e.Handled = true;

                window.Dispatcher.BeginInvoke(
                    new Action(
                        window.ApplyCustomMeshViewportPreviews));
            }
            catch (Exception ex)
            {
                // Delete is a keyboard command; without this guard an SC2
                // structural failure can escape the routed event and close the
                // application.  Keep the editor alive and report the problem.
                e.Handled = true;

                MessageBox.Show(
                    window,
                    ex.ToString(),
                    "Custom Mesh Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                window.StatusText.Text =
                    "Custom mesh deletion failed; no further delete action was run.";
            }
        }

        private void InitializeCustomMeshPreviewHooks()
        {
            if (_customMeshPreviewHooksInitialized)
                return;

            _customMeshPreviewHooksInitialized =
                true;

            ScenarioMapCanvas.ItemMoved +=
                (_, _) =>
                    ScheduleCustomMeshViewportPreviewRefresh();

            ScenarioMapCanvas.ItemRotated +=
                (_, _) =>
                    ScheduleCustomMeshViewportPreviewRefresh();

            ScenarioMapCanvas.ObjectAdded +=
                (_, _) =>
                    ScheduleCustomMeshViewportPreviewRefresh();

            ScenarioMapCanvas.ObjectDeleted +=
                (_, _) =>
                    ScheduleCustomMeshViewportPreviewRefresh();

            // MapViewport3D has its own direct LMB drag path. EnsembleNext's
            // handler commits the position and then rebuilds the object layer,
            // which temporarily restores the stock donor UGX. Register after
            // EnsembleNext so our deferred callback re-applies the generated
            // custom BSP/mesh once that rebuild has completed.
            if (_ensemble3DViewport != null)
            {
                _ensemble3DViewport.ItemMoved +=
                    (_, _) =>
                        ScheduleCustomMeshViewportPreviewRefresh();
            }

            ScheduleCustomMeshViewportPreviewRefresh();
        }

        private void ScheduleCustomMeshViewportPreviewRefresh()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    ApplyCustomMeshViewportPreviews));
        }

        internal void ApplyCustomMeshViewportPreviews()
        {
            if (_ensemble3DViewport == null ||
                _currentScenarioChunk == null)
            {
                return;
            }

            CustomMeshViewportPreviewService.Apply(
                _ensemble3DViewport,
                _currentScenarioChunk.FileName,
                _currentArtObjects,
                _selectedScenarioItem);
        }

        internal float? GetCustomMeshScaleForGizmoV356(
            object item)
        {
            if (item is not ScenarioArtObject artObject ||
                _currentScenarioChunk == null)
            {
                return null;
            }

            return CustomMeshViewportPreviewService.TryGetScale(
                    _currentScenarioChunk.FileName,
                    artObject.Id,
                    out float scale)
                ? scale
                : null;
        }

        internal void SetCustomMeshScaleForGizmoV356(
            object item,
            float scale)
        {
            if (item is not ScenarioArtObject artObject ||
                _currentScenarioChunk == null)
            {
                return;
            }

            if (CustomMeshViewportPreviewService.SetPreviewScale(
                    _currentScenarioChunk.FileName,
                    artObject.Id,
                    scale))
            {
                ApplyCustomMeshViewportPreviews();
            }
        }

        internal void CommitCustomMeshScaleFromGizmoV356(
            ScenarioItemScaledEventArgs e)
        {
            if (e.Item is not ScenarioArtObject artObject ||
                _currentScenarioChunk == null)
            {
                return;
            }

            try
            {
                if (!CustomMeshViewportPreviewService.CommitScale(
                        _currentScenarioChunk.FileName,
                        artObject.Id,
                        e.NewScale,
                        out int triangleCount,
                        out int sectionCount))
                {
                    // A saved/reopened custom mesh may no longer have the
                    // session template required for safe recompilation. Keep
                    // the visible object at its previous committed scale.
                    CustomMeshViewportPreviewService.SetPreviewScale(
                        _currentScenarioChunk.FileName,
                        artObject.Id,
                        e.OldScale);

                    ApplyCustomMeshViewportPreviews();

                    StatusText.Text =
                        "Scale is available for custom meshes that still have an active Ensemble import session.";

                    return;
                }

                // Scaling changes the queued UGX rather than an SC2 attribute.
                // Mark the document dirty so a Ctrl+S definitely re-embeds it,
                // including after an earlier save in this same editor session.
                _artObjectsDirty = true;
                UpdateDirtyState();

                ApplyCustomMeshViewportPreviews();

                StatusText.Text =
                    $"Custom mesh scale {e.NewScale:0.###}x | " +
                    $"{triangleCount:N0} tris | {sectionCount:N0} UGX section(s) | save required";
            }
            catch (Exception ex)
            {
                CustomMeshViewportPreviewService.SetPreviewScale(
                    _currentScenarioChunk.FileName,
                    artObject.Id,
                    e.OldScale);

                ApplyCustomMeshViewportPreviews();

                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Custom Mesh Scale Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Custom mesh scale failed; the previous scale was restored.";
            }
        }
    }
}
