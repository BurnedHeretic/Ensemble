using Ensemble.Models;
using System.IO;
using System.Text.Json;

namespace Ensemble.Services
{
    /// <summary>
    /// Keeps generated map-local custom assets until the normal Ensemble save
    /// pipeline writes the ERA. v31 also persists a small registry inside the
    /// ERA so embedded custom meshes can later be selected and removed safely.
    /// </summary>
    internal static class CustomMeshPendingAssetService
    {
        public const string RegistryArchivePath =
            "ensemble\\custom_meshes.json";

        internal sealed class EmbeddedCustomMeshRecord
        {
            public string MeshId
            {
                get;
                set;
            } = string.Empty;

            public string DisplayName
            {
                get;
                set;
            } = string.Empty;

            public string ScenarioKey
            {
                get;
                set;
            } = string.Empty;

            public string UgxArchivePath
            {
                get;
                set;
            } = string.Empty;

            public int ArtObjectId
            {
                get;
                set;
            }

            /// <summary>
            /// Portable path/file-name hint for the stock ERA that supplied
            /// the rigid UGX template. Absolute user paths are never embedded
            /// into distributed map ERAs.
            /// </summary>
            public string TemplateEraHint
            {
                get;
                set;
            } = string.Empty;

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(DisplayName)
                    ? UgxArchivePath
                    : DisplayName;
            }
        }

        private sealed class RegistryDocument
        {
            public int Version
            {
                get;
                set;
            } = 1;

            public List<EmbeddedCustomMeshRecord> Meshes
            {
                get;
                set;
            } = new();
        }

        private sealed class PendingAsset
        {
            public required string ScenarioKey { get; set; }
            public required string ArchivePath { get; init; }
            public required byte[] Data { get; set; }
            public required string MeshId { get; init; }
            public required string DisplayName { get; init; }
            public required string TemplateEraHint { get; init; }
            public int ArtObjectId { get; set; }
            public byte CompressionMethod { get; init; }
            public byte AlignmentLog2 { get; init; }
            public ushort ResourceFlags { get; init; }
            public ulong Date { get; init; }
        }

        private sealed class PendingDeletion
        {
            public required EmbeddedCustomMeshRecord Record
            {
                get;
                init;
            }
        }

        private static readonly object Sync = new();

