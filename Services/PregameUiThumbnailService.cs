using Ensemble.Models;
using System.IO;

namespace Ensemble.Services
{
    /// <summary>
    /// Installs a custom map's embedded DDX thumbnail into pregameUI.era.
    ///
    /// Why this exists:
    /// Halo Wars mounts pregameUI.era while the skirmish setup UI is active.
    /// A scenario ERA is not mounted until scenario prefetch begins, which is
    /// too late for the lobby map preview.
    ///
    /// The custom map ERA remains the canonical/distributable source of the
    /// thumbnail; Ensemble copies that resource into pregameUI.era at install
    /// time so the menu can resolve ENSMAP1.MapName.
    /// </summary>
    internal static class PregameUiThumbnailService
    {
        private const string ImgPrefix =
            "img://";


        public sealed class InstallResult
        {
            public bool HadEmbeddedThumbnail
            {
                get;
                init;
            }


            public bool WasModified
            {
                get;
                init;
            }


            public bool WasAdded
            {
                get;
                init;
            }


            public bool WasReplaced
            {
                get;
                init;
            }


            public string ThumbnailArchivePath
            {
                get;
                init;
            } =
                string.Empty;


            public string PregameUiEraPath
            {
                get;
                init;
            } =
                string.Empty;


            public string BackupPath
            {
                get;
                init;
            } =
                string.Empty;
        }


