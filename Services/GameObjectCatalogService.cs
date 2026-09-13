using Ensemble.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace Ensemble.Services
{
    internal sealed class GameObjectCatalogLoadResult
    {
        public string? GameDirectory
        {
            get;
            init;
        }

        public int EraFileCount
        {
            get;
            set;
        }

        public int RawObjectCount
        {
            get;
            set;
        }

        public int DuplicateCount
        {
            get;
            set;
        }

        public List<ObjectCatalogEntry> Entries
        {
            get;
            init;
        } =
            new();

        public Dictionary<string, EraArchiveInfo> Archives
        {
            get;
            init;
        } =
            new(
                StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings
        {
            get;
            init;
        } =
            new();
    }

    /// <summary>
    /// Builds the Add Object library from every ERA inside the detected
    /// Halo Wars game directory. The currently-open ERA is always included,
    /// even when it is a custom copy stored outside the game installation.
    /// </summary>
    internal static class GameObjectCatalogService
    {
        public static GameObjectCatalogLoadResult LoadGameLibrary(
            string currentEraPath)
        {
            if (string.IsNullOrWhiteSpace(
                    currentEraPath))
            {
                throw new ArgumentException(
                    "Current ERA path cannot be empty.",
                    nameof(currentEraPath));
            }

            string currentFullPath =
                Path.GetFullPath(
                    currentEraPath);

            EraArchiveInfo currentArchive =
                EraArchiveService.Open(
                    currentFullPath);

            // This is also the renderer's established game-install detector.
            // Calling it here means Object Browser automatically reuses the
            // configured/detected Halo Wars installation rather than asking
            // the user to browse map-by-map.
            _ = HaloWarsAssetArchiveService
                .GetSearchArchives(
                    currentArchive);

            string? gameDirectory =
                HaloWarsAssetArchiveService
                    .GameDirectory;

            GameObjectCatalogLoadResult result =
                new()
                {
                    GameDirectory =
                        gameDirectory
                };

            HashSet<string> eraPaths =
                new(
                    StringComparer.OrdinalIgnoreCase)
                {
                    currentFullPath
                };

            if (!string.IsNullOrWhiteSpace(
                    gameDirectory) &&
                Directory.Exists(
                    gameDirectory))
            {
                foreach (string path
                         in EnumerateEraFilesSafe(
                             gameDirectory))
                {
                    eraPaths.Add(
                        Path.GetFullPath(
                            path));
                }
            }

            // Microsoft Store / Xbox app builds keep ModManifest-compatible
            // content in the package LocalState sandbox. Include ERAs placed
            // there as part of the global object library as well, so Store
            // users can browse installed mods/custom map assets without
            // manually changing the source ERA.
            string storeLocalState =
                HaloWarsAssetArchiveService
                    .WindowsStoreLocalStateDirectory;

            if (Directory.Exists(
                    storeLocalState))
            {
                foreach (string path
                         in EnumerateEraFilesSafe(
                             storeLocalState))
                {
                    eraPaths.Add(
                        Path.GetFullPath(
                            path));
                }
            }

            List<string> orderedPaths =
                eraPaths
                    .OrderBy(
                        path =>
                            string.Equals(
                                path,
                                currentFullPath,
                                StringComparison.OrdinalIgnoreCase)
                                ? 0
                                : 1)
                    .ThenBy(
                        path =>
                            Path.GetFileName(
                                path),
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        path =>
                            path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            result.EraFileCount =
                orderedPaths.Count;

            List<ObjectCatalogEntry> rawEntries =
                new();

            foreach (string eraPath
                     in orderedPaths)
            {
                try
                {
                    ObjectCatalogLoadResult load =
                        CrossEraObjectCatalogService
                            .Load(
                                eraPath,
                                includeImportedMeshes:
                                    false);

                    if (load.Archive !=
                        null)
                    {
                        result.Archives[
                            Path.GetFullPath(
                                eraPath)] =
                                    load.Archive;
                    }

                    rawEntries.AddRange(
                        load.Entries);

                    foreach (string warning
                             in load.Warnings)
                    {
                        result.Warnings.Add(
                            Path.GetFileName(
                                eraPath) +
                            ": " +
                            warning);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(
                        Path.GetFileName(
                            eraPath) +
                        ": " +
                        ex.Message);
                }
            }

            rawEntries.AddRange(
                CrossEraObjectCatalogService
                    .LoadImportedMeshEntries());

            result.RawObjectCount =
                rawEntries.Count;

            List<ObjectCatalogEntry> deduplicated =
                Deduplicate(
                    rawEntries,
                    currentFullPath);

            result.DuplicateCount =
                Math.Max(
                    0,
                    rawEntries.Count -
                    deduplicated.Count);

            result.Entries.AddRange(
                deduplicated);

            return result;
        }

        public static List<ObjectCatalogEntry> Deduplicate(
            IEnumerable<ObjectCatalogEntry> entries,
            string? preferredEraPath =
                null)
        {
            string preferredFullPath =
                string.IsNullOrWhiteSpace(
                    preferredEraPath)
                    ? string.Empty
                    : Path.GetFullPath(
                        preferredEraPath);

            Dictionary<string, ObjectCatalogEntry> unique =
                new(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ObjectCatalogEntry entry
                     in entries)
            {
                string key =
                    BuildIdentityKey(
                        entry);

                if (!unique.TryGetValue(
                        key,
                        out ObjectCatalogEntry? existing))
                {
                    unique[
                        key] =
                            entry;

                    continue;
                }

                // Prefer a template already present in the target/current map.
                // This avoids unnecessary cross-ERA dependency transfer when
                // two maps contain the exact same object type.
                bool candidatePreferred =
                    IsFromEra(
                        entry,
                        preferredFullPath);

                bool existingPreferred =
                    IsFromEra(
                        existing,
                        preferredFullPath);

                if (candidatePreferred &&
                    !existingPreferred)
                {
                    unique[
                        key] =
                            entry;
                }
            }

            return unique
                .Values
                .OrderBy(
                    entry =>
                        entry.Layer)
                .ThenBy(
                    entry =>
                        entry.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    entry =>
                        entry.Type,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsFromEra(
            ObjectCatalogEntry entry,
            string preferredFullPath)
        {
            if (string.IsNullOrWhiteSpace(
                    preferredFullPath) ||
                string.IsNullOrWhiteSpace(
                    entry.DonorEraPath))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(
                        entry.DonorEraPath),
                    preferredFullPath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildIdentityKey(
            ObjectCatalogEntry entry)
        {
            if (entry.Layer ==
                ObjectCatalogLayer.ImportedMesh)
            {
                string mesh =
                    entry.ImportedMesh?.FileName
                    ??
                    entry.SourceFileName;

                return
                    "MESH|" +
                    NormaliseToken(
                        mesh);
            }

            int variation =
                entry.ScenarioObject?
                    .VisualVariationIndex
                ??
                entry.ArtObject?
                    .VisualVariationIndex
                ??
                0;

            string type =
                NormaliseToken(
                    entry.Type);

            string friendlyName =
                NormaliseToken(
                    entry.Name);

            if (!string.IsNullOrWhiteSpace(
                    type))
            {
                return
                    entry.Layer +
                    "|" +
                    type +
                    "|" +
                    friendlyName +
                    "|VAR=" +
                    variation;
            }

            // Very old/odd XMB objects can have no useful Type. Strip only a
            // final instance-number suffix so repeated placements such as
            // foo_17 / foo_83 still collapse to one template.
            string fallback =
                Regex.Replace(
                    entry.InternalName
                    ??
                    string.Empty,
                    @"[_\-\s]+\d+$",
                    string.Empty,
                    RegexOptions.CultureInvariant);

            return
                entry.Layer +
                "|" +
                NormaliseToken(
                    fallback) +
                "|VAR=" +
                variation;
        }

        private static string NormaliseToken(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return string.Empty;
            }

            return Regex.Replace(
                    value
                        .Trim()
                        .ToLowerInvariant(),
                    @"[^a-z0-9]+",
                    string.Empty,
                    RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> EnumerateEraFilesSafe(
            string rootDirectory)
        {
            Stack<string> pending =
                new();

            HashSet<string> visited =
                new(
                    StringComparer.OrdinalIgnoreCase);

            pending.Push(
                Path.GetFullPath(
                    rootDirectory));

            while (pending.Count >
                0)
            {
                string directory =
                    pending.Pop();

                if (!visited.Add(
                        directory))
                {
                    continue;
                }

                string[] files;

                try
                {
                    files =
                        Directory.GetFiles(
                            directory,
                            "*.era",
                            SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    files =
                        Array.Empty<string>();
                }

                foreach (string file
                         in files)
                {
                    string name =
                        Path.GetFileName(
                            file);

                    // Never catalogue Ensemble's temporary save file should a
                    // failed/interrupted save leave one behind.
                    if (name.EndsWith(
                            ".ensemble.tmp.era",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return file;
                }

                string[] children;

                try
                {
                    children =
                        Directory.GetDirectories(
                            directory);
                }
                catch
                {
                    children =
                        Array.Empty<string>();
                }

                foreach (string child
                         in children)
                {
                    pending.Push(
                        child);
                }
            }
        }
    }
}