        private static readonly List<PendingAsset> Pending = new();
        private static readonly List<PendingDeletion> PendingDeletions = new();

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };

        public static void Register(
            string scenarioFile,
            string archivePath,
            byte[] data,
            string meshId,
            string displayName,
            string templateEraHint,
            byte compressionMethod,
            byte alignmentLog2,
            ushort resourceFlags,
            ulong date)
        {
            ArgumentNullException.ThrowIfNull(data);

            string scenarioKey = NormalizeScenarioKey(scenarioFile);
            string path = NormalizeArchivePath(archivePath);

            if (string.IsNullOrWhiteSpace(scenarioKey))
                throw new ArgumentException("Scenario file cannot be empty.", nameof(scenarioFile));

            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".ugx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Custom model target must be a Halo Wars .ugx archive path.");
            }

            if (data.Length == 0)
                throw new InvalidDataException("Generated UGX contains no data.");

            if (compressionMethod > 2)
                throw new InvalidDataException("The donor UGX uses an unsupported ERA compression method.");

            lock (Sync)
            {
                Pending.RemoveAll(
                    item =>
                        item.ScenarioKey.Equals(
                            scenarioKey,
                            StringComparison.OrdinalIgnoreCase) &&
                        item.ArchivePath.Equals(
                            path,
                            StringComparison.OrdinalIgnoreCase));

                Pending.Add(
                    new PendingAsset
                    {
                        ScenarioKey = scenarioKey,
                        ArchivePath = path,
                        Data = data.ToArray(),
                        MeshId = meshId ?? string.Empty,
                        DisplayName = displayName ?? string.Empty,
                        TemplateEraHint = templateEraHint ?? string.Empty,
                        CompressionMethod = compressionMethod,
                        AlignmentLog2 = alignmentLog2,
                        ResourceFlags = resourceFlags,
                        Date = date
                    });
            }
        }

        /// <summary>
        /// Associates the newly cloned SC2 object with the generated UGX so
        /// the persisted registry can remove both pieces later.
        /// </summary>
        public static void BindObject(
            string scenarioFile,
            string archivePath,
            int artObjectId)
        {
            if (artObjectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(artObjectId));

            string scenarioKey = NormalizeScenarioKey(scenarioFile);
            string path = NormalizeArchivePath(archivePath);

            lock (Sync)
            {
                PendingAsset? asset =
                    Pending.LastOrDefault(
                        item =>
                            item.ScenarioKey.Equals(
                                scenarioKey,
                                StringComparison.OrdinalIgnoreCase) &&
                            item.ArchivePath.Equals(
                                path,
                                StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    throw new InvalidOperationException(
                        "The generated custom UGX is no longer queued for this map.");
                }

                asset.ArtObjectId = artObjectId;
            }
        }

        public static void Remove(
            string scenarioFile,
            string archivePath)
        {
            string scenarioKey = NormalizeScenarioKey(scenarioFile);
            string path = NormalizeArchivePath(archivePath);

            lock (Sync)
            {
                Pending.RemoveAll(
                    item =>
                        item.ScenarioKey.Equals(
                            scenarioKey,
                            StringComparison.OrdinalIgnoreCase) &&
                        item.ArchivePath.Equals(
                            path,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        public static bool UpdateDataForArtObject(
            string scenarioFile,
            int artObjectId,
            byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (artObjectId <= 0 || data.Length == 0)
                return false;

            string scenarioKey = NormalizeScenarioKey(scenarioFile);

            lock (Sync)
            {
                PendingAsset? asset =
                    Pending.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                scenarioKey,
                                StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                    return false;

                asset.Data = data.ToArray();
                return true;
            }
        }

        public static bool TryGetPendingRecordForArtObject(
            string scenarioFile,
            int artObjectId,
            out EmbeddedCustomMeshRecord? record)
        {
            record = null;

            if (artObjectId <= 0)
                return false;

            string scenarioKey = NormalizeScenarioKey(scenarioFile);

            lock (Sync)
            {
                PendingAsset? asset =
                    Pending.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                scenarioKey,
                                StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                    return false;

                record =
                    new EmbeddedCustomMeshRecord
                    {
                        MeshId = asset.MeshId,
                        DisplayName = asset.DisplayName,
                        ScenarioKey = asset.ScenarioKey,
                        UgxArchivePath = asset.ArchivePath,
                        ArtObjectId = asset.ArtObjectId,
                        TemplateEraHint = asset.TemplateEraHint
                    };

                return true;
            }
        }

        public static bool CancelPendingPlacement(
            string scenarioFile,
            int artObjectId)
        {
            if (artObjectId <= 0)
                return false;

            string scenarioKey = NormalizeScenarioKey(scenarioFile);

            lock (Sync)
            {
                List<PendingAsset> matches =
                    Pending
                        .Where(
                            item =>
                                item.ArtObjectId == artObjectId &&
                                item.ScenarioKey.Equals(
                                    scenarioKey,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (matches.Count == 0)
                    return false;

                HashSet<string> paths =
                    matches
                        .Select(item => item.ArchivePath)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                Pending.RemoveAll(
                    item =>
                        item.ArtObjectId == artObjectId &&
                        item.ScenarioKey.Equals(
                            scenarioKey,
                            StringComparison.OrdinalIgnoreCase));

                PendingDeletions.RemoveAll(
                    item =>
                        item.Record.ArtObjectId == artObjectId ||
                        paths.Contains(
                            NormalizeArchivePath(
                                item.Record.UgxArchivePath)));

                return true;
            }
        }

        public static bool IsReserved(
            string scenarioFile,
            string archivePath)
        {
            string scenarioKey = NormalizeScenarioKey(scenarioFile);
            string path = NormalizeArchivePath(archivePath);

            lock (Sync)
            {
                return Pending.Any(
                    item =>
                        item.ScenarioKey.Equals(
                            scenarioKey,
                            StringComparison.OrdinalIgnoreCase) &&
                        item.ArchivePath.Equals(
                            path,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Converts the template archive path into a portable hint suitable
        /// for writing into a map ERA. Paths inside the detected Halo Wars
        /// folder are stored relative to that folder; external sources store
        /// only the ERA file name.
        /// </summary>
        public static string BuildTemplateEraHint(
            string sourceArchivePath)
        {
            if (string.IsNullOrWhiteSpace(sourceArchivePath))
                return string.Empty;

            try
            {
                string sourceFull = Path.GetFullPath(sourceArchivePath);
                string? gameDirectory = HaloWarsAssetArchiveService.GameDirectory;

                if (!string.IsNullOrWhiteSpace(gameDirectory))
                {
                    string gameFull =
                        Path.GetFullPath(gameDirectory)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    string relative = Path.GetRelativePath(gameFull, sourceFull);

                    if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                        !relative.Equals("..", StringComparison.Ordinal))
                    {
                        return relative.Replace('/', '\\');
                    }
                }
            }
            catch
            {
                // File-name fallback below remains portable.
            }

            return Path.GetFileName(sourceArchivePath);
        }

        public static IReadOnlyList<EmbeddedCustomMeshRecord> LoadEmbedded(
            string eraPath)
        {
            if (string.IsNullOrWhiteSpace(eraPath) ||
                !File.Exists(eraPath))
            {
                return Array.Empty<EmbeddedCustomMeshRecord>();
            }

            EraArchiveInfo archive = EraArchiveService.Open(eraPath);
            return ReadRegistryFromArchive(archive)
                .Select(CloneRecord)
                .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.ArtObjectId)
                .ToList();
        }

        public static void QueueDelete(
            IEnumerable<EmbeddedCustomMeshRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            lock (Sync)
            {
                foreach (EmbeddedCustomMeshRecord source in records)
                {
                    EmbeddedCustomMeshRecord record = CloneRecord(source);

                    if (string.IsNullOrWhiteSpace(record.UgxArchivePath))
                        continue;

                    PendingDeletions.RemoveAll(
                        item => RecordsMatch(item.Record, record));

                    PendingDeletions.Add(
                        new PendingDeletion
                        {
                            Record = record
                        });
                }
            }
        }

        public static byte[] InjectPending(
            byte[] encryptedEra,
            string scenarioFile)
        {
            ArgumentNullException.ThrowIfNull(encryptedEra);

            string scenarioKey = NormalizeScenarioKey(scenarioFile);
            List<PendingAsset> assets = GetPendingAssetsForScenario(scenarioKey);
            List<PendingDeletion> deletions = GetPendingDeletionsForScenario(scenarioKey);

            // If a mesh was embedded earlier in this same editor session, its
            // generated UGX remains in Pending so subsequent Ctrl+S operations
            // can re-inject it. A queued deletion must win over that retained
            // pending addition rather than silently adding the model back.
            if (deletions.Count > 0)
            {
                HashSet<string> deletedPaths =
                    deletions
                        .Select(item => NormalizeArchivePath(item.Record.UgxArchivePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                assets.RemoveAll(
                    item => deletedPaths.Contains(item.ArchivePath));
            }

            if (assets.Count == 0 && deletions.Count == 0)
                return encryptedEra;

            string tempPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Ensemble_CustomMesh_" +
                    Guid.NewGuid().ToString("N") +
                    ".era");

            try
            {
                File.WriteAllBytes(tempPath, encryptedEra);

                EraArchiveInfo archive = EraArchiveService.Open(tempPath);

                Dictionary<int, byte[]> replacements = new();
                List<EraFileAddition> additions = new();
                HashSet<string> additionNames =
                    new(StringComparer.OrdinalIgnoreCase);

                List<EmbeddedCustomMeshRecord> registry =
                    ReadRegistryFromArchive(archive);

                EraChunkInfo? registryChunk =
                    archive.Chunks.FirstOrDefault(
                        chunk =>
                            NormalizeArchivePath(chunk.FileName)
                                .Equals(
                                    RegistryArchivePath,
                                    StringComparison.OrdinalIgnoreCase));

                // =========================================================
                // DELETE / RESTORE EMBEDDED CUSTOM MESHES
                // =========================================================
                if (deletions.Count > 0)
                {
                    List<EmbeddedCustomMeshRecord> recordsToDelete =
                        deletions
                            .Select(item => ResolveRegistryRecord(registry, item.Record))
                            .Where(record => record != null)
                            .Select(record => record!)
                            .DistinctBy(
                                record =>
                                    NormalizeArchivePath(record.UgxArchivePath),
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    if (recordsToDelete.Count > 0)
                    {
                        RemovePlacedArtObjects(
                            archive,
                            scenarioKey,
                            recordsToDelete,
                            replacements);

                        foreach (EmbeddedCustomMeshRecord record in recordsToDelete)
                        {
                            string ugxPath = NormalizeArchivePath(record.UgxArchivePath);

                            EraChunkInfo? customChunk =
                                archive.Chunks.FirstOrDefault(
                                    chunk =>
                                        NormalizeArchivePath(chunk.FileName)
                                            .Equals(
                                                ugxPath,
                                                StringComparison.OrdinalIgnoreCase));

                            if (customChunk != null)
                            {
                                // Restore the borrowed stock UGX. The archive
                                // may retain this now-stock chunk, but the
                                // custom geometry itself is completely removed
                                // and no placed ArtObject references it.
                                replacements[customChunk.Index] =
                                    LoadStockTemplateUgx(record);
                            }

                            registry.RemoveAll(
                                existing => RecordsMatch(existing, record));
                        }
                    }
                }

                // =========================================================
                // ADD / REPLACE GENERATED CUSTOM UGX FILES
                // =========================================================
                foreach (PendingAsset asset in assets)
                {
                    if (asset.ArtObjectId <= 0)
                    {
                        throw new InvalidDataException(
                            $"Custom mesh '{asset.DisplayName}' has no bound SC2 ArtObject ID.");
                    }

                    EraChunkInfo? existing =
                        archive.Chunks.FirstOrDefault(
                            chunk =>
                                NormalizeArchivePath(chunk.FileName)
                                    .Equals(
                                        asset.ArchivePath,
                                        StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        replacements[existing.Index] = asset.Data;
                    }
                    else if (additionNames.Add(asset.ArchivePath))
                    {
                        additions.Add(
                            new EraFileAddition
                            {
                                FileName = asset.ArchivePath,
                                Data = asset.Data,
                                CompressionMethod = asset.CompressionMethod,
                                AlignmentLog2 = asset.AlignmentLog2,
                                ResourceFlags = asset.ResourceFlags,
                                Date = asset.Date
                            });
                    }

                    EmbeddedCustomMeshRecord record =
                        new()
                        {
                            MeshId = asset.MeshId,
                            DisplayName = asset.DisplayName,
                            ScenarioKey = scenarioKey,
                            UgxArchivePath = asset.ArchivePath,
                            ArtObjectId = asset.ArtObjectId,
                            TemplateEraHint = asset.TemplateEraHint
                        };

                    registry.RemoveAll(
                        existingRecord =>
                            RecordsMatch(existingRecord, record) ||
                            NormalizeArchivePath(existingRecord.UgxArchivePath)
                                .Equals(
                                    asset.ArchivePath,
                                    StringComparison.OrdinalIgnoreCase));

                    registry.Add(record);
                }

                // =========================================================
                // EMBED / UPDATE ENSEMBLE CUSTOM-MESH REGISTRY
                // =========================================================
                RegistryDocument registryDocument =
                    new()
                    {
                        Version = 1,
                        Meshes = registry
                            .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(record => record.ArtObjectId)
                            .Select(CloneRecord)
                            .ToList()
                    };

                byte[] registryData =
                    JsonSerializer.SerializeToUtf8Bytes(
                        registryDocument,
                        JsonOptions);

                if (registryChunk != null)
                {
                    replacements[registryChunk.Index] = registryData;
                }
                else if (registry.Count > 0)
                {
                    additions.Add(
                        new EraFileAddition
                        {
                            FileName = RegistryArchivePath,
                            Data = registryData,
                            CompressionMethod = 0,
                            AlignmentLog2 = 2,
                            ResourceFlags = 0
                        });
                }

                if (replacements.Count == 0 && additions.Count == 0)
                    return encryptedEra;

                byte[] rebuilt =
                    EraRebuildService.BuildModifiedEra(
                        archive,
                        replacements,
                        new Dictionary<int, string>(),
                        additions);

                // Subsequent Ctrl+S operations should use the Save-As
                // scenario basename without relying on directory fallback.
                lock (Sync)
                {
                    HashSet<string> injectedPaths =
                        assets
                            .Select(item => item.ArchivePath)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (PendingAsset item in Pending)
                    {
                        if (injectedPaths.Contains(item.ArchivePath) &&
                            assets.Any(
                                source =>
                                    source.ArchivePath.Equals(
                                        item.ArchivePath,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    source.ScenarioKey.Equals(
                                        item.ScenarioKey,
                                        StringComparison.OrdinalIgnoreCase)))
                        {
                            item.ScenarioKey = scenarioKey;
                        }
                    }

                    foreach (PendingDeletion deletion in deletions)
                    {
                        PendingDeletions.RemoveAll(
                            queued => RecordsMatch(queued.Record, deletion.Record));

                        Pending.RemoveAll(
                            item =>
                                NormalizeArchivePath(item.ArchivePath)
                                    .Equals(
                                        NormalizeArchivePath(deletion.Record.UgxArchivePath),
                                        StringComparison.OrdinalIgnoreCase));
                    }
                }

                return rebuilt;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Temporary cleanup failure must not hide the actual
                    // save/conversion result.
                }
            }
        }

        private static List<PendingAsset> GetPendingAssetsForScenario(
            string scenarioKey)
        {
            lock (Sync)
            {
                List<PendingAsset> matches =
                    Pending
                        .Where(
                            item =>
                                item.ScenarioKey.Equals(
                                    scenarioKey,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (matches.Count == 0)
                {
                    string directory = GetScenarioDirectory(scenarioKey);

                    List<PendingAsset> directoryMatches =
                        Pending
                            .Where(
                                item =>
                                    GetScenarioDirectory(item.ScenarioKey)
                                        .Equals(
                                            directory,
                                            StringComparison.OrdinalIgnoreCase))
                            .ToList();

                    int distinctSources =
                        directoryMatches
                            .Select(item => item.ScenarioKey)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                    if (distinctSources == 1)
                        matches = directoryMatches;
                }

                return matches.ToList();
            }
        }

        private static List<PendingDeletion> GetPendingDeletionsForScenario(
            string scenarioKey)
        {
            lock (Sync)
            {
                List<PendingDeletion> exact =
                    PendingDeletions
                        .Where(
                            item =>
                                NormalizeScenarioKey(item.Record.ScenarioKey)
                                    .Equals(
                                        scenarioKey,
                                        StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (exact.Count > 0)
                    return exact;

                string directory = GetScenarioDirectory(scenarioKey);

                List<PendingDeletion> directoryMatches =
                    PendingDeletions
                        .Where(
                            item =>
                                GetScenarioDirectory(
                                    NormalizeScenarioKey(item.Record.ScenarioKey))
                                    .Equals(
                                        directory,
                                        StringComparison.OrdinalIgnoreCase))
                        .ToList();

                int distinctSources =
                    directoryMatches
                        .Select(item => NormalizeScenarioKey(item.Record.ScenarioKey))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                return distinctSources == 1
                    ? directoryMatches
                    : new List<PendingDeletion>();
            }
        }

        private static void RemovePlacedArtObjects(
            EraArchiveInfo archive,
            string scenarioKey,
            IReadOnlyCollection<EmbeddedCustomMeshRecord> records,
            IDictionary<int, byte[]> replacements)
        {
            List<int> requestedIds =
                records
                    .Select(record => record.ArtObjectId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

            if (requestedIds.Count == 0)
                return;

            string sc2Path = BuildSc2ArchivePath(scenarioKey);

            EraChunkInfo? sc2Chunk =
                archive.Chunks.FirstOrDefault(
                    chunk =>
                        NormalizeArchivePath(chunk.FileName)
                            .Equals(
                                sc2Path,
                                StringComparison.OrdinalIgnoreCase));

            if (sc2Chunk == null)
            {
                throw new InvalidDataException(
                    "The map's SC2 ArtObjects file could not be found while removing a custom mesh.\n\n" +
                    sc2Path);
            }

            byte[] sourceSc2 =
                replacements.TryGetValue(sc2Chunk.Index, out byte[]? alreadyModified)
                    ? alreadyModified
                    : EraExtractionService.ExtractChunk(archive, sc2Chunk);

            HashSet<int> existingIds =
                ScenarioArtObjectsService.ReadArtObjects(sourceSc2)
                    .Select(obj => obj.Id)
                    .ToHashSet();

            List<int> idsToRemove =
                requestedIds
                    .Where(existingIds.Contains)
                    .ToList();

            if (idsToRemove.Count == 0)
                return;

            byte[] modified =
                ScenarioArtObjectsService.RemoveObjects(
                    sourceSc2,
                    idsToRemove,
                    out _);

            replacements[sc2Chunk.Index] = modified;
        }

        private static byte[] LoadStockTemplateUgx(
            EmbeddedCustomMeshRecord record)
        {
            string? gameDirectory = HaloWarsAssetArchiveService.GameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory) ||
                !Directory.Exists(gameDirectory))
            {
                throw new InvalidOperationException(
                    "Halo Wars game assets must be configured before an embedded custom mesh can be removed.\n\n" +
                    "Use Tools > Locate Halo Wars Game Assets first.");
            }

            string fileName = Path.GetFileName(record.TemplateEraHint);
            List<string> candidates = new();

            if (!string.IsNullOrWhiteSpace(record.TemplateEraHint))
            {
                string hinted =
                    Path.Combine(
                        gameDirectory,
                        record.TemplateEraHint
                            .Replace('\\', Path.DirectorySeparatorChar)
                            .Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(hinted))
                    candidates.Add(hinted);
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                try
                {
                    foreach (string path in Directory.EnumerateFiles(
                                 gameDirectory,
                                 fileName,
                                 SearchOption.AllDirectories))
                    {
                        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
                            candidates.Add(path);
                    }
                }
                catch
                {
                    // Try the directly hinted candidate(s) gathered above.
                }
            }

            string ugxPath = NormalizeArchivePath(record.UgxArchivePath);

            foreach (string candidate in candidates)
            {
                try
                {
                    EraArchiveInfo archive = EraArchiveService.Open(candidate);

                    EraChunkInfo? chunk =
                        archive.Chunks.FirstOrDefault(
                            item =>
                                NormalizeArchivePath(item.FileName)
                                    .Equals(
                                        ugxPath,
                                        StringComparison.OrdinalIgnoreCase));

                    if (chunk != null)
                        return EraExtractionService.ExtractChunk(archive, chunk);
                }
                catch
                {
                    // Continue with another archive having the same file name.
                }
            }

            throw new InvalidOperationException(
                "Ensemble could not locate the original stock UGX used by this custom mesh.\n\n" +
                $"Template ERA: {record.TemplateEraHint}\n" +
                $"UGX: {record.UgxArchivePath}");
        }

        private static List<EmbeddedCustomMeshRecord> ReadRegistryFromArchive(
            EraArchiveInfo archive)
        {
            EraChunkInfo? chunk =
                archive.Chunks.FirstOrDefault(
                    item =>
                        NormalizeArchivePath(item.FileName)
                            .Equals(
                                RegistryArchivePath,
                                StringComparison.OrdinalIgnoreCase));

            if (chunk == null)
                return new List<EmbeddedCustomMeshRecord>();

            try
            {
                byte[] data = EraExtractionService.ExtractChunk(archive, chunk);

                RegistryDocument? document =
                    JsonSerializer.Deserialize<RegistryDocument>(
                        data,
                        JsonOptions);

                if (document == null || document.Version != 1)
                    return new List<EmbeddedCustomMeshRecord>();

                return document.Meshes
                    .Where(record => !string.IsNullOrWhiteSpace(record.UgxArchivePath))
                    .Select(CloneRecord)
                    .ToList();
            }
            catch
            {
                return new List<EmbeddedCustomMeshRecord>();
            }
        }

        private static EmbeddedCustomMeshRecord? ResolveRegistryRecord(
            IReadOnlyCollection<EmbeddedCustomMeshRecord> registry,
            EmbeddedCustomMeshRecord queued)
        {
            EmbeddedCustomMeshRecord? exact =
                registry.FirstOrDefault(record => RecordsMatch(record, queued));

            return exact ?? CloneRecord(queued);
        }

        private static bool RecordsMatch(
            EmbeddedCustomMeshRecord left,
            EmbeddedCustomMeshRecord right)
        {
            if (!string.IsNullOrWhiteSpace(left.MeshId) &&
                !string.IsNullOrWhiteSpace(right.MeshId) &&
                left.MeshId.Equals(right.MeshId, StringComparison.OrdinalIgnoreCase) &&
                left.ArtObjectId == right.ArtObjectId)
            {
                return true;
            }

            return left.ArtObjectId == right.ArtObjectId &&
                   NormalizeArchivePath(left.UgxArchivePath)
                       .Equals(
                           NormalizeArchivePath(right.UgxArchivePath),
                           StringComparison.OrdinalIgnoreCase);
        }

        private static EmbeddedCustomMeshRecord CloneRecord(
            EmbeddedCustomMeshRecord record)
        {
            return new EmbeddedCustomMeshRecord
            {
                MeshId = record.MeshId ?? string.Empty,
                DisplayName = record.DisplayName ?? string.Empty,
                ScenarioKey = NormalizeScenarioKey(record.ScenarioKey),
                UgxArchivePath = NormalizeArchivePath(record.UgxArchivePath),
                ArtObjectId = record.ArtObjectId,
                TemplateEraHint = record.TemplateEraHint ?? string.Empty
            };
        }

        private static string BuildSc2ArchivePath(
            string scenarioKey)
        {
            string normalized = NormalizeScenarioKey(scenarioKey);

            if (normalized.EndsWith(".scn", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^4];

            return NormalizeArchivePath(
                "scenario\\" + normalized + ".sc2.xmb");
        }

        private static string GetScenarioDirectory(
            string scenarioKey)
        {
            int slash =
                (scenarioKey ?? string.Empty)
                    .LastIndexOf('\\');

            return slash >= 0
                ? scenarioKey[..slash]
                : string.Empty;
        }

        public static string NormalizeScenarioKey(
            string value)
        {
            string result =
                (value ?? string.Empty)
                    .Trim()
                    .Replace('/', '\\')
                    .TrimStart('\\');

            if (result.StartsWith(
                    "scenario\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = result["scenario\\".Length..];
            }

            if (result.EndsWith(
                    ".xmb",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^4];
            }

            return result.ToLowerInvariant();
        }

        public static string NormalizeArchivePath(
            string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace('/', '\\')
                .TrimStart('\\');
        }
    }
}
