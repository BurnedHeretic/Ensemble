using Ensemble.Models;
using Ensemble.Services;
using System.IO;

namespace Ensemble
{
    public partial class MainWindow
    {
        /// <summary>
        /// Places an ImportedMesh browser entry by borrowing an unused rigid
        /// stock SC2 ArtObject slot. The ArtObject remains a completely normal
        /// Halo Wars object; only its map-local UGX is replaced when the ERA is
        /// saved. This avoids inventing prototype database records.
        /// </summary>
        internal void PlaceImportedMeshFromBrowser(
            ObjectCatalogEntry meshEntry,
            IReadOnlyList<ObjectCatalogEntry> catalogEntries,
            IReadOnlyDictionary<string, EraArchiveInfo> previewArchives)
        {
            ArgumentNullException.ThrowIfNull(meshEntry);
            ArgumentNullException.ThrowIfNull(catalogEntries);
            ArgumentNullException.ThrowIfNull(previewArchives);

            if (meshEntry.Layer != ObjectCatalogLayer.ImportedMesh ||
                meshEntry.ImportedMesh == null)
            {
                throw new InvalidOperationException(
                    "The selected Object Browser entry is not a custom mesh.");
            }

            if (_currentArchive == null ||
                _currentScenarioChunk == null ||
                _currentArtObjectsChunk == null ||
                _currentArtObjectsOriginalSc2Data == null ||
                ScenarioMapCanvas.Scenario == null)
            {
                throw new InvalidOperationException(
                    "Open a map with an editable SC2 ArtObjects companion before placing a custom model.");
            }

            ImportedMeshEntry importedMesh = meshEntry.ImportedMesh;

            StatusText.Text =
                $"Reading custom mesh {importedMesh.DisplayName}...";

            CustomMeshGeometryService.Geometry geometry =
                CustomMeshGeometryService.Import(
                    importedMesh.LibraryFilePath);

            string scenarioFile =
                _currentScenarioChunk.FileName;

            HashSet<string> usedTypes =
                _currentArtObjects
                    .Select(obj => obj.Type)
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> usedUgxPaths =
                ResolveUsedUgxPathsForCurrentMap();

            HashSet<string> targetEraPaths =
                _currentArchive.Chunks
                    .Where(chunk => !string.IsNullOrWhiteSpace(chunk.FileName))
                    .Select(
                        chunk =>
                            CustomMeshPendingAssetService
                                .NormalizeArchivePath(chunk.FileName))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<ObjectCatalogEntry> candidates =
                catalogEntries
                    .Where(
                        entry =>
                            entry.Layer == ObjectCatalogLayer.ArtObject &&
                            entry.ArtObject != null &&
                            entry.SourceXmbData.Length > 0 &&
                            !string.IsNullOrWhiteSpace(entry.DonorEraPath) &&
                            !string.IsNullOrWhiteSpace(entry.Type) &&
                            !usedTypes.Contains(entry.Type))
                    .OrderBy(GetCustomMeshTemplateRank)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(600)
                    .ToList();

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "Ensemble could not find an unused SC2 ArtObject slot for the custom model.");
            }

            Exception? lastTemplateError = null;

