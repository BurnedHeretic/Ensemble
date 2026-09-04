using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Ensemble.Services
{
    /// <summary>
    /// Embeds the metadata consumed by Ensemble's modular xgameFinal.exe.
    ///
    /// The manifest occupies the final 2048 bytes of the normal Halo Wars
    /// trailing guard/padding region. It is intentionally plaintext and is
    /// not an ECF chunk; Halo Wars never indexes this region. The archive
    /// size and all normal ECF offsets/checksums remain unchanged.
    /// </summary>
    public static class EraManifestFooterService
    {
        public const int FooterSize = 2048;
        private const uint Version = 1;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ENSMAP1\0");

        private const int ScenarioOffset = 16;
        private const int ScenarioSize = 512;
        private const int DisplayNameOffset = 528;
        private const int DisplayNameSize = 256;
        private const int DescriptionOffset = 784;
        private const int DescriptionSize = 512;
        private const int LoadingScreenOffset = 1296;
        private const int LoadingScreenSize = 256;
        private const int MapNameOffset = 1552;
        private const int MapNameSize = 128;

        public sealed class Manifest
        {
            public required string ScenarioFile { get; init; }
            public required string DisplayName { get; init; }
            public string Description { get; init; } = string.Empty;
            public int MaxPlayers { get; init; } = 2;
            public string LoadingScreen { get; init; } = string.Empty;
            public string MapName { get; init; } = string.Empty;
        }

        public static byte[] Attach(byte[] encryptedEra, Manifest manifest)
        {
            ArgumentNullException.ThrowIfNull(encryptedEra);
            ArgumentNullException.ThrowIfNull(manifest);

            if (encryptedEra.Length < 4096 || (encryptedEra.Length & 4095) != 0)
            {
                throw new InvalidDataException(
                    "The ERA must use Halo Wars' normal 4096-byte-aligned guard layout.");
            }

            if (manifest.MaxPlayers is not (2 or 4 or 6))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(manifest),
                    "MaxPlayers must be 2, 4, or 6.");
            }

            byte[] result = (byte[])encryptedEra.Clone();
            Span<byte> footer = result.AsSpan(result.Length - FooterSize, FooterSize);
            footer.Clear();
            Magic.CopyTo(footer);
            BinaryPrimitives.WriteUInt32LittleEndian(footer.Slice(8, 4), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(footer.Slice(12, 4), checked((uint)manifest.MaxPlayers));

            WriteUtf8(footer, ScenarioOffset, ScenarioSize,
                NormalizeScenarioFile(manifest.ScenarioFile), nameof(manifest.ScenarioFile));
            WriteUtf8(footer, DisplayNameOffset, DisplayNameSize,
                manifest.DisplayName, nameof(manifest.DisplayName));
            WriteUtf8(footer, DescriptionOffset, DescriptionSize,
                manifest.Description, nameof(manifest.Description));
            WriteUtf8(footer, LoadingScreenOffset, LoadingScreenSize,
                manifest.LoadingScreen, nameof(manifest.LoadingScreen));
            WriteUtf8(footer, MapNameOffset, MapNameSize,
                manifest.MapName, nameof(manifest.MapName));

            return result;
        }

        public static Manifest? TryRead(
            string eraPath)
        {
            if (string.IsNullOrWhiteSpace(
                    eraPath) ||
                !File.Exists(
                    eraPath))
            {
                return null;
            }


            byte[] data =
                File.ReadAllBytes(
                    eraPath);


            return TryRead(
                data);
        }


        public static Manifest? TryRead(
            byte[] eraData)
        {
            ArgumentNullException.ThrowIfNull(
                eraData);


            if (eraData.Length <
                FooterSize)
            {
                return null;
            }


            ReadOnlySpan<byte> footer =
                eraData.AsSpan(
                    eraData.Length -
                    FooterSize,
                    FooterSize);


            if (!footer
                    .Slice(
                        0,
                        Magic.Length)
                    .SequenceEqual(
                        Magic))
            {
                return null;
            }


            uint version =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        footer.Slice(
                            8,
                            4));


            if (version !=
                Version)
            {
                throw new InvalidDataException(
                    $"Unsupported Ensemble ERA manifest version: {version}");
            }


            uint maxPlayers =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        footer.Slice(
                            12,
                            4));


            if (maxPlayers is not
                (2 or 4 or 6))
            {
                throw new InvalidDataException(
                    $"Invalid Ensemble manifest MaxPlayers value: {maxPlayers}");
            }


            string scenarioFile =
                ReadUtf8(
                    footer,
                    ScenarioOffset,
                    ScenarioSize);


            string displayName =
                ReadUtf8(
                    footer,
                    DisplayNameOffset,
                    DisplayNameSize);


            if (string.IsNullOrWhiteSpace(
                    scenarioFile) ||
                string.IsNullOrWhiteSpace(
                    displayName))
            {
                throw new InvalidDataException(
                    "The Ensemble ERA manifest is incomplete.");
            }


            return new Manifest
            {
                ScenarioFile =
                    scenarioFile,

                DisplayName =
                    displayName,

                Description =
                    ReadUtf8(
                        footer,
                        DescriptionOffset,
                        DescriptionSize),

                MaxPlayers =
                    checked(
                        (int)maxPlayers),

                LoadingScreen =
                    ReadUtf8(
                        footer,
                        LoadingScreenOffset,
                        LoadingScreenSize),

                MapName =
                    ReadUtf8(
                        footer,
                        MapNameOffset,
                        MapNameSize)
            };
        }


        private static string ReadUtf8(
            ReadOnlySpan<byte> footer,
            int offset,
            int capacity)
        {
            ReadOnlySpan<byte> field =
                footer.Slice(
                    offset,
                    capacity);


            int terminator =
                field.IndexOf(
                    (byte)0);


            if (terminator >=
                0)
            {
                field =
                    field[
                        ..terminator];
            }


            return Encoding.UTF8.GetString(
                field);
        }

        private static string NormalizeScenarioFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("ScenarioFile cannot be empty.", nameof(value));

            string result = value.Replace('/', '\\').TrimStart('\\');
            if (result.StartsWith("scenario\\", StringComparison.OrdinalIgnoreCase))
                result = result["scenario\\".Length..];
            if (result.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
                result = result[..^4];
            if (!result.EndsWith(".scn", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ScenarioFile must resolve to a .scn path.");

            return result;
        }

        private static void WriteUtf8(
            Span<byte> footer,
            int offset,
            int capacity,
            string? value,
            string fieldName)
        {
            value ??= string.Empty;
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount >= capacity)
            {
                throw new InvalidDataException(
                    $"{fieldName} is too long for the modular ERA manifest " +
                    $"({byteCount} UTF-8 bytes; maximum {capacity - 1}).");
            }

            Encoding.UTF8.GetBytes(value, footer.Slice(offset, capacity - 1));
        }
    }
}
