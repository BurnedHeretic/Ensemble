using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    internal static class EcfFileService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const int BaseHeaderSize =
            32;

        private const int BaseChunkHeaderSize =
            24;

        public static byte[] ReplaceChunk(
            byte[] originalEcf,
            ulong chunkId,
            byte[] replacementData)
        {
            if (originalEcf == null)
                throw new ArgumentNullException(
                    nameof(originalEcf));

            if (replacementData == null)
                throw new ArgumentNullException(
                    nameof(replacementData));

            ParsedEcf ecf =
                Parse(originalEcf);

            EcfChunk? target =
                null;

            foreach (EcfChunk chunk
                     in ecf.Chunks)
            {
                if (chunk.Id ==
                    chunkId)
                {
                    target =
                        chunk;

                    break;
                }
            }

            if (target == null)
            {
                throw new InvalidDataException(
                    $"ECF chunk 0x{chunkId:X16} was not found.");
            }

            target.Data =
                replacementData;

            return Build(
                ecf);
        }

        private static ParsedEcf Parse(
            byte[] data)
        {
            if (data.Length <
                BaseHeaderSize)
            {
                throw new InvalidDataException(
                    "ECF file is too small.");
            }

            uint magic =
                ReadUInt32(
                    data,
                    0);

            if (magic !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    $"Invalid ECF magic 0x{magic:X8}.");
            }

            uint headerSize =
                ReadUInt32(
                    data,
                    4);

            uint declaredSize =
                ReadUInt32(
                    data,
                    12);

            ushort numChunks =
                ReadUInt16(
                    data,
                    16);

            ushort flags =
                ReadUInt16(
                    data,
                    18);

            uint id =
                ReadUInt32(
                    data,
                    20);

            ushort chunkExtraSize =
                ReadUInt16(
                    data,
                    24);

            if (headerSize <
                    BaseHeaderSize ||
                headerSize >
                    data.Length)
            {
                throw new InvalidDataException(
                    "Invalid ECF header size.");
            }

            if (declaredSize != 0 &&
                declaredSize !=
                    data.Length)
            {
                throw new InvalidDataException(
                    $"ECF size mismatch. " +
                    $"Header: {declaredSize:N0}, " +
                    $"actual: {data.Length:N0}.");
            }

            int chunkHeaderSize =
                checked(
                    BaseChunkHeaderSize +
                    chunkExtraSize);

            long tableEnd =
                (long)headerSize +
                ((long)numChunks *
                 chunkHeaderSize);

            if (tableEnd >
                data.Length)
            {
                throw new InvalidDataException(
                    "ECF chunk table is truncated.");
            }

            ParsedEcf result =
                new ParsedEcf
                {
                    HeaderSize =
                        checked(
                            (int)headerSize),

                    Flags =
                        flags,

                    Id =
                        id,

                    ChunkExtraDataSize =
                        chunkExtraSize,

                    OriginalHeader =
                        data.AsSpan(
                            0,
                            checked(
                                (int)headerSize))
                        .ToArray()
                };

            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int p =
                    checked(
                        (int)headerSize +
                        i *
                        chunkHeaderSize);

                ulong currentId =
                    ReadUInt64(
                        data,
                        p);

                uint offset =
                    ReadUInt32(
                        data,
                        p + 8);

                uint size =
                    ReadUInt32(
                        data,
                        p + 12);

                byte chunkFlags =
                    data[p + 20];

                byte alignmentLog2 =
                    data[p + 21];

                ushort resourceFlags =
                    ReadUInt16(
                        data,
                        p + 22);

                if ((long)offset +
                    size >
                    data.Length)
                {
                    throw new InvalidDataException(
                        $"ECF chunk {i} points outside the file.");
                }

                byte[] extra =
                    chunkExtraSize == 0
                        ? Array.Empty<byte>()
                        : data.AsSpan(
                            p +
                            BaseChunkHeaderSize,
                            chunkExtraSize)
                            .ToArray();

                byte[] chunkData =
                    size == 0
                        ? Array.Empty<byte>()
                        : data.AsSpan(
                            checked(
                                (int)offset),
                            checked(
                                (int)size))
                            .ToArray();

                result.Chunks.Add(
                    new EcfChunk
                    {
                        Id =
                            currentId,

                        Flags =
                            chunkFlags,

                        AlignmentLog2 =
                            alignmentLog2,

                        ResourceFlags =
                            resourceFlags,

                        ExtraData =
                            extra,

                        Data =
                            chunkData
                    });
            }

            return result;
        }

        private static byte[] Build(
            ParsedEcf ecf)
        {
            int chunkHeaderSize =
                checked(
                    BaseChunkHeaderSize +
                    ecf.ChunkExtraDataSize);

            int chunkTableSize =
                checked(
                    chunkHeaderSize *
                    ecf.Chunks.Count);

            int initialSize =
                checked(
                    ecf.HeaderSize +
                    chunkTableSize);

            using MemoryStream stream =
                new MemoryStream();

            // Reserve header + chunk-table area.
            stream.Write(
                new byte[initialSize]);

            uint[] offsets =
                new uint[
                    ecf.Chunks.Count];

            for (int i = 0;
                 i < ecf.Chunks.Count;
                 i++)
            {
                EcfChunk chunk =
                    ecf.Chunks[i];

                if (chunk.Data.Length ==
                    0)
                {
                    offsets[i] =
                        0;

                    continue;
                }

                if (chunk.AlignmentLog2 >=
                    31)
                {
                    throw new InvalidDataException(
                        $"Unsupported ECF alignment in chunk {i}.");
                }

                int alignment =
                    1 <<
                    chunk.AlignmentLog2;

                AlignStream(
                    stream,
                    alignment);

                if (stream.Position >
                    uint.MaxValue)
                {
                    throw new InvalidDataException(
                        "ECF file exceeded 4 GB.");
                }

                offsets[i] =
                    (uint)stream.Position;

                stream.Write(
                    chunk.Data,
                    0,
                    chunk.Data.Length);
            }

            byte[] result =
                stream.ToArray();

            if (result.Length >
                uint.MaxValue)
            {
                throw new InvalidDataException(
                    "ECF file exceeded 4 GB.");
            }

            // Preserve original header padding / extra header data.
            Buffer.BlockCopy(
                ecf.OriginalHeader,
                0,
                result,
                0,
                ecf.HeaderSize);

            // -----------------------------------------------------
            // Main ECF header
            // -----------------------------------------------------

            WriteUInt32(
                result,
                0,
                EcfMagic);

            WriteUInt32(
                result,
                4,
                (uint)ecf.HeaderSize);

            // Adler is written later.
            WriteUInt32(
                result,
                8,
                0);

            WriteUInt32(
                result,
                12,
                (uint)result.Length);

            WriteUInt16(
                result,
                16,
                checked(
                    (ushort)ecf.Chunks.Count));

            WriteUInt16(
                result,
                18,
                ecf.Flags);

            WriteUInt32(
                result,
                20,
                ecf.Id);

            WriteUInt16(
                result,
                24,
                ecf.ChunkExtraDataSize);

            // -----------------------------------------------------
            // Chunk table
            // -----------------------------------------------------

            for (int i = 0;
                 i < ecf.Chunks.Count;
                 i++)
            {
                EcfChunk chunk =
                    ecf.Chunks[i];

                int p =
                    checked(
                        ecf.HeaderSize +
                        i *
                        chunkHeaderSize);

                WriteUInt64(
                    result,
                    p,
                    chunk.Id);

                WriteUInt32(
                    result,
                    p + 8,
                    offsets[i]);

                WriteUInt32(
                    result,
                    p + 12,
                    checked(
                        (uint)chunk.Data.Length));

                uint chunkAdler =
                    chunk.Data.Length == 0
                        ? 1
                        : EraCompressionService
                            .Adler32(
                                chunk.Data);

                WriteUInt32(
                    result,
                    p + 16,
                    chunkAdler);

                result[p + 20] =
                    chunk.Flags;

                result[p + 21] =
                    chunk.AlignmentLog2;

                WriteUInt16(
                    result,
                    p + 22,
                    chunk.ResourceFlags);

                if (ecf.ChunkExtraDataSize >
                    0)
                {
                    if (chunk.ExtraData.Length !=
                        ecf.ChunkExtraDataSize)
                    {
                        throw new InvalidDataException(
                            $"Chunk {i} has invalid extra-data size.");
                    }

                    Buffer.BlockCopy(
                        chunk.ExtraData,
                        0,
                        result,
                        p +
                        BaseChunkHeaderSize,
                        chunk.ExtraData.Length);
                }
            }

            // Ensemble's ECF header checksum starts after
            // Magic / HeaderSize / HeaderAdler32.
            uint headerAdler =
                EraCompressionService.Adler32(
                    result.AsSpan(
                        12,
                        ecf.HeaderSize -
                        12));

            WriteUInt32(
                result,
                8,
                headerAdler);

            return result;
        }

        private static void AlignStream(
            MemoryStream stream,
            int alignment)
        {
            long remainder =
                stream.Position %
                alignment;

            if (remainder == 0)
                return;

            int padding =
                checked(
                    (int)(
                        alignment -
                        remainder));

            stream.Write(
                new byte[padding]);
        }

        private static uint ReadUInt32(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4));
        }

        private static ushort ReadUInt16(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt16BigEndian(
                    data.AsSpan(
                        offset,
                        2));
        }

        private static ulong ReadUInt64(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt64BigEndian(
                    data.AsSpan(
                        offset,
                        8));
        }

        private static void WriteUInt32(
            byte[] data,
            int offset,
            uint value)
        {
            BinaryPrimitives
                .WriteUInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4),
                    value);
        }

        private static void WriteUInt16(
            byte[] data,
            int offset,
            ushort value)
        {
            BinaryPrimitives
                .WriteUInt16BigEndian(
                    data.AsSpan(
                        offset,
                        2),
                    value);
        }

        private static void WriteUInt64(
            byte[] data,
            int offset,
            ulong value)
        {
            BinaryPrimitives
                .WriteUInt64BigEndian(
                    data.AsSpan(
                        offset,
                        8),
                    value);
        }

        private sealed class ParsedEcf
        {
            public int HeaderSize
            {
                get;
                init;
            }

            public ushort Flags
            {
                get;
                init;
            }

            public uint Id
            {
                get;
                init;
            }

            public ushort ChunkExtraDataSize
            {
                get;
                init;
            }

            public byte[] OriginalHeader
            {
                get;
                init;
            } = Array.Empty<byte>();

            public List<EcfChunk> Chunks
            {
                get;
            } = new();
        }

        private sealed class EcfChunk
        {
            public ulong Id
            {
                get;
                init;
            }

            public byte Flags
            {
                get;
                init;
            }

            public byte AlignmentLog2
            {
                get;
                init;
            }

            public ushort ResourceFlags
            {
                get;
                init;
            }

            public byte[] ExtraData
            {
                get;
                init;
            } = Array.Empty<byte>();

            public byte[] Data
            {
                get;
                set;
            } = Array.Empty<byte>();
        }
    }
}