        public static InstallResult InstallFromMapEra(
            string mapEraPath,
            string pregameUiEraPath,
            EraManifestFooterService.Manifest manifest)
        {
            if (string.IsNullOrWhiteSpace(
                    mapEraPath))
            {
                throw new ArgumentException(
                    "Map ERA path is empty.",
                    nameof(mapEraPath));
            }


            if (string.IsNullOrWhiteSpace(
                    pregameUiEraPath))
            {
                throw new ArgumentException(
                    "pregameUI.era path is empty.",
                    nameof(pregameUiEraPath));
            }


            ArgumentNullException.ThrowIfNull(
                manifest);


            if (!File.Exists(
                    mapEraPath))
            {
                throw new FileNotFoundException(
                    "Custom map ERA was not found.",
                    mapEraPath);
            }


            if (!File.Exists(
                    pregameUiEraPath))
            {
                throw new FileNotFoundException(
                    "pregameUI.era was not found.",
                    pregameUiEraPath);
            }


            string thumbnailArchivePath =
                NormalizeMapNameToArchivePath(
                    manifest.MapName);


            // Empty MapName means no preview.
            if (string.IsNullOrWhiteSpace(
                    thumbnailArchivePath))
            {
                return new InstallResult
                {
                    HadEmbeddedThumbnail =
                        false,

                    WasModified =
                        false,

                    ThumbnailArchivePath =
                        string.Empty,

                    PregameUiEraPath =
                        pregameUiEraPath
                };
            }


            // =====================================================
            // FIND THE THUMBNAIL INSIDE THE CUSTOM MAP ERA
            // =====================================================
            //
            // Stock-inherited MapName values deliberately point at
            // pregameUI.era and will not exist inside the custom
            // scenario ERA. In that case nothing needs installing.
            // =====================================================

            EraArchiveInfo mapArchive =
                EraArchiveService.Open(
                    mapEraPath);


            EraChunkInfo? mapThumbnailChunk =
                mapArchive
                    .Chunks
                    .FirstOrDefault(
                        chunk =>
                            string.Equals(
                                NormalizeArchivePath(
                                    chunk.FileName),
                                thumbnailArchivePath,
                                StringComparison.OrdinalIgnoreCase));


            if (mapThumbnailChunk ==
                null)
            {
                return new InstallResult
                {
                    HadEmbeddedThumbnail =
                        false,

                    WasModified =
                        false,

                    ThumbnailArchivePath =
                        thumbnailArchivePath,

                    PregameUiEraPath =
                        pregameUiEraPath
                };
            }


            byte[] thumbnailData =
                EraExtractionService.ExtractChunk(
                    mapArchive,
                    mapThumbnailChunk);


            DdxTextureService
                .ValidateMapThumbnail(
                    thumbnailData);


            // =====================================================
            // OPEN PREGAME UI ARCHIVE
            // =====================================================

            EraArchiveInfo pregameArchive =
                EraArchiveService.Open(
                    pregameUiEraPath);


            EraChunkInfo? existingThumbnailChunk =
                pregameArchive
                    .Chunks
                    .FirstOrDefault(
                        chunk =>
                            string.Equals(
                                NormalizeArchivePath(
                                    chunk.FileName),
                                thumbnailArchivePath,
                                StringComparison.OrdinalIgnoreCase));


            // If an identical thumbnail is already installed, don't
            // rebuild pregameUI.era unnecessarily.
            if (existingThumbnailChunk !=
                null)
            {
                byte[] existingData =
                    EraExtractionService.ExtractChunk(
                        pregameArchive,
                        existingThumbnailChunk);


                if (existingData
                        .AsSpan()
                        .SequenceEqual(
                            thumbnailData))
                {
                    return new InstallResult
                    {
                        HadEmbeddedThumbnail =
                            true,

                        WasModified =
                            false,

                        WasAdded =
                            false,

                        WasReplaced =
                            false,

                        ThumbnailArchivePath =
                            thumbnailArchivePath,

                        PregameUiEraPath =
                            pregameUiEraPath,

                        BackupPath =
                            GetBackupPath(
                                pregameUiEraPath)
                    };
                }
            }


            // =====================================================
            // BUILD MODIFIED PREGAMEUI.ERA
            // =====================================================

            Dictionary<int, byte[]> replacements =
                new();


            List<EraFileAddition> additions =
                new();


            bool wasAdded =
                false;


            bool wasReplaced =
                false;


            if (existingThumbnailChunk !=
                null)
            {
                replacements[
                    existingThumbnailChunk.Index] =
                        thumbnailData;


                wasReplaced =
                    true;
            }
            else
            {
                additions.Add(
                    new EraFileAddition
                    {
                        FileName =
                            thumbnailArchivePath,

                        Data =
                            thumbnailData,

                        // Stored DDX chunks are valid in shipping
                        // pregameUI.era and avoid needless recompression.
                        CompressionMethod =
                            0,

                        AlignmentLog2 =
                            2,

                        ResourceFlags =
                            0
                    });


                wasAdded =
                    true;
            }


            byte[] modifiedPregameEra =
                EraRebuildService.BuildModifiedEra(
                    pregameArchive,
                    replacements,
                    new Dictionary<int, string>(),
                    additions);


            // =====================================================
            // WRITE + REOPEN + VERIFY BEFORE COMMIT
            // =====================================================

            string tempPath =
                pregameUiEraPath +
                ".ensemble.tmp";


            string backupPath =
                GetBackupPath(
                    pregameUiEraPath);


            try
            {
                File.WriteAllBytes(
                    tempPath,
                    modifiedPregameEra);


                EraArchiveInfo verificationArchive =
                    EraArchiveService.Open(
                        tempPath);


                EraChunkInfo? verificationChunk =
                    verificationArchive
                        .Chunks
                        .FirstOrDefault(
                            chunk =>
                                string.Equals(
                                    NormalizeArchivePath(
                                        chunk.FileName),
                                    thumbnailArchivePath,
                                    StringComparison.OrdinalIgnoreCase));


                if (verificationChunk ==
                    null)
                {
                    throw new InvalidDataException(
                        "pregameUI.era rebuild lost the custom map " +
                        "thumbnail.\n\n" +
                        thumbnailArchivePath);
                }


                byte[] verificationData =
                    EraExtractionService.ExtractChunk(
                        verificationArchive,
                        verificationChunk);


                if (!verificationData
                        .AsSpan()
                        .SequenceEqual(
                            thumbnailData))
                {
                    throw new InvalidDataException(
                        "The custom map thumbnail failed " +
                        "pregameUI.era round-trip verification.");
                }


                DdxTextureService
                    .ValidateMapThumbnail(
                        verificationData);


                // Preserve the first pre-Ensemble pregameUI archive.
                // Use .bak rather than .era so neither Halo Wars nor
                // Ensemble's modular ERA scanner can mistake it for
                // an installable archive.
                if (!File.Exists(
                        backupPath))
                {
                    File.Copy(
                        pregameUiEraPath,
                        backupPath,
                        overwrite: false);
                }


                File.Copy(
                    tempPath,
                    pregameUiEraPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(
                        tempPath))
                {
                    File.Delete(
                        tempPath);
                }
            }


            // =====================================================
            // FINAL VERIFY FROM INSTALLED FILE
            // =====================================================

            EraArchiveInfo installedArchive =
                EraArchiveService.Open(
                    pregameUiEraPath);


            EraChunkInfo installedThumbnailChunk =
                installedArchive
                    .Chunks
                    .FirstOrDefault(
                        chunk =>
                            string.Equals(
                                NormalizeArchivePath(
                                    chunk.FileName),
                                thumbnailArchivePath,
                                StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(
                    "Installed pregameUI.era does not contain " +
                    "the custom map thumbnail.");


            byte[] installedThumbnailData =
                EraExtractionService.ExtractChunk(
                    installedArchive,
                    installedThumbnailChunk);


            if (!installedThumbnailData
                    .AsSpan()
                    .SequenceEqual(
                        thumbnailData))
            {
                throw new InvalidDataException(
                    "Installed pregameUI.era thumbnail verification failed.");
            }


            return new InstallResult
            {
                HadEmbeddedThumbnail =
                    true,

                WasModified =
                    true,

                WasAdded =
                    wasAdded,

                WasReplaced =
                    wasReplaced,

                ThumbnailArchivePath =
                    thumbnailArchivePath,

                PregameUiEraPath =
                    pregameUiEraPath,

                BackupPath =
                    backupPath
            };
        }


        private static string NormalizeMapNameToArchivePath(
            string? mapName)
        {
            if (string.IsNullOrWhiteSpace(
                    mapName))
            {
                return string.Empty;
            }


            string value =
                mapName.Trim();


            if (value.StartsWith(
                    ImgPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    value[
                        ImgPrefix.Length..];
            }


            return NormalizeArchivePath(
                value);
        }


        private static string NormalizeArchivePath(
            string value)
        {
            return
                value
                    .Replace(
                        '/',
                        '\\')
                    .TrimStart(
                        '\\');
        }


        private static string GetBackupPath(
            string pregameUiEraPath)
        {
            return
                pregameUiEraPath +
                ".ensemble_untouched.bak";
        }
    }
}
