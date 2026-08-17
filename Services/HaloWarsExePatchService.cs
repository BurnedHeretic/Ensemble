using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Ensemble.Services
{
    public static class HaloWarsExePatchService
    {
        // Based on the public KSoft.Phoenix / PhxGUI
        // pattern-matching patch for Halo Wars DE.
        //
        // -1 means wildcard.
        private static readonly short[] UnpatchedPattern =
        {
            0xE8, -1, -1, 0xFF, 0xFF,
            0x90,
            -1, 0xFB,
            0x41, 0x3B, 0x5E, 0x20,
            0x0F, 0x83,
            -1, 0x00, 0x00, 0x00
        };

        private static readonly short[] PatchedPattern =
        {
            0xE8, -1, -1, 0xFF, 0xFF,
            0x90,
            -1, 0xFB,
            0x41, 0x3B, 0x5E, 0x20,
            0xE9,
            -1, -1, -1, -1,
            0x00
        };

        private const int NextJumpOffset =
            14;

        private const int ModJumpOffset =
            12;

        public static HaloWarsExePatchResult Patch(
            string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException(
                    "EXE path is empty.",
                    nameof(exePath));
            }

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException(
                    "Halo Wars executable was not found.",
                    exePath);
            }

            FileAttributes attributes =
                File.GetAttributes(exePath);

            if ((attributes &
                 FileAttributes.ReadOnly) != 0)
            {
                throw new InvalidOperationException(
                    "The Halo Wars executable is read-only.");
            }

            byte[] data =
                File.ReadAllBytes(exePath);

            string sha1Before =
                ComputeSha1(data);

            List<int> unpatchedMatches =
                FindPattern(
                    data,
                    UnpatchedPattern);

            // -----------------------------------------------------
            // Already patched?
            // -----------------------------------------------------

            if (unpatchedMatches.Count == 0)
            {
                List<int> patchedMatches =
                    FindPattern(
                        data,
                        PatchedPattern);

                if (patchedMatches.Count == 1)
                {
                    return new HaloWarsExePatchResult
                    {
                        Success =
                            true,

                        AlreadyPatched =
                            true,

                        PatchOffset =
                            patchedMatches[0] +
                            ModJumpOffset,

                        Sha1Before =
                            sha1Before,

                        Sha1After =
                            sha1Before
                    };
                }

                throw new InvalidDataException(
                    "Ensemble could not locate the Halo Wars " +
                    "ERA signature-check pattern in this executable.\n\n" +
                    $"SHA1: {sha1Before}\n\n" +
                    "The executable may be unsupported, already modified " +
                    "in another way, or from a different game build.");
            }

            if (unpatchedMatches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Ensemble found {unpatchedMatches.Count} possible " +
                    "ERA signature-check locations.\n\n" +
                    "For safety, the executable will not be modified.");
            }

            int patternOffset =
                unpatchedMatches[0];

            // -----------------------------------------------------
            // Locate the later successful execution path.
            // -----------------------------------------------------

            int nextJumpIndex =
                checked(
                    patternOffset +
                    NextJumpOffset);

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
            if (data[goodJumpBase] !=
                0x75)
            {
                throw new InvalidDataException(
                    "Ensemble found the expected Halo Wars code " +
                    "pattern but could not locate its successful branch.");
            }

            int shortJumpRelative =
                unchecked(
                    (sbyte)data[
                        goodJumpBase + 1]);

            int goodCodeIndex =
                checked(
                    goodJumpBase +
                    2 +
                    shortJumpRelative);

            // -----------------------------------------------------
            // Replace:
            //
            // 0F 83 xx xx xx xx
            //
            // JNB rel32
            //
            // with:
            //
            // E9 xx xx xx xx
            //
            // JMP rel32
            //
            // -----------------------------------------------------

            int patchOffset =
                checked(
                    patternOffset +
                    ModJumpOffset);

            int addressAfterNewJump =
                checked(
                    patchOffset +
                    5);

            int newRelativeJump =
                checked(
                    goodCodeIndex -
                    addressAfterNewJump);

            string backupPath =
                CreateBackup(
                    exePath);

            data[patchOffset] =
                0xE9;

            BinaryPrimitives
                .WriteInt32LittleEndian(
                    data.AsSpan(
                        patchOffset + 1,
                        4),
                    newRelativeJump);

            // Verify our modified bytes before touching the EXE.
            List<int> resultingPatterns =
                FindPattern(
                    data,
                    PatchedPattern);

            if (resultingPatterns.Count != 1)
            {
                throw new InvalidDataException(
                    "Internal patch verification failed. " +
                    "The original executable has been preserved.");
            }

            string tempPath =
                exePath +
                ".ensemble.tmp";

            try
            {
                File.WriteAllBytes(
                    tempPath,
                    data);

                File.Copy(
                    tempPath,
                    exePath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            string sha1After =
                ComputeSha1(
                    data);

            return new HaloWarsExePatchResult
            {
                Success =
                    true,

                AlreadyPatched =
                    false,

                BackupPath =
                    backupPath,

                PatchOffset =
                    patchOffset,

                Sha1Before =
                    sha1Before,

                Sha1After =
                    sha1After
            };
        }

        private static List<int> FindPattern(
            byte[] data,
            short[] pattern)
        {
            List<int> matches =
                new();

            int maximum =
                data.Length -
                pattern.Length;

            for (int i = 0;
                 i <= maximum;
                 i++)
            {
                // First fixed byte is E8.
                // Cheap early rejection for a ~20 MB executable.
                if (data[i] !=
                    pattern[0])
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

                    if (expected >= 0 &&
                        data[i + p] !=
                        (byte)expected)
                    {
                        match =
                            false;

                        break;
                    }
                }

                if (match)
                {
                    matches.Add(i);
                }
            }

            return matches;
        }

        private static string CreateBackup(
            string exePath)
        {
            string directory =
                Path.GetDirectoryName(exePath)
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

            if (!File.Exists(
                    backupPath))
            {
                File.Copy(
                    exePath,
                    backupPath,
                    overwrite: false);

                return backupPath;
            }

            // Never overwrite an existing known-good backup.
            string timestamp =
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss");

            backupPath =
                Path.Combine(
                    directory,
                    fileNameWithoutExtension +
                    "_UNTOUCHED_" +
                    timestamp +
                    extension);

            File.Copy(
                exePath,
                backupPath,
                overwrite: false);

            return backupPath;
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
            if (offset < 0 ||
                size < 0 ||
                offset >
                    data.Length - size)
            {
                throw new InvalidDataException(
                    "Executable patch location is outside the file.");
            }
        }
    }

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

        public string BackupPath
        {
            get;
            init;
        } = string.Empty;

        public int PatchOffset
        {
            get;
            init;
        }

        public string Sha1Before
        {
            get;
            init;
        } = string.Empty;

        public string Sha1After
        {
            get;
            init;
        } = string.Empty;
    }
}