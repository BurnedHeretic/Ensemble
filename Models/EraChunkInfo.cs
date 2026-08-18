namespace Ensemble.Models
{
    public sealed class EraChunkInfo
    {
        public int Index { get; init; }

        public ulong Id { get; init; }

        public uint Offset { get; init; }

        public uint CompressedSize { get; init; }

        public uint Adler32 { get; init; }

        public byte Flags { get; init; }

        public byte AlignmentLog2 { get; init; }

        public ushort ResourceFlags { get; init; }

        public ulong Date { get; init; }

        public uint DecompressedSize { get; init; }

        public byte[] CompressedTiger128 { get; init; } =
            new byte[16];

        public uint NameOffset { get; init; }

        // Filled after Chunk 0 has been decompressed.
        public string FileName { get; set; } =
            string.Empty;

        public int CompressionMethod =>
            Flags & 0x07;

        public string CompressionName =>
            CompressionMethod switch
            {
                0 => "Stored",
                1 => "Deflate Raw",
                2 => "Deflate Stream",
                _ => $"Unknown ({CompressionMethod})"
            };

        public string Tiger128Hex =>
            Convert.ToHexString(
                CompressedTiger128);
    }
}