            foreach (ObjectCatalogEntry candidate in candidates)
            {
                try
                {
                    EraArchiveInfo donorArchive =
                        ResolveBrowserArchive(
                            candidate.DonorEraPath,
                            previewArchives);

                    UgxMeshService.UgxMeshAsset? asset =
                        UgxMeshService.TryLoadForObject(
                            donorArchive,
                            candidate.Type,
                            candidate.InternalName);

                    if (asset == null ||
                        asset.Parts.Count == 0 ||
                        string.IsNullOrWhiteSpace(asset.SourceFileName))
                    {
                        continue;
                    }

                    string ugxArchivePath =
                        CustomMeshPendingAssetService
                            .NormalizeArchivePath(asset.SourceFileName);

                    // Never shadow a model that the current map is already
                    // using and never reuse a slot reserved by another custom
                    // mesh queued in this map.
                    if (usedUgxPaths.Contains(ugxArchivePath) ||
                        CustomMeshPendingAssetService.IsReserved(
                            scenarioFile,
                            ugxArchivePath))
                    {
                        continue;
                    }

                    // Prefer truly map-local-new paths. Replacing an existing
                    // target ERA asset can unintentionally affect hidden map
                    // content even when no visible ArtObject currently uses it.
                    if (targetEraPaths.Contains(ugxArchivePath))
                    {
                        continue;
                    }

                    EraArchiveInfo sourceArchive =
                        EraArchiveService.Open(asset.SourceArchivePath);

                    EraChunkInfo? sourceChunk =
                        sourceArchive.Chunks
                            .FirstOrDefault(
                                chunk =>
                                    CustomMeshPendingAssetService
                                        .NormalizeArchivePath(chunk.FileName)
                                        .Equals(
                                            ugxArchivePath,
                                            StringComparison.OrdinalIgnoreCase));

                    if (sourceChunk == null)
                        continue;

                    byte[] stockUgx =
                        EraExtractionService.ExtractChunk(
                            sourceArchive,
                            sourceChunk);

                    StatusText.Text =
                        $"Compiling {importedMesh.DisplayName} to Halo Wars UGX...";

                    CustomUgxConversionService.Result conversion =
                        CustomUgxConversionService.Convert(
                            stockUgx,
                            geometry);

                    CustomMeshPendingAssetService.Register(
                        scenarioFile,
                        ugxArchivePath,
                        conversion.UgxData,
                        importedMesh.Id,
                        importedMesh.DisplayName,
                        CustomMeshPendingAssetService.BuildTemplateEraHint(
                            asset.SourceArchivePath),
                        checked((byte)sourceChunk.CompressionMethod),
                        sourceChunk.AlignmentLog2,
                        sourceChunk.ResourceFlags,
                        sourceChunk.Date);

                    try
                    {
                        // Reuse the established cross-map ArtObject cloning,
                        // undo/history, selection and terrain-grounding path.
                        ObjectPlacementContextService.SetPending(
                            meshEntry,
                            preserveDonorPosition: false);

                        ImportForeignArtObject(
                            candidate,
                            preserveDonorPosition: false);

                        if (_selectedScenarioItem
                                is not ScenarioArtObject placedCustomObject)
                        {
                            throw new InvalidOperationException(
                                "The custom mesh SC2 placement completed, but Ensemble could not identify the newly placed ArtObject.");
                        }

                        CustomMeshPendingAssetService.BindObject(
                            scenarioFile,
                            ugxArchivePath,
                            placedCustomObject.Id);
                    }
                    catch
                    {
                        CustomMeshPendingAssetService.Remove(
                            scenarioFile,
                            ugxArchivePath);
                        throw;
                    }

                    UgxMeshService.ClearCaches();

                    StatusText.Text =
                        "Placed SC2 ArtObject custom mesh " +
                        $"{importedMesh.DisplayName} | " +
                        $"{conversion.TriangleCount:N0} tris | " +
                        $"{conversion.SectionCount:N0} UGX section(s) | " +
                        "Save the ERA to embed the native model";

                    return;
                }
                catch (Exception ex)
                {
                    lastTemplateError = ex;
                    // A candidate may be skinned, use an unusual vertex
                    // declaration, or have internally compressed geometry.
                    // Continue until a compatible unused rigid slot is found.
                }
            }

