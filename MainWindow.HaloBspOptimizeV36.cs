using Ensemble.Models;
using Ensemble.Services;
using System.Numerics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v36 first Halo BSP runtime-integration pass.
    ///
    /// The native Halo import remains one object while it is being authored:
    /// creators can move, rotate and uniformly scale it with the normal gizmo.
    /// When the transform is final, "Optimize Selected Halo BSP" spatially
    /// partitions that one mesh into multiple compact UGX ArtObjects and
    /// replaces the original. Each chunk receives a world-space origin near its
    /// own geometry rather than sharing one map-sized origin.
    ///
    /// This keeps authoring convenient while addressing the in-game culling
    /// behaviour observed with a giant single ArtObject.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _haloBspOptimizeBootstrapV36 =
                RegisterHaloBspOptimizeV36();

        private bool
            _haloBspOptimizeInitializedV36;

        private MenuItem?
            _optimizeHaloBspMenuItemV36;

        private static bool RegisterHaloBspOptimizeV36()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    HaloBspOptimizeV36_Loaded),
                true);

            return true;
        }

        private static void HaloBspOptimizeV36_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._haloBspOptimizeInitializedV36)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializeHaloBspOptimizeV36));
        }

        private void InitializeHaloBspOptimizeV36()
        {
            if (_haloBspOptimizeInitializedV36)
                return;

            Menu? menu =
                FindVisualDescendantV19<Menu>(
                    this);

            if (menu == null)
                return;

            MenuItem? objectsMenu =
                menu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(
                        item =>
                            NormalizeMenuHeaderV33(
                                item.Header) ==
                            "Objects");

            if (objectsMenu == null)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(
                        InitializeHaloBspOptimizeV36));

                return;
            }

            _haloBspOptimizeInitializedV36 =
                true;

            // v36 clarifies the role of the current .map importer. It is a
            // VISUAL BSP importer; gameplay terrain will be a separate pipeline.
            if (_haloMapImportMenuItemV34 !=
                null)
            {
                _haloMapImportMenuItemV34.Header =
                    "Import Halo _BSP...";
            }

            if (objectsMenu.Items.Count >
                0)
            {
                objectsMenu.Items.Add(
                    new Separator());
            }

            _optimizeHaloBspMenuItemV36 =
                new MenuItem
                {
                    Header =
                        "_Optimize Selected Halo BSP..."
                };

            _optimizeHaloBspMenuItemV36.Click +=
                OptimizeHaloBspV36_Click;

            objectsMenu.Items.Add(
                _optimizeHaloBspMenuItemV36);

            ScenarioMapCanvas.SelectionChanged +=
                HaloBspOptimizeV36_SelectionChanged;

            Closed +=
                HaloBspOptimizeV36_Closed;

            UpdateHaloBspOptimizeMenuV36();
        }

        private void HaloBspOptimizeV36_SelectionChanged(
            object? sender,
            Ensemble.Controls.ScenarioSelectionChangedEventArgs e)
        {
            UpdateHaloBspOptimizeMenuV36();
        }

        private void HaloBspOptimizeV36_Closed(
            object? sender,
            EventArgs e)
        {
            ScenarioMapCanvas.SelectionChanged -=
                HaloBspOptimizeV36_SelectionChanged;

            if (_optimizeHaloBspMenuItemV36 !=
                null)
            {
                _optimizeHaloBspMenuItemV36.Click -=
                    OptimizeHaloBspV36_Click;
            }

            Closed -=
                HaloBspOptimizeV36_Closed;
        }

        private void UpdateHaloBspOptimizeMenuV36()
        {
            if (_optimizeHaloBspMenuItemV36 ==
                null)
            {
                return;
            }

            _optimizeHaloBspMenuItemV36.IsEnabled =
                _selectedScenarioItem
                    is ScenarioArtObject &&
                _currentScenarioChunk !=
                    null;
        }

        private async void OptimizeHaloBspV36_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedScenarioItem
                    is not ScenarioArtObject original ||
                _currentScenarioChunk ==
                    null ||
                _currentArchive ==
                    null ||
                ScenarioMapCanvas.Scenario ==
                    null)
            {
                return;
            }

            if (!TryResolveActiveHaloBspSourceV36(
                    original,
                    out ImportedMeshEntry? sourceMesh,
                    out float committedScale,
                    out string reason) ||
                sourceMesh ==
                    null)
            {
                MessageBox.Show(
                    this,
                    reason,
                    "Optimize Halo BSP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            HaloBspOptimizeWindowV36 options =
                new HaloBspOptimizeWindowV36(
                    sourceMesh.DisplayName,
                    committedScale)
                {
                    Owner =
                        this
                };

            if (options.ShowDialog() !=
                true)
            {
                return;
            }

            MessageBoxResult confirm =
                MessageBox.Show(
                    this,
                    "FINALIZE HALO BSP FOR HALO WARS\n\n" +
                    $"Mesh: {sourceMesh.DisplayName}\n" +
                    $"Grid: {options.CellsPerAxis} × {options.CellsPerAxis}\n" +
                    $"Current committed scale: {committedScale:0.###}x\n\n" +
                    "The selected single BSP object will remain untouched until all spatial chunks have been generated and placed successfully. After that, Ensemble will remove the original single BSP placement.\n\n" +
                    "Continue?",
                    "Finalize Halo BSP",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirm !=
                MessageBoxResult.Yes)
            {
                return;
            }

            string tempDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    "Ensemble",
                    "HaloBspChunks",
                    Guid.NewGuid()
                        .ToString("N"));

            Directory.CreateDirectory(
                tempDirectory);

            bool oldEnabled =
                IsEnabled;

            int placedCount =
                0;

            try
            {
                IsEnabled =
                    false;

                StatusText.Text =
                    "Partitioning Halo BSP into culling-friendly spatial cells...";

                IReadOnlyList<HaloBspChunkService.Chunk> chunks =
                    await Task.Run(
                        () =>
                            HaloBspChunkService
                                .SplitGeneratedHaloObj(
                                    sourceMesh.LibraryFilePath,
                                    tempDirectory,
                                    options.CellsPerAxis,
                                    committedScale));

                StatusText.Text =
                    $"Preparing {chunks.Count:N0} non-empty Halo BSP chunk(s)...";

                GameObjectCatalogLoadResult library =
                    await Task.Run(
                        () =>
                            GameObjectCatalogService
                                .LoadGameLibrary(
                                    _currentArchive.FilePath));

                Vector3 originalPosition =
                    original.Position;

                Vector3 originalForward =
                    original.Forward;

                Vector3 originalRight =
                    original.Right;

                long totalTriangles =
                    0;

                foreach (HaloBspChunkService.Chunk chunk
                         in chunks)
                {
                    StatusText.Text =
                        $"Compiling Halo BSP chunk {placedCount + 1}/{chunks.Count}...";

                    ImportedMeshEntry imported =
                        CustomMeshImportService
                            .Import(
                                chunk.ObjPath);

                    ObjectCatalogEntry importedEntry =
                        new ObjectCatalogEntry
                        {
                            Layer =
                                ObjectCatalogLayer.ImportedMesh,

                            DonorEraPath =
                                string.Empty,

                            SourceFileName =
                                imported.FileName,

                            Id =
                                0,

                            Name =
                                $"Halo BSP Chunk {chunk.CellX},{chunk.CellZ}",

                            InternalName =
                                imported.DisplayName,

                            Type =
                                "OBJ mesh",

                            ImportedMesh =
                                imported
                        };

                    Vector3 worldChunkPosition =
                        originalPosition +
                        RotateLocalOffsetForForwardV36(
                            chunk.LocalOrigin,
                            originalForward);

                    PlaceImportedHaloBspChunkV36(
                        importedEntry,
                        library.Entries,
                        library.Archives,
                        worldChunkPosition,
                        originalForward,
                        originalRight);

                    placedCount++;
                    totalTriangles +=
                        chunk.TriangleCount;
                }

                // Do not consume the editable source until every requested
                // spatial cell is safely present in the current SC2 state.
                if (!TryDeleteCustomMeshPlacementFromKeyboard(
                        original))
                {
                    throw new InvalidOperationException(
                        "All Halo BSP chunks were placed, but Ensemble could not retire the original single BSP custom-mesh placement.");
                }

                RefreshArtObjectLayer();
                Refresh3DViewportNext(
                    false);
                ApplyCustomMeshViewportPreviews();
                UpdateDirtyState();

                StatusText.Text =
                    $"Halo BSP finalized | {placedCount:N0} spatial UGX chunks | " +
                    $"{totalTriangles:N0} tris | save the ERA";

                MessageBox.Show(
                    this,
                    "Halo BSP optimization is complete.\n\n" +
                    $"Spatial chunks: {placedCount:N0}\n" +
                    $"Triangles: {totalTriangles:N0}\n" +
                    $"Grid requested: {options.CellsPerAxis} × {options.CellsPerAxis}\n\n" +
                    "The original giant BSP ArtObject has been removed from the pending map state. Each replacement chunk now has an origin near its own geometry, which is substantially safer for Halo Wars object culling.\n\n" +
                    "Save the ERA, then test the map from several camera positions/angles.",
                    "Halo BSP Finalized",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString()
                    +
                    (
                        placedCount > 0
                            ? $"\n\n{placedCount} chunk(s) were already placed before the failure. The original BSP was NOT intentionally removed unless the operation reached the final replacement step. You can delete/undo the partial chunks before retrying with a smaller grid."
                            : string.Empty
                    ),
                    "Halo BSP Optimization Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Halo BSP optimization failed.";
            }
            finally
            {
                IsEnabled =
                    oldEnabled;

                try
                {
                    if (Directory.Exists(
                            tempDirectory))
                    {
                        Directory.Delete(
                            tempDirectory,
                            recursive: true);
                    }
                }
                catch
                {
                    // Local custom-mesh library copies survive independently;
                    // temporary optimizer files are disposable.
                }
            }
        }

        private bool TryResolveActiveHaloBspSourceV36(
            ScenarioArtObject artObject,
            out ImportedMeshEntry? sourceMesh,
            out float committedScale,
            out string reason)
        {
            sourceMesh =
                null;

            committedScale =
                1.0f;

            reason =
                "The selected ArtObject is not an active Ensemble Halo BSP import.";

            if (_currentScenarioChunk ==
                null)
            {
                return false;
            }

            if (!CustomMeshPendingAssetService
                    .TryGetPendingRecordForArtObject(
                        _currentScenarioChunk.FileName,
                        artObject.Id,
                        out CustomMeshPendingAssetService.EmbeddedCustomMeshRecord? pending)
                ||
                pending ==
                    null)
            {
                reason =
                    "The selected object is not an active unsaved/current-session custom mesh.\n\n" +
                    "BSP finalization currently needs the source geometry retained by the live Halo import session. If this BSP was saved and Ensemble was restarted, re-import it, set the desired transform, and finalize it before closing the editor.";

                return false;
            }

            sourceMesh =
                CustomMeshImportService
                    .LoadAll()
                    .FirstOrDefault(
                        entry =>
                            entry.Id.Equals(
                                pending.MeshId,
                                StringComparison.OrdinalIgnoreCase));

            if (sourceMesh ==
                    null ||
                !sourceMesh.Extension.Equals(
                    ".obj",
                    StringComparison.OrdinalIgnoreCase) ||
                !HaloBspChunkService
                    .IsEnsembleHaloObj(
                        sourceMesh.LibraryFilePath))
            {
                sourceMesh =
                    null;

                reason =
                    "The selected custom mesh is not an Ensemble-generated Halo BSP OBJ.";

                return false;
            }

            committedScale =
                GetCustomMeshScaleForGizmoV356(
                    artObject)
                ??
                1.0f;

            if (!float.IsFinite(
                    committedScale) ||
                committedScale <= 0)
            {
                committedScale =
                    1.0f;
            }

            return true;
        }

        private ScenarioArtObject PlaceImportedHaloBspChunkV36(
            ObjectCatalogEntry meshEntry,
            IReadOnlyList<ObjectCatalogEntry> catalogEntries,
            IReadOnlyDictionary<string, EraArchiveInfo> previewArchives,
            Vector3 targetPosition,
            Vector3 targetForward,
            Vector3 targetRight)
        {
            if (meshEntry.Layer !=
                    ObjectCatalogLayer.ImportedMesh ||
                meshEntry.ImportedMesh ==
                    null)
            {
                throw new InvalidOperationException(
                    "Halo BSP chunk is not a staged custom mesh.");
            }

            if (_currentArchive ==
                    null ||
                _currentScenarioChunk ==
                    null ||
                _currentArtObjectsChunk ==
                    null ||
                _currentArtObjectsOriginalSc2Data ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap map)
            {
                throw new InvalidOperationException(
                    "The target map has no editable SC2 ArtObjects companion.");
            }

            ImportedMeshEntry importedMesh =
                meshEntry.ImportedMesh;

            CustomMeshGeometryService.Geometry geometry =
                CustomMeshGeometryService
                    .Import(
                        importedMesh.LibraryFilePath);

            string scenarioFile =
                _currentScenarioChunk.FileName;

            HashSet<string> usedTypes =
                _currentArtObjects
                    .Select(
                        obj => obj.Type)
                    .Where(
                        type =>
                            !string.IsNullOrWhiteSpace(
                                type))
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            HashSet<string> usedUgxPaths =
                ResolveUsedUgxPathsForCurrentMap();

            HashSet<string> targetEraPaths =
                _currentArchive.Chunks
                    .Where(
                        chunk =>
                            !string.IsNullOrWhiteSpace(
                                chunk.FileName))
                    .Select(
                        chunk =>
                            CustomMeshPendingAssetService
                                .NormalizeArchivePath(
                                    chunk.FileName))
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            List<ObjectCatalogEntry> candidates =
                catalogEntries
                    .Where(
                        entry =>
                            entry.Layer ==
                                ObjectCatalogLayer.ArtObject &&
                            entry.ArtObject !=
                                null &&
                            entry.SourceXmbData.Length >
                                0 &&
                            !string.IsNullOrWhiteSpace(
                                entry.DonorEraPath) &&
                            !string.IsNullOrWhiteSpace(
                                entry.Type) &&
                            !usedTypes.Contains(
                                entry.Type))
                    .OrderBy(
                        GetCustomMeshTemplateRank)
                    .ThenBy(
                        entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(
                        900)
                    .ToList();

            Exception? lastTemplateError =
                null;

            foreach (ObjectCatalogEntry candidate
                     in candidates)
            {
                try
                {
                    EraArchiveInfo donorArchive =
                        ResolveBrowserArchive(
                            candidate.DonorEraPath,
                            previewArchives);

                    UgxMeshService.UgxMeshAsset? asset =
                        UgxMeshService
                            .TryLoadForObject(
                                donorArchive,
                                candidate.Type,
                                candidate.InternalName);

                    if (asset ==
                            null ||
                        asset.Parts.Count ==
                            0 ||
                        string.IsNullOrWhiteSpace(
                            asset.SourceFileName))
                    {
                        continue;
                    }

                    string ugxArchivePath =
                        CustomMeshPendingAssetService
                            .NormalizeArchivePath(
                                asset.SourceFileName);

                    if (usedUgxPaths.Contains(
                            ugxArchivePath) ||
                        CustomMeshPendingAssetService
                            .IsReserved(
                                scenarioFile,
                                ugxArchivePath) ||
                        targetEraPaths.Contains(
                            ugxArchivePath))
                    {
                        continue;
                    }

                    EraArchiveInfo sourceArchive =
                        EraArchiveService
                            .Open(
                                asset.SourceArchivePath);

                    EraChunkInfo? sourceChunk =
                        sourceArchive.Chunks
                            .FirstOrDefault(
                                chunk =>
                                    CustomMeshPendingAssetService
                                        .NormalizeArchivePath(
                                            chunk.FileName)
                                        .Equals(
                                            ugxArchivePath,
                                            StringComparison.OrdinalIgnoreCase));

                    if (sourceChunk ==
                        null)
                    {
                        continue;
                    }

                    byte[] stockUgx =
                        EraExtractionService
                            .ExtractChunk(
                                sourceArchive,
                                sourceChunk);

                    CustomUgxConversionService.Result conversion =
                        CustomUgxConversionService
                            .Convert(
                                stockUgx,
                                geometry);

                    CustomMeshPendingAssetService
                        .Register(
                            scenarioFile,
                            ugxArchivePath,
                            conversion.UgxData,
                            importedMesh.Id,
                            importedMesh.DisplayName,
                            CustomMeshPendingAssetService
                                .BuildTemplateEraHint(
                                    asset.SourceArchivePath),
                            checked(
                                (byte)sourceChunk.CompressionMethod),
                            sourceChunk.AlignmentLog2,
                            sourceChunk.ResourceFlags,
                            sourceChunk.Date);

                    try
                    {
                        ScenarioArtObject source =
                            candidate.ArtObject!;

                        int newId =
                            AllocateCrossLayerObjectId(
                                map);

                        ScenarioArtObject imported =
                            CloneArtObjectModel(
                                source,
                                newId,
                                targetPosition);

                        imported.Forward =
                            targetForward;

                        imported.Right =
                            targetRight;

                        byte[]? beforePending =
                            _pendingArtObjectsSc2Replacement?
                                .ToArray();

                        byte[] targetSc2 =
                            beforePending
                            ??
                            _currentArtObjectsOriginalSc2Data;

                        byte[] afterPending =
                            XmbObjectTransferService
                                .CloneObjectSubtree(
                                    targetSc2,
                                    candidate.SourceXmbData,
                                    source.Id,
                                    newId);

                        afterPending =
                            XmbDocumentService
                                .WriteArtObjectTransforms(
                                    afterPending,
                                    new[]
                                    {
                                        imported
                                    });

                        int insertIndex =
                            _currentArtObjects.Count;

                        _currentArtObjects.Add(
                            imported);

                        _pendingArtObjectsSc2Replacement =
                            afterPending;

                        _artObjectsDirty =
                            true;

                        CustomMeshPendingAssetService
                            .BindObject(
                                scenarioFile,
                                ugxArchivePath,
                                imported.Id);

                        CustomMeshViewportPreviewService
                            .Register(
                                scenarioFile,
                                imported.Id,
                                geometry,
                                stockUgx);

                        RefreshArtObjectLayer();

                        PushEnsembleNextHistory(
                            new ArtObjectStructureHistoryAction(
                                this,
                                imported,
                                insertIndex,
                                beforePending,
                                afterPending,
                                $"Place Halo BSP chunk"));

                        UgxMeshService
                            .ClearCaches();

                        return imported;
                    }
                    catch
                    {
                        CustomMeshPendingAssetService
                            .Remove(
                                scenarioFile,
                                ugxArchivePath);

                        throw;
                    }
                }
                catch (Exception ex)
                {
                    lastTemplateError =
                        ex;
                }
            }

            throw new InvalidOperationException(
                "Ensemble could not find another compatible unused rigid UGX slot for this Halo BSP chunk.\n\n" +
                "Try finalizing with fewer spatial cells, or use a target map / game-asset set with more unused rigid scenery slots.\n\n" +
                (
                    lastTemplateError !=
                        null
                        ? "Last template error: " +
                          lastTemplateError.Message
                        : string.Empty
                ),
                lastTemplateError);
        }

        private static Vector3 RotateLocalOffsetForForwardV36(
            Vector3 localOffset,
            Vector3 forward)
        {
            Vector3 horizontal =
                new Vector3(
                    forward.X,
                    0,
                    forward.Z);

            if (!float.IsFinite(
                    horizontal.X) ||
                !float.IsFinite(
                    horizontal.Z) ||
                horizontal.LengthSquared() <
                    0.000001f)
            {
                horizontal =
                    Vector3.UnitZ;
            }
            else
            {
                horizontal =
                    Vector3.Normalize(
                        horizontal);
            }

            // CustomMeshViewportPreviewService uses:
            // yaw = atan2(forward.X, forward.Z), then WPF Y rotation.
            // Apply the same transform to the chunk-local origin offset.
            float sin =
                horizontal.X;

            float cos =
                horizontal.Z;

            return new Vector3(
                localOffset.X *
                    cos +
                localOffset.Z *
                    sin,

                localOffset.Y,

                -localOffset.X *
                    sin +
                localOffset.Z *
                    cos);
        }
    }
}
