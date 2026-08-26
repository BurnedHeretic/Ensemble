using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Ensemble.Services
{
    public static class HaloWarsExePatchService
    {
        // =========================================================
        // ENSEMBLE MODDING PATCH
        //
        // Each patch stage is independent and idempotent.
        //
        // Running Ensemble on:
        //
        // Stock EXE
        // Old Ensemble-patched EXE
        // Current Ensemble-patched EXE
        //
        // should simply apply whichever stages are missing.
        // =========================================================


        // =========================================================
        // PATCH 1
        // ERA SIGNATURE BYPASS
        //
        // Based on the public KSoft.Phoenix / PhxGUI patch.
        // =========================================================

        private static readonly short[]
            EraUnpatchedPattern =
        {
            0xE8, -1, -1, 0xFF, 0xFF,
            0x90,
            -1, 0xFB,
            0x41, 0x3B, 0x5E, 0x20,
            0x0F, 0x83,
            -1, 0x00, 0x00, 0x00
        };


        private static readonly short[]
            EraPatchedPattern =
        {
            0xE8, -1, -1, 0xFF, 0xFF,
            0x90,
            -1, 0xFB,
            0x41, 0x3B, 0x5E, 0x20,
            0xE9,
            -1, -1, -1, -1,
            0x00
        };


        private const int
            EraNextJumpOffset =
                14;


        private const int
            EraModJumpOffset =
                12;


        // =========================================================
        // PATCH 2
        // ENABLE LOOSE FILES
        //
        // Halo Wars final-build filesystem setup contains:
        //
        // C6 07 01
        // C6 06 01
        // 41 C6 06 01
        // 80 3F 00
        // 0F 85 ?? ?? ?? ??
        //
        // The second store is:
        //
        // disableLooseFilesOut = true
        //
        // We change only its immediate value:
        //
        // true → false
        //
        // Archives remain enabled.
        // =========================================================

        private static readonly short[]
            LooseFilesUnpatchedPattern =
        {
            0xC6, 0x07, 0x01,

            0xC6, 0x06, 0x01,

            0x41, 0xC6, 0x06, 0x01,

            0x80, 0x3F, 0x00,

            0x0F, 0x85,
            -1, -1, -1, -1
        };


        private static readonly short[]
            LooseFilesPatchedPattern =
        {
            0xC6, 0x07, 0x01,

            0xC6, 0x06, 0x00,

            0x41, 0xC6, 0x06, 0x01,

            0x80, 0x3F, 0x00,

            0x0F, 0x85,
            -1, -1, -1, -1
        };


        // Index of the immediate TRUE/FALSE value
        // inside LooseFilesUnpatchedPattern.
        private const int
            LooseFilesValueOffset =
                5;


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


            byte[] data =
                File.ReadAllBytes(
                    exePath);


            string sha1Before =
                ComputeSha1(
                    data);


            // =====================================================
            // Apply all supported patch stages IN MEMORY first.
            // =====================================================

            PatchStageResult
                eraResult =
                    ApplyEraSignaturePatch(
                        data);


            PatchStageResult
                looseResult =
                    ApplyLooseFilesPatch(
                        data);


            bool wasModified =
                eraResult.WasModified ||
                looseResult.WasModified;


            string backupPath =
                string.Empty;


            // =====================================================
            // Only touch disk if at least one stage changed.
            // =====================================================

            if (wasModified)
            {
                backupPath =
                    CreateBackup(
                        exePath);


                string tempPath =
                    exePath +
                    ".ensemble.tmp";


                try
                {
                    File.WriteAllBytes(
                        tempPath,
                        data);


                    // Re-read temporary output before replacing EXE.
                    byte[] verificationData =
                        File.ReadAllBytes(
                            tempPath);


                    VerifyCompletePatch(
                        verificationData);


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
            }
            else
            {
                // Even when no modifications were necessary,
                // prove the existing executable has every stage.
                VerifyCompletePatch(
                    data);
            }


            string sha1After =
                ComputeSha1(
                    data);


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

                PatchOffset =
                    eraResult.PatchOffset,

                EraSignaturePatchOffset =
                    eraResult.PatchOffset,

                LooseFilesPatchOffset =
                    looseResult.PatchOffset,

                EraSignatureBypassEnabled =
                    true,

                LooseFilesEnabled =
                    true,

                EraSignaturePatchChanged =
                    eraResult.WasModified,

                LooseFilesPatchChanged =
                    looseResult.WasModified,

                Sha1Before =
                    sha1Before,

                Sha1After =
                    sha1After
            };
        }


        // =========================================================
        // ERA SIGNATURE PATCH
        // =========================================================

        private static PatchStageResult
            ApplyEraSignaturePatch(
                byte[] data)
        {
            List<int> unpatched =
                FindPattern(
                    data,
                    EraUnpatchedPattern);


            List<int> patched =
                FindPattern(
                    data,
                    EraPatchedPattern);


            // Already done.
            if (patched.Count ==
                    1 &&
                unpatched.Count ==
                    0)
            {
                return new PatchStageResult
                {
                    WasModified =
                        false,

                    PatchOffset =
                        patched[0] +
                        EraModJumpOffset
                };
            }


            if (unpatched.Count !=
                    1 ||
                patched.Count !=
                    0)
            {
                throw new InvalidDataException(
                    "Ensemble could not uniquely identify the " +
                    "Halo Wars ERA signature-check patch point.\n\n" +

                    $"Unpatched matches: {unpatched.Count}\n" +
                    $"Patched matches:   {patched.Count}");
            }


            int patternOffset =
                unpatched[0];


            // -----------------------------------------------------
            // Locate later successful execution path.
            // -----------------------------------------------------

            int nextJumpIndex =
                checked(
                    patternOffset +
                    EraNextJumpOffset);


            EnsureRange(
                data,
                nextJumpIndex,
                4);


            int nextJumpRelative =
                BinaryPrimitives
                    .ReadInt32LittleEndian(
                        data.AsSpan(
                            nextJumpIndex,
                            4));


            int goodJumpBase =
                checked(
                    nextJumpIndex +
                    4 +
                    nextJumpRelative);


            EnsureRange(
                data,
                goodJumpBase,
                2);


            // PhxGUI expects:
            //
            // 75 xx
            //
            // JNZ short
            if (data[
                    goodJumpBase] !=
                0x75)
            {
                throw new InvalidDataException(
                    "Ensemble found the expected ERA patch " +
                    "pattern but could not locate its " +
                    "successful execution branch.");
            }


            int shortJumpRelative =
                unchecked(
                    (sbyte)data[
                        goodJumpBase +
                        1]);


            int goodCodeIndex =
                checked(
                    goodJumpBase +
                    2 +
                    shortJumpRelative);


            int patchOffset =
                checked(
                    patternOffset +
                    EraModJumpOffset);


            int addressAfterNewJump =
                checked(
                    patchOffset +
                    5);


            int newRelativeJump =
                checked(
                    goodCodeIndex -
                    addressAfterNewJump);


            // Replace:
            //
            // 0F 83 xx xx xx xx
            //
            // with:
            //
            // E9 xx xx xx xx
            //
            // Last original byte is intentionally left alone.

            data[
                patchOffset] =
                    0xE9;


            BinaryPrimitives
                .WriteInt32LittleEndian(
                    data.AsSpan(
                        patchOffset +
                        1,
                        4),
                    newRelativeJump);


            List<int> verification =
                FindPattern(
                    data,
                    EraPatchedPattern);


            if (verification.Count !=
                1)
            {
                throw new InvalidDataException(
                    "ERA signature bypass internal " +
                    "verification failed.");
            }


            return new PatchStageResult
            {
                WasModified =
                    true,

                PatchOffset =
                    patchOffset
            };
        }


        // =========================================================
        // LOOSE FILE PATCH
        // =========================================================

        private static PatchStageResult
            ApplyLooseFilesPatch(
                byte[] data)
        {
            List<int> unpatched =
                FindPattern(
                    data,
                    LooseFilesUnpatchedPattern);


            List<int> patched =
                FindPattern(
                    data,
                    LooseFilesPatchedPattern);


            // Already done.
            if (patched.Count ==
                    1 &&
                unpatched.Count ==
                    0)
            {
                return new PatchStageResult
                {
                    WasModified =
                        false,

                    PatchOffset =
                        patched[0] +
                        LooseFilesValueOffset
                };
            }


            if (unpatched.Count !=
                    1 ||
                patched.Count !=
                    0)
            {
                throw new InvalidDataException(
                    "Ensemble could not uniquely identify the " +
                    "Halo Wars loose-file patch point.\n\n" +

                    $"Unpatched matches: {unpatched.Count}\n" +
                    $"Patched matches:   {patched.Count}");
            }


            int patchOffset =
                checked(
                    unpatched[0] +
                    LooseFilesValueOffset);


            EnsureRange(
                data,
                patchOffset,
                1);


            if (data[
                    patchOffset] !=
                0x01)
            {
                throw new InvalidDataException(
                    "Loose-file patch immediate value " +
                    "was not the expected TRUE byte.");
            }


            // disableLooseFilesOut:
            //
            // TRUE → FALSE
            data[
                patchOffset] =
                    0x00;


            List<int> verification =
                FindPattern(
                    data,
                    LooseFilesPatchedPattern);


            if (verification.Count !=
                1)
            {
                throw new InvalidDataException(
                    "Loose-file patch internal " +
                    "verification failed.");
            }


            return new PatchStageResult
            {
                WasModified =
                    true,

                PatchOffset =
                    patchOffset
            };
        }


        // =========================================================
        // COMPLETE PATCH VERIFICATION
        // =========================================================

        private static void VerifyCompletePatch(
            byte[] data)
        {
            List<int> era =
                FindPattern(
                    data,
                    EraPatchedPattern);


            if (era.Count !=
                1)
            {
                throw new InvalidDataException(
                    "Ensemble verification failed: " +
                    "ERA signature bypass is missing.");
            }


            List<int> loose =
                FindPattern(
                    data,
                    LooseFilesPatchedPattern);


            if (loose.Count !=
                1)
            {
                throw new InvalidDataException(
                    "Ensemble verification failed: " +
                    "loose-file support is missing.");
            }
        }


        // =========================================================
        // PATTERN SCANNER
        // =========================================================

        private static List<int> FindPattern(
            byte[] data,
            short[] pattern)
        {
            List<int> matches =
                new();


            if (pattern.Length ==
                0)
            {
                return matches;
            }


            int maximum =
                data.Length -
                pattern.Length;


            for (int i = 0;
                 i <= maximum;
                 i++)
            {
                // Cheap early rejection when first byte
                // isn't a wildcard.
                if (pattern[0] >=
                        0 &&
                    data[i] !=
                        (byte)pattern[0])
                {
                    continue;
                }


                bool match =
                    true;


                for (int p = 0;
                     p < pattern.Length;
                     p++)
                {
                    short expected =
                        pattern[p];


                    if (expected >=
                            0 &&
                        data[
                            i + p] !=
                            (byte)expected)
                    {
                        match =
                            false;

                        break;
                    }
                }


                if (match)
                {
                    matches.Add(
                        i);
                }
            }


            return matches;
        }


        // =========================================================
        // BACKUP
        // =========================================================

        private static string CreateBackup(
            string exePath)
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


            // -----------------------------------------------------
            // Preserve the first known-good stock backup forever.
            // -----------------------------------------------------

            if (!File.Exists(
                    backupPath))
            {
                File.Copy(
                    exePath,
                    backupPath,
                    overwrite: false);


                return backupPath;
            }


            // -----------------------------------------------------
            // If an untouched backup already exists, do NOT create
            // endless timestamped backups simply because an older
            // Ensemble patch is being upgraded.
            //
            // The original untouched EXE is already safe.
            // -----------------------------------------------------

            return backupPath;
        }


        // =========================================================
        // HASH
        // =========================================================

        private static string ComputeSha1(
            byte[] data)
        {
            byte[] hash =
                SHA1.HashData(
                    data);


            return Convert.ToHexString(
                hash);
        }


        // =========================================================
        // RANGE CHECK
        // =========================================================

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


        // =========================================================
        // INTERNAL PATCH RESULT
        // =========================================================

        private sealed class PatchStageResult
        {
            public bool WasModified
            {
                get;
                init;
            }


            public int PatchOffset
            {
                get;
                init;
            } =
                -1;
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


        // True only when every supported Ensemble patch
        // was already installed before this run.
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


        // Kept for compatibility with the existing MainWindow.
        // Represents the ERA-signature patch location.
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