            throw new InvalidOperationException(
                "Ensemble could not find a compatible unused rigid UGX slot in the scanned Halo Wars assets.\n\n" +
                "The custom mesh itself was read successfully, but every candidate stock slot was either in use or used an unsupported UGX declaration.\n\n" +
                (lastTemplateError != null
                    ? "Last template error: " + lastTemplateError.Message
                    : string.Empty),
                lastTemplateError);
        }


        internal void ApplyEmbeddedCustomMeshDeletionFromBrowser(
            IReadOnlyList<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            if (records.Count == 0)
                return;

            if (_currentArtObjectsChunk == null ||
                _currentArtObjectsOriginalSc2Data == null)
            {
                throw new InvalidOperationException(
                    "The current map has no editable SC2 ArtObjects companion.");
            }

            HashSet<int> requestedIds =
                records
                    .Select(record => record.ArtObjectId)
                    .Where(id => id > 0)
                    .ToHashSet();

            List<int> liveIds =
                _currentArtObjects
                    .Where(obj => requestedIds.Contains(obj.Id))
                    .Select(obj => obj.Id)
                    .Distinct()
                    .ToList();

            if (liveIds.Count > 0)
            {
                byte[] sourceSc2 =
                    _pendingArtObjectsSc2Replacement
                    ??
                    _currentArtObjectsOriginalSc2Data;

                byte[] modifiedSc2 =
                    ScenarioArtObjectsService.RemoveObjects(
                        sourceSc2,
                        liveIds,
                        out _);

                _pendingArtObjectsSc2Replacement =
                    modifiedSc2;

                _artObjectsDirty =
                    true;

                _currentArtObjects.RemoveAll(
                    obj => requestedIds.Contains(obj.Id));

                if (_selectedScenarioItem
                        is ScenarioArtObject selected &&
                    requestedIds.Contains(selected.Id))
                {
                    _selectedScenarioItem =
                        null;

                    ScenarioMapCanvas.ClearSelection();
                    _ensemble3DViewport?.SelectItem(null);
                }

                RefreshArtObjectLayer();
                Refresh3DViewportNext(false);
                UpdateDirtyState();
            }

            CustomMeshPendingAssetService.QueueDelete(records);

            StatusText.Text =
                $"Queued {records.Count:N0} embedded custom mesh deletion(s) | " +
                "save the ERA to remove the SC2 placement and restore the stock UGX slot";
        }

        private HashSet<string> ResolveUsedUgxPathsForCurrentMap()
        {
            HashSet<string> result =
                new(StringComparer.OrdinalIgnoreCase);

            if (_currentArchive == null)
                return result;

            void AddAsset(string type, string editorName)
            {
                if (string.IsNullOrWhiteSpace(type) &&
                    string.IsNullOrWhiteSpace(editorName))
                {
                    return;
                }

                try
                {
                    UgxMeshService.UgxMeshAsset? asset =
                        UgxMeshService.TryLoadForObject(
                            _currentArchive,
                            type,
                            editorName);

                    if (asset != null &&
                        !string.IsNullOrWhiteSpace(asset.SourceFileName))
                    {
                        result.Add(
                            CustomMeshPendingAssetService
                                .NormalizeArchivePath(asset.SourceFileName));
                    }
                }
                catch
                {
                    // Missing visual mappings should not block a custom mesh;
                    // the visible object's type check still protects common
                    // collisions.
                }
            }

            foreach (ScenarioArtObject art in _currentArtObjects)
                AddAsset(art.Type, art.EditorName);

            if (ScenarioMapCanvas.Scenario is ScenarioMap map)
            {
                foreach (ScenarioObject obj in map.Objects)
                    AddAsset(obj.Type, obj.EditorName);
            }

            return result;
        }

        private static EraArchiveInfo ResolveBrowserArchive(
            string path,
            IReadOnlyDictionary<string, EraArchiveInfo> previewArchives)
        {
            string full = Path.GetFullPath(path);

            if (previewArchives.TryGetValue(full, out EraArchiveInfo? archive))
                return archive;

            return EraArchiveService.Open(full);
        }

        private static int GetCustomMeshTemplateRank(
            ObjectCatalogEntry entry)
        {
            string text =
                (entry.Type + " " + entry.InternalName + " " + entry.Name)
                    .ToLowerInvariant();

            if (text.Contains("crate", StringComparison.Ordinal)) return 0;
            if (text.Contains("prop", StringComparison.Ordinal)) return 10;
            if (text.Contains("debris", StringComparison.Ordinal)) return 20;
            if (text.Contains("rock", StringComparison.Ordinal)) return 30;
            if (text.Contains("boulder", StringComparison.Ordinal)) return 35;
            if (text.Contains("wall", StringComparison.Ordinal)) return 45;
            if (text.Contains("tree", StringComparison.Ordinal)) return 60;
            if (text.Contains("bush", StringComparison.Ordinal)) return 65;

            if (text.Contains("unit", StringComparison.Ordinal) ||
                text.Contains("vehicle", StringComparison.Ordinal) ||
                text.Contains("building", StringComparison.Ordinal) ||
                text.Contains("base", StringComparison.Ordinal))
            {
                return 500;
            }

            return 100;
        }
    }
}
