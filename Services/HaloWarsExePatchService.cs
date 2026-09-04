using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Ensemble.Services
{
    public static class HaloWarsExePatchService
    {
        // =========================================================
        // SUPPORTED HALO WARS DE BUILD
        //
        // Stock xgameFinal.exe:
        //   Size: 0x1637200
        //   SHA1: DADC7A0DE6A7B5EFC76C343D909E3686773E8642
        //
        // Current Ensemble modular result:
        //   Size: 0x163BC00
        //   SHA1: 5DA29710BA569D82C5B8B6FF97DFA1DD95FFFE68
        //
        // The modular result is deterministic. Ensemble also accepts
        // the previous two-stage patch (ERA bypass / loose files) and
        // normalizes it back to the supported stock image before
        // installing the current modular patch.
        // =========================================================

        private const int StockFileSize =
            0x1637200;

        private const int ModularFileSize =
            0x163BC00;

        private const int ModularPayloadSize =
            ModularFileSize -
            StockFileSize;

        private const string StockSha1 =
            "DADC7A0DE6A7B5EFC76C343D909E3686773E8642";

        private const string ModularSha1 =
            "5DA29710BA569D82C5B8B6FF97DFA1DD95FFFE68";

        private const string PayloadSha1 =
            "3245DF6C5DFF8C55F8754AB794BFB8C6D9174335";


        // =========================================================
        // PE HEADER PATCH
        // =========================================================

        private const int AddressOfEntryPointOffset =
            0x188;

        private const uint StockEntryPointRva =
            0x00CC25F4;

        private const uint ModularEntryPointRva =
            0x01C33000;


        private const int SizeOfImageOffset =
            0x1B0;

        private const uint StockSizeOfImage =
            0x01C2F000;

        private const uint ModularSizeOfImage =
            0x01C34000;


        // .reloc section header.
        private const int RelocVirtualSizeOffset =
            0x3D8;

        private const uint StockRelocVirtualSize =
            0x0002C724;

        private const uint ModularRelocVirtualSize =
            0x00031141;


        private const int RelocRawSizeOffset =
            0x3E0;

        private const uint StockRelocRawSize =
            0x0002C800;

        private const uint ModularRelocRawSize =
            0x00031200;


        private const int RelocCharacteristicsOffset =
            0x3F4;

        private const uint StockRelocCharacteristics =
            0x42000040;

        private const uint ModularRelocCharacteristics =
            0xE0000060;


        // =========================================================
        // ORIGINAL ENSEMBLE PATCH STAGES
        // =========================================================

        private const int EraSignaturePatchOffset =
            0x683F90;

        private static readonly byte[]
            EraSignatureOriginal =
        {
            0x0F, 0x83, 0xC9, 0x00, 0x00, 0x00
        };

        private static readonly byte[]
            EraSignaturePatched =
        {
            0xE9, 0x10, 0x01, 0x00, 0x00, 0x00
        };


        private const int LooseFilesPatchOffset =
            0x82032B;

        private const byte LooseFilesOriginal =
            0x01;

        private const byte LooseFilesPatched =
            0x00;


        // =========================================================
        // MODULAR ERA / ENSMAP1 HOOKS
        // =========================================================

        private const int ScenarioListLoadPatchOffset =
            0x5C750;

        private static readonly byte[]
            ScenarioListLoadOriginal =
        {
            0x48, 0x8B, 0xC4, 0x55, 0x41, 0x54
        };

        private static readonly byte[]
            ScenarioListLoadPatched =
        {
            0xE9, 0xBD, 0x5C, 0xBD, 0x01, 0x90
        };


        private const int ScenarioDescriptionsPatchOffset =
            0x5C7B5;

        private static readonly byte[]
            ScenarioDescriptionsOriginal =
        {
            0x4C, 0x8D, 0x05, 0xD4, 0x2C, 0x12, 0x01
        };

        private static readonly byte[]
            ScenarioDescriptionsPatched =
        {
            0xE8, 0xAB, 0x5C, 0xBD, 0x01, 0x90, 0x90
        };


        private const int LocalizationSetupPatchOffset =
            0x1ED180;

        private static readonly byte[]
            LocalizationSetupOriginal =
        {
            0x88, 0x54, 0x24, 0x10,
            0x48, 0x89, 0x4C, 0x24, 0x08
        };

        private static readonly byte[]
            LocalizationSetupPatched =
        {
            0xE9, 0xF9, 0x52, 0xA4, 0x01,
            0x90, 0x90, 0x90, 0x90
        };


        private const int StringTableSelectionPatchOffset =
            0x1ED2CA;

        private static readonly byte[]
            StringTableSelectionOriginal =
        {
            0x8B, 0x15, 0xDC, 0x50, 0x2C, 0x01
        };

        private static readonly byte[]
            StringTableSelectionPatched =
        {
            0xE8, 0x11, 0x52, 0xA4, 0x01, 0x90
        };


        // =========================================================
        // EMBEDDED MODULAR PAYLOAD
        //
        // Resources\EnsembleModularPatchPayload.bin
        //
        // This is the exact 0x4A00-byte extension extracted from the
        // tested working modular xgameFinal.exe. It is embedded into
        // Ensemble.exe by the project file and copied into the newly
        // extended .reloc section.
        // =========================================================

        private static readonly Lazy<byte[]>
            ModularPayload =
                new Lazy<byte[]>(
                    LoadModularPayload);


        // =========================================================
        // PUBLIC PATCH ENTRY
        // =========================================================

        public static HaloWarsExePatchResult Patch(
            string exePath)
        {
            if (string.IsNullOrWhiteSpace(
                    exePath))
            {
                throw new ArgumentException(
                    "EXE path is empty.",
                    nameof(exePath));
            }


            if (!File.Exists(
                    exePath))
            {
                throw new FileNotFoundException(
                    "Halo Wars executable was not found.",
                    exePath);
            }


            FileAttributes attributes =
                File.GetAttributes(
                    exePath);


            if ((attributes &
                 FileAttributes.ReadOnly) !=
                0)
            {
                throw new InvalidOperationException(
                    "The Halo Wars executable is read-only.");
            }


            byte[] input =
                File.ReadAllBytes(
                    exePath);


            string sha1Before =
                ComputeSha1(
                    input);


            // =====================================================
            // Already on the exact current modular build.
            // =====================================================

            if (input.Length ==
                ModularFileSize)
            {
                VerifyCompletePatch(
                    input);


                return BuildResult(
                    wasModified: false,
                    backupPath: string.Empty,
                    sha1Before: sha1Before,
                    sha1After: sha1Before,
                    eraPatchChanged: false,
                    loosePatchChanged: false,
                    modularPatchChanged: false);
            }


            // =====================================================
            // Stock or previous two-stage Ensemble patch.
            //
            // Normalize only the two historical byte patches and
            // require the result to hash EXACTLY as the supported
            // retail executable. This prevents us from injecting
            // absolute-address hooks into an unknown game build.
            // =====================================================

            PatchState inputState =
                InspectLegacyPatchState(
                    input);


            byte[] normalizedStock =
                NormalizeSupportedBase(
                    input,
                    inputState);


            // =====================================================
            // Build the deterministic current modular executable.
            // =====================================================

            byte[] output =
                BuildModularImage(
                    normalizedStock);


            VerifyCompletePatch(
                output);


            string sha1After =
                ComputeSha1(
                    output);


            // =====================================================
            // Preserve a true stock backup.
            //
            // Even when the input was the old two-stage patch,
            // normalizedStock is byte-for-byte retail stock.
            // =====================================================

            string backupPath =
                CreateUntouchedBackup(
                    exePath,
                    normalizedStock);


            string tempPath =
                exePath +
                ".ensemble.tmp";


            try
            {
                File.WriteAllBytes(
                    tempPath,
                    output);


                byte[] verification =
                    File.ReadAllBytes(
                        tempPath);


                VerifyCompletePatch(
                    verification);


                File.Copy(
                    tempPath,
                    exePath,
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


            return BuildResult(
                wasModified: true,
                backupPath: backupPath,
                sha1Before: sha1Before,
                sha1After: sha1After,
                eraPatchChanged:
                    !inputState.EraSignatureBypassEnabled,
                loosePatchChanged:
                    !inputState.LooseFilesEnabled,
                modularPatchChanged: true);
        }


        // =========================================================
        // BUILD MODULAR IMAGE
        // =========================================================

        private static byte[] BuildModularImage(
            byte[] stock)
        {
            if (stock.Length !=
                StockFileSize)
            {
                throw new InvalidDataException(
                    "The normalized Halo Wars executable has " +
                    "an unexpected file size.");
            }


            if (!string.Equals(
                    ComputeSha1(stock),
                    StockSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The normalized Halo Wars executable does not " +
                    "match the supported retail build.");
            }


            byte[] data =
                new byte[
                    ModularFileSize];


            Buffer.BlockCopy(
                stock,
                0,
                data,
                0,
                stock.Length);


            byte[] payload =
                ModularPayload.Value;


            if (payload.Length !=
                ModularPayloadSize)
            {
                throw new InvalidDataException(
                    "The embedded Ensemble modular payload has " +
                    "an unexpected size.");
            }


            Buffer.BlockCopy(
                payload,
                0,
                data,
                StockFileSize,
                payload.Length);


            // -----------------------------------------------------
            // PE image / final-section extension.
            // -----------------------------------------------------

            WriteUInt32(
                data,
                AddressOfEntryPointOffset,
                ModularEntryPointRva);


            WriteUInt32(
                data,
                SizeOfImageOffset,
                ModularSizeOfImage);


            WriteUInt32(
                data,
                RelocVirtualSizeOffset,
                ModularRelocVirtualSize);


            WriteUInt32(
                data,
                RelocRawSizeOffset,
                ModularRelocRawSize);


            WriteUInt32(
                data,
                RelocCharacteristicsOffset,
                ModularRelocCharacteristics);


            // -----------------------------------------------------
            // Existing support patches.
            // -----------------------------------------------------

            WriteBytes(
                data,
                EraSignaturePatchOffset,
                EraSignaturePatched);


            data[
                LooseFilesPatchOffset] =
                    LooseFilesPatched;


            // -----------------------------------------------------
            // Modular ENSMAP1 discovery / registration /
            // localization hooks.
            // -----------------------------------------------------

            WriteBytes(
                data,
                ScenarioListLoadPatchOffset,
                ScenarioListLoadPatched);


            WriteBytes(
                data,
                ScenarioDescriptionsPatchOffset,
                ScenarioDescriptionsPatched);


            WriteBytes(
                data,
                LocalizationSetupPatchOffset,
                LocalizationSetupPatched);


            WriteBytes(
                data,
                StringTableSelectionPatchOffset,
                StringTableSelectionPatched);


            return data;
        }


        // =========================================================
        // LEGACY INPUT NORMALIZATION
        // =========================================================

        private static PatchState InspectLegacyPatchState(
            byte[] data)
        {
            if (data.Length !=
                StockFileSize)
            {
                throw new InvalidDataException(
                    "This xgameFinal.exe is not a supported Halo Wars " +
                    "Definitive Edition build.\n\n" +

                    $"Expected stock/legacy size: 0x{StockFileSize:X}\n" +
                    $"Actual size: 0x{data.Length:X}\n\n" +

                    "If Halo Wars has been updated, Ensemble's modular " +
                    "patch offsets must be revalidated before patching.");
            }


            bool eraOriginal =
                MatchBytes(
                    data,
                    EraSignaturePatchOffset,
                    EraSignatureOriginal);


            bool eraPatched =
                MatchBytes(
                    data,
                    EraSignaturePatchOffset,
                    EraSignaturePatched);


            if (!eraOriginal &&
                !eraPatched)
            {
                throw new InvalidDataException(
                    "The ERA signature patch location does not match " +
                    "either the retail or previous Ensemble state.");
            }


            byte looseValue =
                data[
                    LooseFilesPatchOffset];


            if (looseValue !=
                    LooseFilesOriginal &&
                looseValue !=
                    LooseFilesPatched)
            {
                throw new InvalidDataException(
                    "The loose-file patch location does not match " +
                    "either the retail or previous Ensemble state.");
            }


            // The newer modular hook sites must still be retail.
            RequireBytes(
                data,
                ScenarioListLoadPatchOffset,
                ScenarioListLoadOriginal,
                "ScenarioList::load hook");


            RequireBytes(
                data,
                ScenarioDescriptionsPatchOffset,
                ScenarioDescriptionsOriginal,
                "ScenarioDescriptions selection hook");


            RequireBytes(
                data,
                LocalizationSetupPatchOffset,
                LocalizationSetupOriginal,
                "localization setup hook");


            RequireBytes(
                data,
                StringTableSelectionPatchOffset,
                StringTableSelectionOriginal,
                "StringTable selection hook");


            return new PatchState
            {
                EraSignatureBypassEnabled =
                    eraPatched,

                LooseFilesEnabled =
                    looseValue ==
                    LooseFilesPatched
            };
        }


        private static byte[] NormalizeSupportedBase(
            byte[] input,
            PatchState state)
        {
            byte[] normalized =
                input.ToArray();


            if (state.EraSignatureBypassEnabled)
            {
                WriteBytes(
                    normalized,
                    EraSignaturePatchOffset,
                    EraSignatureOriginal);
            }


            if (state.LooseFilesEnabled)
            {
                normalized[
                    LooseFilesPatchOffset] =
                        LooseFilesOriginal;
            }


            string normalizedSha1 =
                ComputeSha1(
                    normalized);


            if (!string.Equals(
                    normalizedSha1,
                    StockSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "This xgameFinal.exe does not normalize to the " +
                    "supported retail Halo Wars DE executable.\n\n" +

                    $"Expected retail SHA1:\n{StockSha1}\n\n" +

                    $"Normalized SHA1:\n{normalizedSha1}\n\n" +

                    "Ensemble will not install absolute-address modular " +
                    "hooks into an unknown or additionally modified EXE.");
            }


            // Explicit PE-header sanity checks.
            RequireUInt32(
                normalized,
                AddressOfEntryPointOffset,
                StockEntryPointRva,
                "stock AddressOfEntryPoint");


            RequireUInt32(
                normalized,
                SizeOfImageOffset,
                StockSizeOfImage,
                "stock SizeOfImage");


            RequireUInt32(
                normalized,
                RelocVirtualSizeOffset,
                StockRelocVirtualSize,
                "stock .reloc VirtualSize");


            RequireUInt32(
                normalized,
                RelocRawSizeOffset,
                StockRelocRawSize,
                "stock .reloc SizeOfRawData");


            RequireUInt32(
                normalized,
                RelocCharacteristicsOffset,
                StockRelocCharacteristics,
                "stock .reloc Characteristics");


            return normalized;
        }


        // =========================================================
        // COMPLETE CURRENT PATCH VERIFICATION
        // =========================================================

        private static void VerifyCompletePatch(
            byte[] data)
        {
            if (data.Length !=
                ModularFileSize)
            {
                throw new InvalidDataException(
                    "Ensemble modular patch verification failed: " +
                    "unexpected xgameFinal.exe size.");
            }


            RequireUInt32(
                data,
                AddressOfEntryPointOffset,
                ModularEntryPointRva,
                "modular AddressOfEntryPoint");


            RequireUInt32(
                data,
                SizeOfImageOffset,
                ModularSizeOfImage,
                "modular SizeOfImage");


            RequireUInt32(
                data,
                RelocVirtualSizeOffset,
                ModularRelocVirtualSize,
                "modular .reloc VirtualSize");


            RequireUInt32(
                data,
                RelocRawSizeOffset,
                ModularRelocRawSize,
                "modular .reloc SizeOfRawData");


            RequireUInt32(
                data,
                RelocCharacteristicsOffset,
                ModularRelocCharacteristics,
                "modular .reloc Characteristics");


            RequireBytes(
                data,
                EraSignaturePatchOffset,
                EraSignaturePatched,
                "ERA signature bypass");


            if (data[
                    LooseFilesPatchOffset] !=
                LooseFilesPatched)
            {
                throw new InvalidDataException(
                    "Ensemble modular patch verification failed: " +
                    "loose-file support is missing.");
            }


            RequireBytes(
                data,
                ScenarioListLoadPatchOffset,
                ScenarioListLoadPatched,
                "ScenarioList::load modular hook");


            RequireBytes(
                data,
                ScenarioDescriptionsPatchOffset,
                ScenarioDescriptionsPatched,
                "ScenarioDescriptions modular hook");


            RequireBytes(
                data,
                LocalizationSetupPatchOffset,
                LocalizationSetupPatched,
                "localization setup modular hook");


            RequireBytes(
                data,
                StringTableSelectionPatchOffset,
                StringTableSelectionPatched,
                "StringTable selection modular hook");


            byte[] payload =
                ModularPayload.Value;


            if (!data
                    .AsSpan(
                        StockFileSize,
                        ModularPayloadSize)
                    .SequenceEqual(
                        payload))
            {
                throw new InvalidDataException(
                    "Ensemble modular patch verification failed: " +
                    "embedded modular payload does not match.");
            }


            string sha1 =
                ComputeSha1(
                    data);


            if (!string.Equals(
                    sha1,
                    ModularSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Ensemble modular patch verification failed: " +
                    "final executable hash is not the tested modular build.\n\n" +

                    $"Expected:\n{ModularSha1}\n\n" +

                    $"Actual:\n{sha1}");
            }
        }


        // =========================================================
        // EMBEDDED PAYLOAD
        // =========================================================

        private static byte[] LoadModularPayload()
        {
            Assembly assembly =
                typeof(HaloWarsExePatchService)
                    .Assembly;


            string? resourceName =
                assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(
                        name =>
                            name.EndsWith(
                                ".EnsembleModularPatchPayload.bin",
                                StringComparison.Ordinal));


            if (resourceName ==
                null)
            {
                throw new InvalidDataException(
                    "Ensemble's embedded modular EXE payload " +
                    "could not be found.");
            }


            using Stream stream =
                assembly.GetManifestResourceStream(
                    resourceName)
                ?? throw new InvalidDataException(
                    "Unable to open Ensemble's embedded modular EXE payload.");


            if (stream.Length !=
                ModularPayloadSize)
            {
                throw new InvalidDataException(
                    "Ensemble's embedded modular EXE payload has " +
                    "the wrong size.");
            }


            byte[] payload =
                new byte[
                    ModularPayloadSize];


            int total =
                0;


            while (total <
                   payload.Length)
            {
                int read =
                    stream.Read(
                        payload,
                        total,
                        payload.Length -
                        total);


                if (read <=
                    0)
                {
                    throw new EndOfStreamException(
                        "Unexpected end of embedded modular payload.");
                }


                total +=
                    read;
            }


            string payloadSha1 =
                ComputeSha1(
                    payload);


            if (!string.Equals(
                    payloadSha1,
                    PayloadSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Ensemble's embedded modular EXE payload failed " +
                    "its integrity check.");
            }


            return payload;
        }


        // =========================================================
        // TRUE STOCK BACKUP
        // =========================================================

        private static string CreateUntouchedBackup(
            string exePath,
            byte[] normalizedStock)
        {
            string directory =
                Path.GetDirectoryName(
                    exePath)
                ?? string.Empty;


            string fileNameWithoutExtension =
                Path.GetFileNameWithoutExtension(
                    exePath);


            string extension =
                Path.GetExtension(
                    exePath);


            string backupPath =
                Path.Combine(
                    directory,

                    fileNameWithoutExtension +
                    "_UNTOUCHED" +
                    extension);


            if (File.Exists(
                    backupPath))
            {
                return backupPath;
            }


            if (!string.Equals(
                    ComputeSha1(normalizedStock),
                    StockSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Ensemble refused to create an untouched backup " +
                    "because the normalized image is not stock.");
            }


            File.WriteAllBytes(
                backupPath,
                normalizedStock);


            return backupPath;
        }


        // =========================================================
        // RESULT
        // =========================================================

        private static HaloWarsExePatchResult BuildResult(
            bool wasModified,
            string backupPath,
            string sha1Before,
            string sha1After,
            bool eraPatchChanged,
            bool loosePatchChanged,
            bool modularPatchChanged)
        {
            return new HaloWarsExePatchResult
            {
                Success =
                    true,

                AlreadyPatched =
                    !wasModified,

                WasModified =
                    wasModified,

                BackupPath =
                    backupPath,

                // Compatibility with existing MainWindow.
                PatchOffset =
                    EraSignaturePatchOffset,

                EraSignaturePatchOffset =
                    EraSignaturePatchOffset,

                LooseFilesPatchOffset =
                    LooseFilesPatchOffset,

                EraSignatureBypassEnabled =
                    true,

                LooseFilesEnabled =
                    true,

                EraSignaturePatchChanged =
                    eraPatchChanged,

                LooseFilesPatchChanged =
                    loosePatchChanged,

                ModularMapSupportEnabled =
                    true,

                ModularPatchChanged =
                    modularPatchChanged,

                ModularEntryPointRva =
                    ModularEntryPointRva,

                ModularPayloadFileOffset =
                    StockFileSize,

                Sha1Before =
                    sha1Before,

                Sha1After =
                    sha1After
            };
        }


        // =========================================================
        // BINARY HELPERS
        // =========================================================

        private static bool MatchBytes(
            byte[] data,
            int offset,
            byte[] expected)
        {
            if (offset <
                    0 ||
                offset >
                    data.Length -
                    expected.Length)
            {
                return false;
            }


            return data
                .AsSpan(
                    offset,
                    expected.Length)
                .SequenceEqual(
                    expected);
        }


        private static void RequireBytes(
            byte[] data,
            int offset,
            byte[] expected,
            string description)
        {
            if (!MatchBytes(
                    data,
                    offset,
                    expected))
            {
                throw new InvalidDataException(
                    $"Halo Wars EXE validation failed at {description}.\n\n" +
                    $"File offset: 0x{offset:X}");
            }
        }


        private static void WriteBytes(
            byte[] data,
            int offset,
            byte[] value)
        {
            EnsureRange(
                data,
                offset,
                value.Length);


            value.CopyTo(
                data,
                offset);
        }


        private static void WriteUInt32(
            byte[] data,
            int offset,
            uint value)
        {
            EnsureRange(
                data,
                offset,
                4);


            BinaryPrimitives
                .WriteUInt32LittleEndian(
                    data.AsSpan(
                        offset,
                        4),
                    value);
        }


        private static void RequireUInt32(
            byte[] data,
            int offset,
            uint expected,
            string description)
        {
            EnsureRange(
                data,
                offset,
                4);


            uint actual =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            offset,
                            4));


            if (actual !=
                expected)
            {
                throw new InvalidDataException(
                    $"Halo Wars EXE validation failed at {description}.\n\n" +
                    $"Expected: 0x{expected:X8}\n" +
                    $"Actual:   0x{actual:X8}");
            }
        }


        private static string ComputeSha1(
            byte[] data)
        {
            byte[] hash =
                SHA1.HashData(
                    data);


            return Convert.ToHexString(
                hash);
        }


        private static void EnsureRange(
            byte[] data,
            int offset,
            int size)
        {
            if (offset <
                    0 ||
                size <
                    0 ||
                offset >
                    data.Length -
                    size)
            {
                throw new InvalidDataException(
                    "Executable patch location is outside the file.");
            }
        }


        private sealed class PatchState
        {
            public bool EraSignatureBypassEnabled
            {
                get;
                init;
            }


            public bool LooseFilesEnabled
            {
                get;
                init;
            }
        }
    }


    // =============================================================
    // PUBLIC RESULT
    // =============================================================

    public sealed class HaloWarsExePatchResult
    {
        public bool Success
        {
            get;
            init;
        }


        public bool AlreadyPatched
        {
            get;
            init;
        }


        public bool WasModified
        {
            get;
            init;
        }


        public string BackupPath
        {
            get;
            init;
        } =
            string.Empty;


        // Compatibility with existing MainWindow.
        public int PatchOffset
        {
            get;
            init;
        }


        public int EraSignaturePatchOffset
        {
            get;
            init;
        }


        public int LooseFilesPatchOffset
        {
            get;
            init;
        }


        public bool EraSignatureBypassEnabled
        {
            get;
            init;
        }


        public bool LooseFilesEnabled
        {
            get;
            init;
        }


        public bool EraSignaturePatchChanged
        {
            get;
            init;
        }


        public bool LooseFilesPatchChanged
        {
            get;
            init;
        }


        // Current modular ENSMAP1 support.
        public bool ModularMapSupportEnabled
        {
            get;
            init;
        }


        public bool ModularPatchChanged
        {
            get;
            init;
        }


        public uint ModularEntryPointRva
        {
            get;
            init;
        }


        public int ModularPayloadFileOffset
        {
            get;
            init;
        }


        public string Sha1Before
        {
            get;
            init;
        } =
            string.Empty;


        public string Sha1After
        {
            get;
            init;
        } =
            string.Empty;
    }
}
