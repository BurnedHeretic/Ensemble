using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ensemble
{
    /// <summary>
    /// v38 cohesive MCC Halo .map -> Halo Wars map workflow.
    ///
    /// The earlier importer intentionally stopped at visual BSP geometry. This
    /// pass keeps that workflow available as "Visual Only" while adding a full
    /// import that shares one scale / centre / vertical placement between the
    /// generated UGX and the target XTD/XSD terrain.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly bool
            _haloFullMapBootstrapV37 =
                RegisterHaloFullMapV37();

        private bool
            _haloFullMapInitializedV37;

        private MenuItem?
            _haloFullMapMenuItemV37;

        private static bool RegisterHaloFullMapV37()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    HaloFullMapV37_Loaded),
                true);

            return true;
        }

        private static void HaloFullMapV37_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender
                    is not MainWindow window ||
                window._haloFullMapInitializedV37)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    window.InitializeHaloFullMapV37));
        }

        private void InitializeHaloFullMapV37()
        {
            if (_haloFullMapInitializedV37)
                return;

            MenuItem? fileMenu =
                FindMenuItemByHeaderV19(
                    "File");

            if (fileMenu ==
                null)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(
                        InitializeHaloFullMapV37));

                return;
            }

            _haloFullMapInitializedV37 =
                true;

            if (_haloMapImportMenuItemV34 !=
                null)
            {
                _haloMapImportMenuItemV34.Header =
                    "Import Halo _BSP (Visual Only)...";
            }

            _haloFullMapMenuItemV37 =
                new MenuItem
                {
                    Header =
                        "Import MCC Halo _Map..."
                };

            _haloFullMapMenuItemV37.Click +=
                HaloFullMapV37_Click;

            int visualIndex =
                _haloMapImportMenuItemV34 !=
                    null
                    ? fileMenu.Items.IndexOf(
                        _haloMapImportMenuItemV34)
                    : -1;

            if (visualIndex >=
                0)
            {
                fileMenu.Items.Insert(
                    visualIndex,
                    _haloFullMapMenuItemV37);
            }
            else
            {
                int insertIndex =
                    Math.Max(
                        1,
                        fileMenu.Items.Count -
                        2);

                fileMenu.Items.Insert(
                    insertIndex,
                    _haloFullMapMenuItemV37);
            }

            Closed +=
                HaloFullMapV37_Closed;
        }

        private void HaloFullMapV37_Closed(
            object? sender,
            EventArgs e)
        {
            if (_haloFullMapMenuItemV37 !=
                null)
            {
                _haloFullMapMenuItemV37.Click -=
                    HaloFullMapV37_Click;
            }

            Closed -=
                HaloFullMapV37_Closed;
        }

        private async void HaloFullMapV37_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive ==
                    null ||
                _currentScenarioChunk ==
                    null ||
                _currentArtObjectsChunk ==
                    null ||
                _currentArtObjectsOriginalSc2Data ==
                    null ||
                ScenarioMapCanvas.Scenario
                    is not ScenarioMap targetMap)
            {
                MessageBox.Show(
                    this,
                    "Open the Halo Wars target/template map and its scenario first.\n\n" +
                    "A full Halo import needs the target map's SCN/SC2 structure and, for gameplay terrain, its XTD/XSD files.",
                    "Import MCC Halo Map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            OpenFileDialog mapDialog =
                new OpenFileDialog
                {
                    Title =
                        "Import MCC Halo Map",

                    Filter =
                        "Halo MCC Maps (*.map)|*.map|" +
                        "All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false
                };

            if (mapDialog.ShowDialog(
                    this) !=
                true)
            {
                return;
            }

            HaloFullMapImportWindowV37 options =
                new HaloFullMapImportWindowV37
                {
                    Owner =
                        this
                };

            if (options.ShowDialog() !=
                true)
            {
                return;
            }

            if (options.GenerateGameplayTerrain)
            {
                if (_currentTerrainChunk ==
                        null ||
                    _currentTerrainOriginalXtdData ==
                        null ||
                    _currentSimulationChunk ==
                        null ||
                    _currentSimulationOriginalXsdData ==
                        null ||
                    ScenarioMapCanvas.TerrainHeightMap ==
                        null)
                {
                    MessageBox.Show(
                        this,
                        "This target map does not have the complete XTD + XSD terrain pair required for gameplay-terrain conversion.\n\n" +
                        "You can still use File > Import Halo BSP (Visual Only), or choose another Halo Wars template map with both terrain files.",
                        "Gameplay Terrain Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            string summary =
                "MCC HALO MAP IMPORT\n\n" +
                $"Source: {Path.GetFileName(mapDialog.FileName)}\n" +
                $"Target: {Path.GetFileName(_currentArchive.FilePath)}\n\n" +
                $"Visual BSP: {(options.ImportVisualBsp ? "Yes" : "No")}\n" +
                $"Gameplay terrain: {(options.GenerateGameplayTerrain ? "Yes" : "No")}\n" +
                (
                    options.ImportVisualBsp
                        ? $"Spatial finalization: {(options.OptimizeVisualBsp ? $"{options.CellsPerAxis}×{options.CellsPerAxis}" : "No")}\n"
                        : string.Empty
                ) +
                $"Coverage: {options.CoverageFraction * 100.0f:0.#}%\n" +
                $"Extra multiplier: {options.SizeMultiplier:0.####}x\n" +
                $"Vertical offset: {options.VerticalOffset:0.###}\n" +
                (
                    options.GenerateGameplayTerrain
                        ? $"Ground mode: {FormatSurfaceModeV37(options.SurfaceMode)}\n" +
                          $"Max slope: {options.MaxSlopeDegrees:0.#}°\n" +
                          $"Smoothing: {options.SmoothingPasses} pass(es)\n" +
                          $"Existing unsaved terrain preview: {(ScenarioMapCanvas.HasTerrainPreviewChanges ? "Yes - import will layer onto it" : "No")}\n"
                        : string.Empty
                ) +
                "\nThe Halo FPS gameplay layer is intentionally not imported.\n\n" +
                "Continue?";

            MessageBoxResult confirm =
                MessageBox.Show(
                    this,
                    summary,
                    "Import MCC Halo Map",
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
                    "HaloFullMapV37",
                    Guid.NewGuid()
                        .ToString(
                            "N"));

            Directory.CreateDirectory(
                tempDirectory);

            string objPath =
                Path.Combine(
                    tempDirectory,
                    Path.GetFileNameWithoutExtension(
                        mapDialog.FileName)
                    +
                    "_full_bsp.obj");

            bool oldEnabled =
                IsEnabled;

            List<ScenarioArtObject> rollbackVisuals =
                new();

            List<ImportedMeshEntry> rollbackLibraryEntries =
                new();

            bool terrainApplied =
                false;

            try
            {
                IsEnabled =
                    false;

                Mouse.OverrideCursor =
                    Cursors.Wait;

                StatusText.Text =
                    "Reading MCC Halo BSP geometry...";

                HaloMccMapImportService.ExtractResult extraction =
                    await Task.Run(
                        () =>
                            HaloMccMapImportService
                                .ExtractMapToObj(
                                    mapDialog.FileName,
                                    objPath,
                                    worldScale:
                                        1.0f));

                StatusText.Text =
                    "Measuring BSP and fitting it to the Halo Wars battlefield...";

                if (!HaloMapImportService
                        .TryMeasureGeneratedObjHorizontalSize(
                            extraction.ObjPath,
                            out float sourceWidth,
                            out float sourceDepth))
                {
                    throw new InvalidDataException(
                        "The extracted Halo BSP could not be measured.");
                }

                TerrainHeightMap? targetTerrain =
                    ScenarioMapCanvas
                        .TerrainHeightMap;

                float targetMinX =
                    targetTerrain != null
                        ? targetTerrain.WorldMin.X
                        : targetMap.MinX;

                float targetMaxX =
                    targetTerrain != null
                        ? targetTerrain.WorldMax.X
                        : targetMap.MaxX;

                float targetMinZ =
                    targetTerrain != null
                        ? targetTerrain.WorldMin.Z
                        : targetMap.MinZ;

                float targetMaxZ =
                    targetTerrain != null
                        ? targetTerrain.WorldMax.Z
                        : targetMap.MaxZ;

                float targetWidth =
                    Math.Max(
                        1.0f,
                        MathF.Abs(
                            targetMaxX -
                            targetMinX));

                float targetDepth =
                    Math.Max(
                        1.0f,
                        MathF.Abs(
                            targetMaxZ -
                            targetMinZ));

                float autoFit =
                    HaloMapImportService
                        .CalculateFitScaleFromGeometry(
                            sourceWidth,
                            sourceDepth,
                            targetWidth,
                            targetDepth,
                            options.CoverageFraction);

                float importScale =
                    autoFit *
                    options.SizeMultiplier;

                if (!float.IsFinite(
                        importScale) ||
                    importScale <=
                        0)
                {
                    throw new InvalidDataException(
                        "The calculated Halo map scale is invalid.");
                }

                await Task.Run(
                    () =>
                        HaloMapImportService
                            .ScaleGeneratedObj(
                                extraction.ObjPath,
                                importScale));

                extraction.AppliedScale =
                    importScale;

                extraction.SourceWidth =
                    sourceWidth;

                extraction.SourceDepth =
                    sourceDepth;

                StatusText.Text =
                    "Preparing transformed Halo BSP geometry...";

                CustomMeshGeometryService.Geometry geometry =
                    await Task.Run(
                        () =>
                            CustomMeshGeometryService
                                .Import(
                                    extraction.ObjPath));

                float groundY =
                    (
                        targetTerrain !=
                            null
                            ? HaloBspTerrainService
                                .ChoosePlacementGroundHeight(
                                    targetTerrain)
                            : 0.0f
                    )
                    +
                    options.VerticalOffset;

                Vector3 placement =
                    new Vector3(
                        (
                            targetMinX +
                            targetMaxX
                        ) *
                        0.5f,
                        groundY,
                        (
                            targetMinZ +
                            targetMaxZ
                        ) *
                        0.5f);

                Vector3 forward =
                    Vector3.UnitZ;

                Vector3 right =
                    Vector3.UnitX;

                HaloBspTerrainService.Result?
                    terrainResult =
                        null;

                if (options.GenerateGameplayTerrain)
                {
                    if (targetTerrain ==
                        null)
                    {
                        throw new InvalidDataException(
                            "The target XTD terrain disappeared during import.");
                    }

                    StatusText.Text =
                        "Projecting Halo BSP walkable surfaces into XTD terrain...";

                    terrainResult =
                        await Task.Run(
                            () =>
                                HaloBspTerrainService
                                    .Generate(
                                        geometry,
                                        targetTerrain,
                                        placement,
                                        forward,
                                        options.SurfaceMode,
                                        options.MaxSlopeDegrees,
                                        options.SmoothingPasses));
                }

                ScenarioArtObject?
                    sourceVisual =
                        null;

                IReadOnlyList<ScenarioArtObject>
                    finalVisuals =
                        Array.Empty<ScenarioArtObject>();

                if (options.ImportVisualBsp)
                {
                    StatusText.Text =
                        "Staging native Halo BSP as Halo Wars UGX...";

                    ImportedMeshEntry imported =
                        CustomMeshImportService
                            .Import(
                                extraction.ObjPath);

                    rollbackLibraryEntries.Add(
                        imported);

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
                                extraction.SourceGame + " BSP - " +
                                Path.GetFileNameWithoutExtension(
                                    mapDialog.FileName),

                            InternalName =
                                imported.DisplayName,

                            Type =
                                "OBJ mesh",

                            ImportedMesh =
                                imported
                        };

                    StatusText.Text =
                        "Scanning Halo Wars assets for rigid UGX slots...";

                    GameObjectCatalogLoadResult library =
                        await Task.Run(
                            () =>
                                GameObjectCatalogService
                                    .LoadGameLibrary(
                                        _currentArchive.FilePath));

                    sourceVisual =
                        PlaceImportedHaloBspChunkV36(
                            importedEntry,
                            library.Entries,
                            library.Archives,
                            placement,
                            forward,
                            right);

                    rollbackVisuals.Add(
                        sourceVisual);

                    if (options.OptimizeVisualBsp)
                    {
                        StatusText.Text =
                            "Finalizing Halo BSP into spatial Halo Wars chunks...";

                        finalVisuals =
                            await FinalizeHaloBspForFullImportV37(
                                sourceVisual,
                                imported,
                                library,
                                options.CellsPerAxis,
                                tempDirectory,
                                rollbackVisuals,
                                rollbackLibraryEntries);

                        // The finalized UGXs and viewport previews no longer
                        // depend on the staged monolithic OBJ. Do not leave
                        // generated Halo BSP sources cluttering the user's
                        // normal custom-mesh Object Browser library.
                        TryDeleteImportedMeshLibraryEntryV37(
                            imported);

                        rollbackLibraryEntries.Remove(
                            imported);
                    }
                    else
                    {
                        finalVisuals =
                            new[]
                            {
                                sourceVisual
                            };

                        // Keep the editable monolithic BSP source in the local
                        // mesh library so the manual Optimize Selected Halo BSP
                        // command can still resolve and finalize it later.
                        rollbackLibraryEntries.Remove(
                            imported);
                    }
                }

                if (terrainResult !=
                    null)
                {
                    StatusText.Text =
                        "Applying generated gameplay terrain preview...";

                    int changed =
                        ScenarioMapCanvas
                            .ApplyImportedTerrainHeightsV37(
                                terrainResult.Heights);

                    terrainApplied =
                        changed >
                        0;

                    UpdateDirtyState();
                }

                RefreshArtObjectLayer();

                Refresh3DViewportNext(
                    false);

                ApplyCustomMeshViewportPreviews();

                UpdateDirtyState();

                string completion =
                    "MCC Halo map import complete.\n\n" +
                    $"Source game: {extraction.SourceGame}\n" +
                    $"Cache: {extraction.CacheType}\n" +
                    $"Backend: {extraction.ProviderName}\n" +
                    (string.IsNullOrWhiteSpace(extraction.BuildString)
                        ? string.Empty
                        : $"Build: {extraction.BuildString}\n") +
                    $"BSP tags read: {extraction.BspTags.Count:N0}\n" +
                    $"Render triangles: {extraction.TriangleCount:N0}\n" +
                    $"Extracted footprint: {sourceWidth:0.##} × {sourceDepth:0.##}\n" +
                    $"Applied scale: {importScale:0.######}\n" +
                    $"Placement Y: {groundY:0.###}\n" +
                    (
                        options.ImportVisualBsp
                            ? $"Halo Wars visual object(s): {finalVisuals.Count:N0}\n"
                            : string.Empty
                    ) +
                    (
                        terrainResult !=
                            null
                            ? $"Terrain samples covered: {terrainResult.CoveredVertices:N0}\n" +
                              $"Terrain samples changed: {terrainResult.ChangedVertices:N0}\n" +
                              $"Terrain samples clamped: {terrainResult.ClampedVertices:N0}\n" +
                              $"Generated height range: {terrainResult.MinGeneratedHeight:0.##} → {terrainResult.MaxGeneratedHeight:0.##}\n" +
                              "XSD land pathing: slope-derived obstruction tiles will synchronize on Save\n"
                            : string.Empty
                    ) +
                    "\nSave the ERA to persist the UGX/SC2 changes and the synchronized XTD/XSD gameplay terrain.";

                StatusText.Text =
                    "MCC Halo map import complete | save the ERA";

                MessageBox.Show(
                    this,
                    completion,
                    "Halo Map Import Complete",
                    MessageBoxButton.OK,
                    terrainResult != null &&
                    terrainResult.ClampedVertices > 0
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Best-effort rollback of visual custom meshes created by this
                // operation. The terrain preview is applied last, so most
                // failures occur before any XTD state is changed.
                for (int i =
                         rollbackVisuals.Count -
                         1;
                     i >=
                         0;
                     i--)
                {
                    try
                    {
                        ScenarioArtObject item =
                            rollbackVisuals[i];

                        if (_currentArtObjects.Any(
                                live =>
                                    ReferenceEquals(
                                        live,
                                        item) ||
                                    live.Id ==
                                        item.Id))
                        {
                            _ =
                                TryDeleteCustomMeshPlacementFromKeyboard(
                                    item);
                        }
                    }
                    catch
                    {
                        // Preserve the original exception.
                    }
                }

                foreach (ImportedMeshEntry entry
                         in rollbackLibraryEntries)
                {
                    TryDeleteImportedMeshLibraryEntryV37(
                        entry);
                }

                RefreshArtObjectLayer();

                Refresh3DViewportNext(
                    false);

                ApplyCustomMeshViewportPreviews();

                UpdateDirtyState();

                MessageBox.Show(
                    this,
                    ex.ToString()
                    +
                    (
                        terrainApplied
                            ? "\n\nThe terrain preview was already applied before this failure. Use Terrain > Reset Terrain Changes if you do not want to keep it."
                            : string.Empty
                    ),
                    "Halo Map Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "MCC Halo map import failed.";
            }
            finally
            {
                Mouse.OverrideCursor =
                    null;

                IsEnabled =
                    oldEnabled;

                try
                {
                    if (Directory.Exists(
                            tempDirectory))
                    {
                        Directory.Delete(
                            tempDirectory,
                            recursive:
                                true);
                    }
                }
                catch
                {
                    // Imported custom meshes have already been copied into the
                    // Ensemble library. Temporary extraction files are disposable.
                }
            }
        }

        private async Task<IReadOnlyList<ScenarioArtObject>>
            FinalizeHaloBspForFullImportV37(
                ScenarioArtObject original,
                ImportedMeshEntry sourceMesh,
                GameObjectCatalogLoadResult library,
                int cellsPerAxis,
                string parentTempDirectory,
                List<ScenarioArtObject> rollbackVisuals,
                List<ImportedMeshEntry> rollbackLibraryEntries)
        {
            string chunkDirectory =
                Path.Combine(
                    parentTempDirectory,
                    "chunks");

            Directory.CreateDirectory(
                chunkDirectory);

            IReadOnlyList<HaloBspChunkService.Chunk> chunks =
                await Task.Run(
                    () =>
                        HaloBspChunkService
                            .SplitGeneratedHaloObj(
                                sourceMesh.LibraryFilePath,
                                chunkDirectory,
                                cellsPerAxis,
                                1.0f));

            Vector3 originalPosition =
                original.Position;

            Vector3 originalForward =
                original.Forward;

            Vector3 originalRight =
                original.Right;

            List<ScenarioArtObject> placed =
                new(
                    chunks.Count);

            foreach (HaloBspChunkService.Chunk chunk
                     in chunks)
            {
                StatusText.Text =
                    $"Compiling spatial Halo BSP chunk {placed.Count + 1}/{chunks.Count}...";

                ImportedMeshEntry imported =
                    CustomMeshImportService
                        .Import(
                            chunk.ObjPath);

                rollbackLibraryEntries.Add(
                    imported);

                ObjectCatalogEntry entry =
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

                Vector3 worldPosition =
                    originalPosition
                    +
                    RotateLocalOffsetForForwardV36(
                        chunk.LocalOrigin,
                        originalForward);

                ScenarioArtObject placedChunk =
                    PlaceImportedHaloBspChunkV36(
                        entry,
                        library.Entries,
                        library.Archives,
                        worldPosition,
                        originalForward,
                        originalRight);

                placed.Add(
                    placedChunk);

                rollbackVisuals.Add(
                    placedChunk);

                // The queued native UGX and viewport preview now own everything
                // needed for this generated cell. Remove only Ensemble's
                // automatically staged copy; never touch the original MCC .map.
                TryDeleteImportedMeshLibraryEntryV37(
                    imported);

                rollbackLibraryEntries.Remove(
                    imported);
            }

            if (!TryDeleteCustomMeshPlacementFromKeyboard(
                    original))
            {
                throw new InvalidOperationException(
                    "Spatial BSP chunks were created, but the editable source BSP could not be retired.");
            }

            rollbackVisuals.Remove(
                original);

            return placed;
        }

        private static void TryDeleteImportedMeshLibraryEntryV37(
            ImportedMeshEntry entry)
        {
            try
            {
                string libraryRoot =
                    Path.GetFullPath(
                        CustomMeshImportService
                            .LibraryRoot)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                string filePath =
                    Path.GetFullPath(
                        entry.LibraryFilePath);

                string? directory =
                    Path.GetDirectoryName(
                        filePath);

                if (string.IsNullOrWhiteSpace(
                        directory))
                {
                    return;
                }

                string fullDirectory =
                    Path.GetFullPath(
                        directory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                if (!fullDirectory.StartsWith(
                        libraryRoot +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (Directory.Exists(
                        fullDirectory))
                {
                    Directory.Delete(
                        fullDirectory,
                        recursive:
                            true);
                }
            }
            catch
            {
                // Library cleanup is polish only; never turn a successful
                // conversion into an import failure because Windows still has
                // a preview file handle open.
            }
        }

        private static string FormatSurfaceModeV37(
            HaloTerrainSurfaceMode mode)
        {
            return mode switch
            {
                HaloTerrainSurfaceMode.HighestWalkable =>
                    "Highest walkable surface",

                HaloTerrainSurfaceMode.NearestToTemplate =>
                    "Nearest to target terrain",

                _ =>
                    "Lowest walkable surface"
            };
        }
    }
}
