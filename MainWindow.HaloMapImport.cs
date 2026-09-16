using Ensemble.Models;
using Ensemble.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ensemble
{
    public partial class MainWindow
    {
        private bool
            _haloMapImportV34Initialized;

        private MenuItem?
            _haloMapImportMenuItemV34;

        // Kept under the v34 method name so existing initialization calls in
        // MainWindow.HaloWarsUi.cs continue to work when this cumulative patch
        // is copied over an older Ensemble source tree.
        private void InitializeHaloMapImportV34()
        {
            if (_haloMapImportV34Initialized)
                return;

            _haloMapImportV34Initialized =
                true;

            MenuItem? fileMenu =
                FindMenuItemByHeaderV19(
                    "File");

            if (fileMenu == null)
                return;

            int insertIndex =
                Math.Max(
                    1,
                    fileMenu.Items.Count - 2);

            fileMenu.Items.Insert(
                insertIndex,
                new Separator());

            _haloMapImportMenuItemV34 =
                new MenuItem
                {
                    Header =
                        "Import _Halo Map..."
                };

            _haloMapImportMenuItemV34.Click +=
                HaloMapImportV35_Click;

            fileMenu.Items.Insert(
                insertIndex + 1,
                _haloMapImportMenuItemV34);
        }

        private async void HaloMapImportV35_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentArchive == null
                || _currentScenarioChunk == null
                || _currentArtObjectsChunk == null
                || _currentArtObjectsOriginalSc2Data == null
                || ScenarioMapCanvas.Scenario == null)
            {
                MessageBox.Show(
                    this,
                    "Open the Halo Wars map that will act as the target/template first.\n\n" +
                    "The native Halo importer extracts Reach BSP environment geometry, converts it through Ensemble's UGX compiler, and embeds it into the currently open Halo Wars ERA when you save.",
                    "Import Halo Map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            OpenFileDialog mapDialog =
                new OpenFileDialog
                {
                    Title =
                        "Import MCC Halo Reach Map",

                    Filter =
                        "Halo MCC Reach Maps (*.map)|*.map|All Files (*.*)|*.*",

                    CheckFileExists =
                        true,

                    Multiselect =
                        false
                };

            if (mapDialog.ShowDialog(this) !=
                true)
            {
                return;
            }

            HaloMapImportOptionsWindow sizeOptions =
                new HaloMapImportOptionsWindow
                {
                    Owner = this
                };

            if (sizeOptions.ShowDialog() != true)
                return;

            MessageBoxResult confirmation =
                MessageBox.Show(
                    this,
                    "NATIVE HALO REACH MAP IMPORT\n\n" +
                    $"Source: {Path.GetFileName(mapDialog.FileName)}\n" +
                    $"Target: {Path.GetFileName(_currentArchive.FilePath)}\n\n" +
                    "Ensemble will import CORE BSP / ENVIRONMENT GEOMETRY ONLY.\n\n" +
                    "Halo FPS scenery objects, weapons, vehicles, characters, scripts and encounters are intentionally ignored; individual assets can be imported separately through the Objects menu.\n\n" +
                    $"Requested map coverage: {sizeOptions.CoverageFraction * 100.0f:0.#}%\n" +
                    $"Extra size multiplier: {sizeOptions.SizeMultiplier:0.###}x\n\n" +
                    "The scale is baked into the generated Halo Wars UGX.\n\n" +
                    "Continue?",
                    "Import Halo Map",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }

            string tempDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    "Ensemble",
                    "HaloMapImport",
                    Guid.NewGuid()
                        .ToString("N"));

            Directory.CreateDirectory(
                tempDirectory);

            string objPath =
                Path.Combine(
                    tempDirectory,
                    Path.GetFileNameWithoutExtension(
                        mapDialog.FileName)
                    +
                    "_bsp.obj");

            bool oldEnabled =
                IsEnabled;

            try
            {
                IsEnabled =
                    false;

                Mouse.OverrideCursor =
                    Cursors.Wait;

                StatusText.Text =
                    "Reading MCC Halo Reach BSP geometry at native scale...";

                ScenarioMap targetMap =
                    ScenarioMapCanvas.Scenario!;

                float targetWidth =
                    (float)Math.Max(
                        1.0,
                        targetMap.MaxX - targetMap.MinX);

                float targetDepth =
                    (float)Math.Max(
                        1.0,
                        targetMap.MaxZ - targetMap.MinZ);

                // Extract first at 1:1 so scale is based on the vertices that
                // actually survived BSP/resource filtering. Raw scenario/SBSP
                // metadata can include distant streaming/helper BSP bounds and
                // was the reason some campaign maps arrived extremely small.
                HaloMapImportService.ExtractResult extraction =
                    await Task.Run(
                        () =>
                            HaloMapImportService
                                .ExtractReachMapToObj(
                                    mapDialog.FileName,
                                    objPath,
                                    new HaloMapImportService.ExtractOptions
                                    {
                                        WorldScale = 1.0f
                                    }));

                StatusText.Text =
                    "Measuring extracted Reach geometry and baking Halo Wars scale...";

                if (!HaloMapImportService.TryMeasureGeneratedObjHorizontalSize(
                        extraction.ObjPath,
                        out float actualSourceWidth,
                        out float actualSourceDepth))
                {
                    throw new InvalidDataException(
                        "Ensemble extracted Reach BSP triangles but could not measure their generated OBJ footprint.");
                }

                float autoFitScale =
                    HaloMapImportService.CalculateFitScaleFromGeometry(
                        actualSourceWidth,
                        actualSourceDepth,
                        targetWidth,
                        targetDepth,
                        sizeOptions.CoverageFraction);

                float importScale =
                    checked(autoFitScale * sizeOptions.SizeMultiplier);

                if (!float.IsFinite(importScale) || importScale <= 0)
                {
                    throw new InvalidDataException(
                        "The requested Halo map scale is not valid.");
                }

                await Task.Run(
                    () =>
                        HaloMapImportService.ScaleGeneratedObj(
                            extraction.ObjPath,
                            importScale));

                extraction.AppliedScale = importScale;
                extraction.SourceWidth = actualSourceWidth;
                extraction.SourceDepth = actualSourceDepth;

                StatusText.Text =
                    "Staging native Reach BSP geometry in Ensemble's custom mesh library...";

                ImportedMeshEntry imported =
                    CustomMeshImportService.Import(
                        extraction.ObjPath);

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
                            "Halo Reach BSP - " +
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
                    "Scanning Halo Wars assets for a map-local UGX slot...";

                GameObjectCatalogLoadResult library =
                    await Task.Run(
                        () =>
                            GameObjectCatalogService
                                .LoadGameLibrary(
                                    _currentArchive.FilePath));

                PlaceImportedMeshFromBrowser(
                    importedEntry,
                    library.Entries,
                    library.Archives);

                StatusText.Text =
                    $"Imported Reach BSP geometry natively | " +
                    $"{extraction.BspTags.Count:N0} BSP(s) | " +
                    (extraction.SkippedBspDiagnostics.Count > 0
                        ? $"{extraction.SkippedBspDiagnostics.Count:N0} skipped | "
                        : string.Empty) +
                    $"{extraction.TriangleCount:N0} tris | " +
                    "save the ERA to embed the generated UGX";

                MessageBox.Show(
                    this,
                    "Halo Reach BSP/environment geometry was read directly by Ensemble and placed into the current Halo Wars map.\n\n" +
                    $"Imported BSPs: {extraction.BspTags.Count:N0}\n" +
                    (extraction.SkippedBspDiagnostics.Count > 0
                        ? $"Skipped auxiliary/unsupported BSPs: {extraction.SkippedBspDiagnostics.Count:N0}\n"
                        : string.Empty) +
                    $"Render sections: {extraction.MeshPlacementCount:N0}\n" +
                    $"Triangles: {extraction.TriangleCount:N0}\n" +
                    $"Vertices: {extraction.VertexCount:N0}\n" +
                    $"Extracted footprint: {extraction.SourceWidth:0.##} x {extraction.SourceDepth:0.##} Reach units\n" +
                    $"Baked UGX scale: {extraction.AppliedScale:0.####}\n" +
                    $"Requested coverage: {sizeOptions.CoverageFraction * 100.0f:0.#}% | extra multiplier {sizeOptions.SizeMultiplier:0.###}x\n" +
                    (string.IsNullOrWhiteSpace(extraction.BuildString)
                        ? string.Empty
                        : $"Build: {extraction.BuildString}\n") +
                    "\nNo Reclaimer installation or DLL is required.\n\n" +
                    "Save the ERA to embed the generated Halo Wars UGX. Reach FPS objects were intentionally not imported.\n\n" +
                    "This pass imports visual BSP geometry; Halo Wars XTD/XSD movement/pathing terrain is still the target map's terrain and can be sculpted/edited separately.",
                    "Halo Map Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString()
                    +
                    "\n\nThe native importer does not require Reclaimer. If the error names shared.map, campaign.map or another MCC resource map, use the source .map from its normal Halo Reach maps directory so the sibling resource cache is available.",
                    "Halo Map Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text =
                    "Native Halo map import failed.";
            }
            finally
            {
                Mouse.OverrideCursor =
                    null;

                IsEnabled =
                    oldEnabled;

                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(
                            tempDirectory,
                            recursive: true);
                    }
                }
                catch
                {
                    // Temporary extraction files are disposable; failure to
                    // clean them must not turn a successful import into an error.
                }
            }
        }
    }
}
