using Ensemble.Models;
using System.IO;
using System.Text.Json;

namespace Ensemble.Services
{
    /// <summary>
    /// Local custom-mesh library.
    ///
    /// Raw artist meshes are staged here so they can appear in Ensemble's
    /// Object Browser without requiring Windows installation or modifying a
    /// Halo Wars ERA immediately.  Halo Wars UGX conversion / archive
    /// dependency injection is a separate pipeline and is intentionally not
    /// faked here.
    /// </summary>
    internal static class CustomMeshImportService
    {
        private static readonly string[]
            SupportedExtensions =
            {
                ".obj",
                ".fbx",
                ".gltf",
                ".glb",
                ".dae",
                ".3ds",
                ".gr2",
                ".ugx"
            };

        public static string LibraryRoot =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Ensemble",
                "ImportedMeshes");

        public static ImportedMeshEntry Import(
            string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(
                    sourcePath)
                ||
                !File.Exists(
                    sourcePath))
            {
                throw new FileNotFoundException(
                    "The selected mesh file could not be found.",
                    sourcePath);
            }

            string extension =
                Path.GetExtension(
                    sourcePath)
                .ToLowerInvariant();

            if (!SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Unsupported mesh format.\n\n" +
                    "Supported formats:\n" +
                    string.Join(
                        ", ",
                        SupportedExtensions));
            }

            Directory.CreateDirectory(
                LibraryRoot);

            string id =
                Guid.NewGuid()
                    .ToString(
                        "N");

            string itemDirectory =
                Path.Combine(
                    LibraryRoot,
                    id);

            Directory.CreateDirectory(
                itemDirectory);

            string fileName =
                Path.GetFileName(
                    sourcePath);

            string destination =
                Path.Combine(
                    itemDirectory,
                    fileName);

            File.Copy(
                sourcePath,
                destination,
                overwrite:
                    true);

            // OBJ material sidecar: copy the MTL next to it if present.
            if (extension.Equals(
                    ".obj",
                    StringComparison.OrdinalIgnoreCase))
            {
                TryCopyObjSidecar(
                    sourcePath,
                    itemDirectory);
            }

            ImportedMeshEntry entry =
                new ImportedMeshEntry
                {
                    Id =
                        id,

                    DisplayName =
                        Path.GetFileNameWithoutExtension(
                            fileName),

                    OriginalSourcePath =
                        sourcePath,

                    LibraryFilePath =
                        destination,

                    FileName =
                        fileName,

                    Extension =
                        extension,

                    ImportedUtc =
                        DateTime.UtcNow
                };

            string manifestPath =
                Path.Combine(
                    itemDirectory,
                    "mesh.json");

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    entry,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }));

            return entry;
        }

        public static List<ImportedMeshEntry> LoadAll()
        {
            List<ImportedMeshEntry> result =
                new();

            if (!Directory.Exists(
                    LibraryRoot))
            {
                return result;
            }

            foreach (string directory
                     in Directory.EnumerateDirectories(
                         LibraryRoot))
            {
                string manifestPath =
                    Path.Combine(
                        directory,
                        "mesh.json");

                if (!File.Exists(
                        manifestPath))
                {
                    continue;
                }

                try
                {
                    ImportedMeshEntry? entry =
                        JsonSerializer.Deserialize<
                            ImportedMeshEntry>(
                                File.ReadAllText(
                                    manifestPath));

                    if (entry is null)
                    {
                        continue;
                    }

                    if (!File.Exists(
                            entry.LibraryFilePath))
                    {
                        continue;
                    }

                    result.Add(
                        entry);
                }
                catch
                {
                    // Ignore malformed third-party/local library entries.
                }
            }

            return result
                .OrderBy(
                    entry =>
                        entry.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryCopyObjSidecar(
            string objPath,
            string itemDirectory)
        {
            try
            {
                string? mtllibLine =
                    File.ReadLines(
                        objPath)
                        .Select(
                            line =>
                                line.Trim())
                        .FirstOrDefault(
                            line =>
                                line.StartsWith(
                                    "mtllib ",
                                    StringComparison.OrdinalIgnoreCase));

                string? mtlName =
                    mtllibLine?
                        ["mtllib ".Length..]
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        mtlName))
                {
                    return;
                }

                string sourceDirectory =
                    Path.GetDirectoryName(
                        objPath)
                    ??
                    string.Empty;

                string mtlSource =
                    Path.Combine(
                        sourceDirectory,
                        mtlName);

                if (!File.Exists(
                        mtlSource))
                {
                    return;
                }

                string mtlDestination =
                    Path.Combine(
                        itemDirectory,
                        Path.GetFileName(
                            mtlSource));

                File.Copy(
                    mtlSource,
                    mtlDestination,
                    overwrite:
                        true);
            }
            catch
            {
                // Sidecars are helpful, not required for staging.
            }
        }
    }
}
