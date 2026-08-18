using System.IO;

namespace Ensemble.Models
{
    public sealed class EraArchiveInfo
    {
        public string FilePath { get; init; } =
            string.Empty;

        public string FileName =>
            Path.GetFileName(FilePath);

        public long FileSize { get; init; }

        public bool IsEncrypted { get; init; }

        public bool IsValidEra { get; init; }

        public byte[] RawHeaderBytes { get; init; } =
            Array.Empty<byte>();

        public byte[] DecryptedHeaderBytes { get; init; } =
            Array.Empty<byte>();

        public uint Magic { get; init; }

        public uint HeaderSize { get; init; }

        public uint HeaderAdler32 { get; init; }

        public uint ArchiveFileSize { get; init; }

        public ushort NumChunks { get; init; }

        public ushort Flags { get; init; }

        public uint ArchiveId { get; init; }

        public ushort ChunkExtraDataSize { get; init; }

        public uint ArchiveHeaderMagic { get; init; }

        public uint SignatureSize { get; init; }

        public List<EraChunkInfo> Chunks { get; init; } =
            new();

        public string RawHeaderHex =>
            string.Join(
                " ",
                RawHeaderBytes.Select(
                    b => b.ToString("X2")));

        public string DecryptedHeaderHex =>
            string.Join(
                " ",
                DecryptedHeaderBytes.Select(
                    b => b.ToString("X2")));

        public string DecryptedHeaderAscii =>
            new string(
                DecryptedHeaderBytes
                    .Select(
                        b =>
                            b >= 32 && b <= 126
                                ? (char)b
                                : '.')
                    .ToArray());
    }
}