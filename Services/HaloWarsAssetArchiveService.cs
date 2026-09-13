using Ensemble.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace Ensemble.Services
{
    /// <summary>
    /// Provides the 3D viewport and object browser with Halo Wars' shared/global
    /// ERA archives. Supports Steam, Xbox app installs and the legacy Microsoft
    /// Store/UWP package used by Halo Wars: Definitive Edition.
    /// </summary>
    internal static class HaloWarsAssetArchiveService
    {
        private const string StorePackageFamily =
            "Microsoft.BulldogThreshold_8wekyb3d8bbwe";

        private const string StorePackagePrefix =
            "Microsoft.BulldogThreshold_";

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

        /// <summary>
        /// Halo Wars' sandbox/mod folder for the Microsoft Store version.
        /// ModManifest.txt and Store-compatible mod folders live here.
        /// </summary>
        public static string WindowsStoreLocalStateDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                StorePackageFamily,
                "LocalState");

        /// <summary>
        /// Steam's normal ModManifest location. Exposed alongside the Store
        /// LocalState path so future install/export workflows do not need to
        /// hard-code either distribution.
        /// </summary>
        public static string SteamLocalStateDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Halo Wars");

        public static string DistributionName
        {
            get
            {
                string? directory =
                    GameDirectory;

                return ClassifyDistribution(
                    directory);
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
                    4);

            if (gameDirectory ==
                null)
            {
                // root.era itself is authoritative. This also covers the
                // Microsoft Store package where xgameFinal.exe may be hidden,
                // protected or stored differently from Steam.
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

            string distribution =
                ClassifyDistribution(
                    gameDirectory);

            message =
                archiveCount >
                    0
                    ? $"Halo Wars assets configured ({distribution}) from {gameDirectory}. " +
                      $"Loaded {archiveCount} shared ERA archive(s)."
                    : $"The {distribution} directory was saved, but no supported shared ERA archives could be opened.";

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
                ? "Halo Wars assets were not auto-detected. Ensemble checked Steam, Xbox app/XboxGames and Microsoft Store locations. Use Tools > Locate Halo Wars Game Assets if needed."
                : $"Halo Wars assets ({ClassifyDistribution(directory)}): {directory} | {count} shared ERA archive(s).";
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
                            4);

                    TryOpenArchive(
                        path,
                        opened,
                        archives);
                }

                // Include plausible shared archives but deliberately avoid
                // loading every scenario ERA into the viewport resolver.
                foreach (string eraPath
                         in EnumerateNearbyEraFiles(
                             directory,
                             3))
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
                // A protected/damaged/unsupported optional archive should not
                // stop the editor from opening the map the user selected.
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

                if (!Directory.Exists(
                        directory))
                {
                    return null;
                }

                // Do not keep a stale saved path if the game moved between
                // Steam, Xbox app or Store package updates.
                return FindGameDirectoryNear(
                    directory,
                    2);
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
            // Fast path: if the currently-open ERA is from a real game install,
            // walk around it first. This works for Steam, XboxGames and
            // WindowsApps without caring which storefront owns the files.
            string? currentDirectory =
                Path.GetDirectoryName(
                    currentArchive.FilePath);

            if (!string.IsNullOrWhiteSpace(
                    currentDirectory))
            {
                string? nearCurrent =
                    FindGameDirectoryNear(
                        currentDirectory,
                        4);

                if (nearCurrent !=
                    null)
                {
                    return nearCurrent;
                }
            }

            List<string> startingPoints =
                new();

            AddDirectoryWithParents(
                startingPoints,
                currentDirectory);

            AddDirectoryWithParents(
                startingPoints,
                AppContext.BaseDirectory);

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            // ---------------------------------------------------------
            // STEAM
            // ---------------------------------------------------------
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

            // ---------------------------------------------------------
            // XBOX APP / MICROSOFT STORE
            // ---------------------------------------------------------
            foreach (string xboxRoot
                     in EnumerateXboxGameRoots())
            {
                string? found =
                    FindGameDirectoryNear(
                        xboxRoot,
                        4);

                if (found !=
                    null)
                {
                    return found;
                }
            }

            foreach (string storeRoot
                     in EnumerateLegacyStorePackageRoots(
                         programFiles))
            {
                string? found =
                    FindGameDirectoryNear(
                        storeRoot,
                        5);

                if (found !=
                    null)
                {
                    return found;
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
                        2);

                if (found !=
                    null)
                {
                    return found;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateXboxGameRoots()
        {
            HashSet<string> yielded =
                new(
                    StringComparer.OrdinalIgnoreCase);

            // Current Xbox app/GDK installs normally live under X:\XboxGames.
            // Also include ModifiableWindowsApps, used by older Xbox app builds.
            foreach (DriveInfo drive
                     in GetReadyDrives())
            {
                string xboxGames =
                    Path.Combine(
                        drive.RootDirectory.FullName,
                        "XboxGames");

                if (Directory.Exists(
                        xboxGames) &&
                    yielded.Add(
                        xboxGames))
                {
                    yield return xboxGames;
                }

                string modifiable =
                    Path.Combine(
                        drive.RootDirectory.FullName,
                        "Program Files",
                        "ModifiableWindowsApps");

                if (Directory.Exists(
                        modifiable) &&
                    yielded.Add(
                        modifiable))
                {
                    yield return modifiable;
                }
            }
        }

        private static IEnumerable<string> EnumerateLegacyStorePackageRoots(
            string programFiles)
        {
            string windowsApps =
                Path.Combine(
                    programFiles,
                    "WindowsApps");

            if (!Directory.Exists(
                    windowsApps))
            {
                yield break;
            }

            string[] packageDirectories;

            try
            {
                packageDirectories =
                    Directory.EnumerateDirectories(
                            windowsApps,
                            StorePackagePrefix + "*",
                            SearchOption.TopDirectoryOnly)
                        .ToArray();
            }
            catch
            {
                // WindowsApps can be ACL-protected. Manual root.era selection
                // still works if the user has access, so detection failure is
                // not fatal and Ensemble never changes folder permissions.
                yield break;
            }

            foreach (string directory
                     in packageDirectories)
            {
                yield return directory;
            }
        }

        private static IEnumerable<DriveInfo> GetReadyDrives()
        {
            DriveInfo[] drives;

            try
            {
                drives =
                    DriveInfo.GetDrives();
            }
            catch
            {
                yield break;
            }

            foreach (DriveInfo drive
                     in drives)
            {
                bool ready;

                try
                {
                    ready =
                        drive.IsReady;
                }
                catch
                {
                    ready =
                        false;
                }

                if (ready)
                {
                    yield return drive;
                }
            }
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
                         4;
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

            string[] children;

            try
            {
                children =
                    Directory.EnumerateDirectories(
                            start)
                        .ToArray();
            }
            catch
            {
                return null;
            }

            // First pass prioritises paths that actually look like Halo Wars,
            // Xbox app content folders or the BulldogThreshold Store package.
            foreach (string child
                     in children
                         .OrderByDescending(
                             IsLikelyHaloWarsDirectory))
            {
                if (!IsLikelyHaloWarsDirectory(
                        child) &&
                    maxDepth <=
                        2)
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

            return null;
        }

        private static bool IsLikelyHaloWarsDirectory(
            string path)
        {
            string name =
                Path.GetFileName(
                    path);

            if (name.Contains(
                    "Halo",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Wars",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "BulldogThreshold",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "Content",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return File.Exists(
                           Path.Combine(
                               path,
                               "root.era"))
                       ||
                       File.Exists(
                           Path.Combine(
                               path,
                               "xgameFinal.exe"));
            }
            catch
            {
                return false;
            }
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
                return null;
            }

            foreach (string child
                     in children)
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

            string[] files;

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

        private static string ClassifyDistribution(
            string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(
                    gameDirectory))
            {
                return "Unknown PC installation";
            }

            string normalized =
                gameDirectory.Replace(
                    '/',
                    '\\');

            if (normalized.Contains(
                    "\\WindowsApps\\" +
                    StorePackagePrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    StorePackagePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft Store / Xbox app";
            }

            if (normalized.Contains(
                    "\\XboxGames\\",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "\\ModifiableWindowsApps\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Xbox app";
            }

            if (normalized.Contains(
                    "\\steamapps\\",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "\\Steam\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Steam";
            }

            return "Halo Wars PC";
        }
    }
}
