using Ensemble.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace Ensemble.Services
{
    /// <summary>
    /// Provides the 3D viewport with Halo Wars' shared/global asset ERAs.
    ///
    /// Scenario ERAs only contain assets needed specifically by that map.
    /// Many gameplay objects resolve their visual mesh from root.era,
    /// root_update.era, scenarioshared.era or DLC archives instead.
    /// </summary>
    internal static class HaloWarsAssetArchiveService
    {
        private static readonly object Sync =
            new();

        private static readonly string[] PreferredArchiveNames =
        {
            "root_update.era",
            "root.era",
            "scenarioshared.era",
            "dlc02.era",
            "dlc01.era"
        };

        private static string? _gameDirectory;
        private static List<EraArchiveInfo>? _globalArchives;
        private static bool _autoDetectionAttempted;

        public static string? GameDirectory
        {
            get
            {
                lock (Sync)
                {
                    return _gameDirectory;
                }
            }
        }

        private static string SettingsDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Ensemble");

        private static string SettingsPath =>
            Path.Combine(
                SettingsDirectory,
                "halo_wars_asset_directory.txt");

        public static IReadOnlyList<EraArchiveInfo> GetSearchArchives(
            EraArchiveInfo currentArchive)
        {
            ArgumentNullException.ThrowIfNull(
                currentArchive);

            EnsureGlobalArchives(
                currentArchive);

            List<EraArchiveInfo> result =
                new()
                {
                    currentArchive
                };

            HashSet<string> seen =
                new(
                    StringComparer.OrdinalIgnoreCase)
                {
                    Path.GetFullPath(
                        currentArchive.FilePath)
                };

            lock (Sync)
            {
                if (_globalArchives != null)
                {
                    foreach (EraArchiveInfo archive
                             in _globalArchives)
                    {
                        string full =
                            Path.GetFullPath(
                                archive.FilePath);

                        if (seen.Add(full))
                        {
                            result.Add(
                                archive);
                        }
                    }
                }
            }

            return result;
        }

        public static bool ConfigureFromRootEra(
            string selectedEraPath,
            out string message)
        {
            message =
                string.Empty;

            if (string.IsNullOrWhiteSpace(
                    selectedEraPath) ||
                !File.Exists(
                    selectedEraPath))
            {
                message =
                    "The selected ERA file does not exist.";

                return false;
            }

            string? directory =
                Path.GetDirectoryName(
                    Path.GetFullPath(
                        selectedEraPath));

            if (string.IsNullOrWhiteSpace(
                    directory))
            {
                message =
                    "The selected ERA has no parent directory.";

                return false;
            }

            string? gameDirectory =
                FindGameDirectoryNear(
                    directory,
                    3);

            if (gameDirectory ==
                null)
            {
                // Accept the selected directory when root.era itself was
                // selected, even if xgameFinal.exe is stored elsewhere.
                if (Path.GetFileName(
                        selectedEraPath)
                    .Equals(
                        "root.era",
                        StringComparison.OrdinalIgnoreCase))
                {
                    gameDirectory =
                        directory;
                }
            }

            if (gameDirectory ==
                null)
            {
                message =
                    "Ensemble could not find root.era in or near the selected directory.";

                return false;
            }

            lock (Sync)
            {
                _gameDirectory =
                    gameDirectory;

                _globalArchives =
                    null;

                _autoDetectionAttempted =
                    true;
            }

            SaveConfiguredDirectory(
                gameDirectory);

            int archiveCount =
                LoadGlobalArchives()
                    .Count;

            UgxMeshService.ClearCaches();

            message =
                archiveCount >
                    0
                    ? $"Halo Wars assets configured from {gameDirectory}. " +
                      $"Loaded {archiveCount} shared ERA archive(s)."
                    : "The directory was saved, but no supported shared ERA archives could be opened.";

            return archiveCount >
                0;
        }

        public static string Rescan(
            EraArchiveInfo? currentArchive)
        {
            lock (Sync)
            {
                _globalArchives =
                    null;

                _autoDetectionAttempted =
                    false;

                _gameDirectory =
                    null;
            }

            UgxMeshService.ClearCaches();

            if (currentArchive !=
                null)
            {
                EnsureGlobalArchives(
                    currentArchive);
            }

            string? directory =
                GameDirectory;

            int count;

            lock (Sync)
            {
                count =
                    _globalArchives?.Count
                    ??
                    0;
            }

            return directory ==
                null
                ? "Halo Wars shared asset archives were not auto-detected. Use Tools > Locate Halo Wars Game Assets."
                : $"Halo Wars assets: {directory} | {count} shared ERA archive(s).";
        }

        private static void EnsureGlobalArchives(
            EraArchiveInfo currentArchive)
        {
            lock (Sync)
            {
                if (_globalArchives !=
                    null)
                {
                    return;
                }
            }

            string? detected =
                null;

            lock (Sync)
            {
                if (!_autoDetectionAttempted)
                {
                    _autoDetectionAttempted =
                        true;
                }
                else
                {
                    detected =
                        _gameDirectory;
                }
            }

            if (detected ==
                null)
            {
                detected =
                    LoadConfiguredDirectory();
            }

            if (detected ==
                null)
            {
                detected =
                    AutoDetectGameDirectory(
                        currentArchive);
            }

            lock (Sync)
            {
                _gameDirectory =
                    detected;
            }

            _ = LoadGlobalArchives();
        }

        private static List<EraArchiveInfo> LoadGlobalArchives()
        {
            lock (Sync)
            {
                if (_globalArchives !=
                    null)
                {
                    return _globalArchives;
                }
            }

            List<EraArchiveInfo> archives =
                new();

            string? directory =
                GameDirectory;

            if (!string.IsNullOrWhiteSpace(
                    directory) &&
                Directory.Exists(
                    directory))
            {
                HashSet<string> opened =
                    new(
                        StringComparer.OrdinalIgnoreCase);

                foreach (string preferred
                         in PreferredArchiveNames)
                {
                    string? path =
                        FindFileNear(
                            directory,
                            preferred,
                            3);

                    TryOpenArchive(
                        path,
                        opened,
                        archives);
                }

                // Some PC installs ship similarly-named update/shared ERAs.
                // Include only plausible root/shared/DLC archives; scenario
                // archives are intentionally not swept into the global set.
                foreach (string eraPath
                         in EnumerateNearbyEraFiles(
                             directory,
                             2))
                {
                    string fileName =
                        Path.GetFileName(
                            eraPath);

                    bool plausible =
                        fileName.StartsWith(
                            "root",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        fileName.Contains(
                            "shared",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        fileName.StartsWith(
                            "dlc",
                            StringComparison.OrdinalIgnoreCase);

                    if (plausible)
                    {
                        TryOpenArchive(
                            eraPath,
                            opened,
                            archives);
                    }
                }
            }

            lock (Sync)
            {
                _globalArchives =
                    archives;

                return _globalArchives;
            }
        }

        private static void TryOpenArchive(
            string? path,
            HashSet<string> opened,
            List<EraArchiveInfo> destination)
        {
            if (string.IsNullOrWhiteSpace(
                    path) ||
                !File.Exists(
                    path))
            {
                return;
            }

            string full =
                Path.GetFullPath(
                    path);

            if (!opened.Add(full))
            {
                return;
            }

            try
            {
                destination.Add(
                    EraArchiveService.Open(
                        full));
            }
            catch
            {
                // A damaged/unsupported optional archive should not stop the
                // map viewport from loading the scenario ERA itself.
            }
        }

        private static string? LoadConfiguredDirectory()
        {
            try
            {
                if (!File.Exists(
                        SettingsPath))
                {
                    return null;
                }

                string directory =
                    File.ReadAllText(
                            SettingsPath)
                        .Trim();

                return Directory.Exists(
                    directory)
                    ? directory
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveConfiguredDirectory(
            string directory)
        {
            try
            {
                Directory.CreateDirectory(
                    SettingsDirectory);

                File.WriteAllText(
                    SettingsPath,
                    directory);
            }
            catch
            {
                // The renderer can still use the directory for this session.
            }
        }

        private static string? AutoDetectGameDirectory(
            EraArchiveInfo currentArchive)
        {
            List<string> startingPoints =
                new();

            AddDirectoryWithParents(
                startingPoints,
                Path.GetDirectoryName(
                    currentArchive.FilePath));

            AddDirectoryWithParents(
                startingPoints,
                AppContext.BaseDirectory);

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            AddSteamCommonDirectory(
                startingPoints,
                Path.Combine(
                    programFilesX86,
                    "Steam",
                    "steamapps",
                    "common"));

            AddSteamCommonDirectory(
                startingPoints,
                Path.Combine(
                    programFiles,
                    "Steam",
                    "steamapps",
                    "common"));

            foreach (string steamRoot
                     in new[]
                     {
                         Path.Combine(
                             programFilesX86,
                             "Steam"),

                         Path.Combine(
                             programFiles,
                             "Steam")
                     })
            {
                foreach (string library
                         in ReadSteamLibraries(
                             steamRoot))
                {
                    AddSteamCommonDirectory(
                        startingPoints,
                        Path.Combine(
                            library,
                            "steamapps",
                            "common"));
                }
            }

            foreach (string candidate
                     in startingPoints
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase))
            {
                string? found =
                    FindGameDirectoryNear(
                        candidate,
                        1);

                if (found !=
                    null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AddDirectoryWithParents(
            List<string> destination,
            string? path)
        {
            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            try
            {
                DirectoryInfo? current =
                    new DirectoryInfo(
                        path);

                for (int i = 0;
                     current !=
                         null &&
                     i <
                         3;
                     i++)
                {
                    destination.Add(
                        current.FullName);

                    current =
                        current.Parent;
                }
            }
            catch
            {
            }
        }

        private static void AddSteamCommonDirectory(
            List<string> destination,
            string path)
        {
            if (!Directory.Exists(
                    path))
            {
                return;
            }

            destination.Add(
                path);

            try
            {
                foreach (string child
                         in Directory.EnumerateDirectories(
                             path))
                {
                    destination.Add(
                        child);
                }
            }
            catch
            {
            }
        }

        private static IEnumerable<string> ReadSteamLibraries(
            string steamRoot)
        {
            string vdf =
                Path.Combine(
                    steamRoot,
                    "steamapps",
                    "libraryfolders.vdf");

            if (!File.Exists(
                    vdf))
            {
                yield break;
            }

            string text;

            try
            {
                text =
                    File.ReadAllText(
                        vdf);
            }
            catch
            {
                yield break;
            }

            foreach (Match match
                     in Regex.Matches(
                         text,
                         "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"",
                         RegexOptions.IgnoreCase))
            {
                string value =
                    match.Groups[
                            "path"]
                        .Value
                        .Replace(
                            "\\\\",
                            "\\");

                if (Directory.Exists(
                        value))
                {
                    yield return value;
                }
            }
        }

        private static string? FindGameDirectoryNear(
            string start,
            int maxDepth)
        {
            if (!Directory.Exists(
                    start))
            {
                return null;
            }

            string direct =
                Path.Combine(
                    start,
                    "root.era");

            if (File.Exists(
                    direct))
            {
                return start;
            }

            if (maxDepth <=
                0)
            {
                return null;
            }

            try
            {
                foreach (string child
                         in Directory.EnumerateDirectories(
                             start))
                {
                    string name =
                        Path.GetFileName(
                            child);

                    bool likely =
                        name.Contains(
                            "Halo",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        File.Exists(
                            Path.Combine(
                                child,
                                "xgameFinal.exe"))
                        ||
                        File.Exists(
                            Path.Combine(
                                child,
                                "root.era"));

                    if (!likely &&
                        maxDepth <=
                            1)
                    {
                        continue;
                    }

                    string? found =
                        FindGameDirectoryNear(
                            child,
                            maxDepth -
                            1);

                    if (found !=
                        null)
                    {
                        return found;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? FindFileNear(
            string directory,
            string fileName,
            int maxDepth)
        {
            string direct =
                Path.Combine(
                    directory,
                    fileName);

            if (File.Exists(
                    direct))
            {
                return direct;
            }

            if (maxDepth <=
                0)
            {
                return null;
            }

            try
            {
                foreach (string child
                         in Directory.EnumerateDirectories(
                             directory))
                {
                    string? found =
                        FindFileNear(
                            child,
                            fileName,
                            maxDepth -
                            1);

                    if (found !=
                        null)
                    {
                        return found;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static IEnumerable<string> EnumerateNearbyEraFiles(
            string directory,
            int maxDepth)
        {
            if (!Directory.Exists(
                    directory))
            {
                yield break;
            }

            IEnumerable<string> files;

            try
            {
                files =
                    Directory.EnumerateFiles(
                            directory,
                            "*.era",
                            SearchOption.TopDirectoryOnly)
                        .ToArray();
            }
            catch
            {
                yield break;
            }

            foreach (string file
                     in files)
            {
                yield return file;
            }

            if (maxDepth <=
                0)
            {
                yield break;
            }

            string[] children;

            try
            {
                children =
                    Directory.EnumerateDirectories(
                            directory)
                        .ToArray();
            }
            catch
            {
                yield break;
            }

            foreach (string child
                     in children)
            {
                foreach (string file
                         in EnumerateNearbyEraFiles(
                             child,
                             maxDepth -
                             1))
                {
                    yield return file;
                }
            }
        }
    }
}
