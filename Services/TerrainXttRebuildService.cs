using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    /// <summary>
    /// Rebuilds and modifies the ECF container used by
    /// Halo Wars DE XTT terrain texture files.
    /// </summary>
    internal static class TerrainXttRebuildService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const ulong AlbedoChunkId =
            0x6666;

        private const ulong FoliageHeaderChunkId =
            0xAAAA;


        private const ulong FoliageQnChunkId =
            0xBBBB;

        // Ensemble-specific disabled linker ID.
        //
        // Halo Wars' XTT loader does not handle this ID,
        // so the original linker bytes remain preserved
        // but are ignored by the game.

        private const int EcfBaseHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;

        private const int AlbedoHeaderSize =
            16;


        // =========================================================
        // ALBEDO INFORMATION
        // =========================================================

        internal sealed class AlbedoInfo
        {
            public int Width
            {
                get;
                init;
            }


            public int Height
            {
                get;
                init;
            }


            public int MipCount
            {
                get;
                init;
            }


            public int MemorySize
            {
                get;
                init;
            }


            public byte[] Bc1MipData
            {
                get;
                init;
            } =
                Array.Empty<byte>();
        }


        // =========================================================
        // READ EXISTING ALBEDO
        // =========================================================

        public static AlbedoInfo ReadAlbedoInfo(
            byte[] xttData)
        {
            ArgumentNullException.ThrowIfNull(
                xttData);


            EcfLayout layout =
                ParseLayout(
                    xttData);


            EcfChunk albedo =
                layout.Chunks
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            AlbedoChunkId)
                ?? throw new InvalidDataException(
                    "XTT contains no albedo chunk 0x6666.");


            if (albedo.Size <
                AlbedoHeaderSize)
            {
                throw new InvalidDataException(
                    "XTT albedo chunk is too small.");
            }


            int memorySize =
                ReadInt32BigEndian(
                    xttData,
                    albedo.DataOffset);

            int width =
                ReadInt32BigEndian(
                    xttData,
                    albedo.DataOffset +
                    4);

            int height =
                ReadInt32BigEndian(
                    xttData,
                    albedo.DataOffset +
                    8);

            int mipCount =
                ReadInt32BigEndian(
                    xttData,
                    albedo.DataOffset +
                    12);


            if (memorySize <
                0)
            {
                throw new InvalidDataException(
                    "XTT albedo has a negative memory size.");
            }


            if (width <=
                    0 ||
                height <=
                    0 ||
                width >
                    16384 ||
                height >
                    16384)
            {
                throw new InvalidDataException(
                    $"Invalid XTT albedo dimensions: " +
                    $"{width}x{height}.");
            }


            if (mipCount <=
                    0 ||
                mipCount >
                    32)
            {
                throw new InvalidDataException(
                    $"Invalid XTT mip count: {mipCount}.");
            }


            if ((long)AlbedoHeaderSize +
                    memorySize >
                albedo.Size)
            {
                throw new InvalidDataException(
                    "XTT albedo memory size extends " +
                    "outside the chunk.");
            }


            byte[] payload =
                xttData
                    .AsSpan(
                        albedo.DataOffset +
                            AlbedoHeaderSize,
                        memorySize)
                    .ToArray();


            return new AlbedoInfo
            {
                Width =
                    width,

                Height =
                    height,

                MipCount =
                    mipCount,

                MemorySize =
                    memorySize,

                Bc1MipData =
                    payload
            };
        }


        // =========================================================
        // REPLACE ALBEDO
        // =========================================================

        public static byte[] ReplaceAlbedo(
            byte[] originalXttData,
            byte[] bc1MipData,
            int width,
            int height,
            int mipCount)
        {
            ArgumentNullException.ThrowIfNull(
                originalXttData);

            ArgumentNullException.ThrowIfNull(
                bc1MipData);


            if (width <=
                    0 ||
                height <=
                    0 ||
                width >
                    16384 ||
                height >
                    16384)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Invalid XTT texture dimensions.");
            }


            if (mipCount <=
                    0 ||
                mipCount >
                    32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipCount));
            }


            int expectedPayloadSize =
                CalculateBc1MipPayloadSize(
                    width,
                    height,
                    mipCount);


            if (bc1MipData.Length !=
                expectedPayloadSize)
            {
                throw new InvalidDataException(
                    "BC1 terrain texture payload has " +
                    "an unexpected size.\n\n" +
                    $"Expected: {expectedPayloadSize:N0} bytes\n" +
                    $"Actual:   {bc1MipData.Length:N0} bytes");
            }


            byte[] newAlbedo =
                BuildAlbedoChunk(
                    bc1MipData,
                    width,
                    height,
                    mipCount);


            return RebuildEcf(
                originalXttData,
                newAlbedo);
        }


        // =========================================================
        // IDENTITY REBUILD
        //
        // Useful while developing the writer. Rebuilds an XTT using
        // its own existing albedo.
        // =========================================================

        public static byte[] RebuildUnchanged(
            byte[] originalXttData)
        {
            AlbedoInfo info =
                ReadAlbedoInfo(
                    originalXttData);


            return ReplaceAlbedo(
                originalXttData,
                info.Bc1MipData,
                info.Width,
                info.Height,
                info.MipCount);
        }

        // =========================================================
        // FOLIAGE
        // =========================================================

        public static int CountFoliageChunks(
            byte[] xttData)
        {
            ArgumentNullException.ThrowIfNull(
                xttData);


            return EcfFileService
                .CountChunks(
                    xttData,
                    FoliageHeaderChunkId,
                    FoliageQnChunkId);
        }


        public static byte[] RemoveAllFoliage(
            byte[] originalXttData,
            out int removedChunkCount)
        {
            ArgumentNullException.ThrowIfNull(
                originalXttData);


            byte[] result =
                EcfFileService
                    .RemoveChunks(
                        originalXttData,
                        out removedChunkCount,
                        FoliageHeaderChunkId,
                        FoliageQnChunkId);


            if (CountFoliageChunks(
                    result) !=
                0)
            {
                throw new InvalidDataException(
                    "XTT foliage removal verification failed.");
            }


            // Make sure the remaining XTT is still structurally
            // readable and still contains its albedo atlas.
            ReadAlbedoInfo(
                result);


            return result;
        }


        // =========================================================
        // BUILD 0x6666
        // =========================================================

        private static byte[] BuildAlbedoChunk(
            byte[] bc1MipData,
            int width,
            int height,
            int mipCount)
        {
            byte[] result =
                new byte[
                    checked(
                        AlbedoHeaderSize +
                        bc1MipData.Length)];


            WriteInt32BigEndian(
                result,
                0,
                bc1MipData.Length);

            WriteInt32BigEndian(
                result,
                4,
                width);

            WriteInt32BigEndian(
                result,
                8,
                height);

            WriteInt32BigEndian(
                result,
                12,
                mipCount);


            Buffer.BlockCopy(
                bc1MipData,
                0,
                result,
                AlbedoHeaderSize,
                bc1MipData.Length);


            return result;
        }


        // =========================================================
        // REBUILD ECF
        // =========================================================

        private static byte[] RebuildEcf(
            byte[] original,
            byte[] newAlbedo)
        {
            EcfLayout layout =
                ParseLayout(
                    original);


            List<EcfChunk> matchingAlbedo =
                layout.Chunks
                    .Where(
                        x =>
                            x.Id ==
                            AlbedoChunkId)
                    .ToList();


            if (matchingAlbedo.Count !=
                1)
            {
                throw new InvalidDataException(
                    "Expected exactly one XTT " +
                    "albedo chunk 0x6666.");
            }


            int prefixSize =
                checked(
                    layout.HeaderSize +
                    layout.Chunks.Count *
                    layout.ChunkHeaderSize);


            int oldAlbedoSize =
                matchingAlbedo[0].Size;


            int capacity =
                checked(
                    original.Length +
                    Math.Max(
                        0,
                        newAlbedo.Length -
                        oldAlbedoSize) +
                    64);


            using MemoryStream output =
                new MemoryStream(
                    capacity);


            // Preserve the complete ECF header and original
            // chunk-table metadata. Offset/size/Adler fields
            // are rewritten after all chunk data is laid out.
            output.Write(
                original,
                0,
                prefixSize);


            int[] newOffsets =
                new int[
                    layout.Chunks.Count];

            int[] newSizes =
                new int[
                    layout.Chunks.Count];

            uint[] newAdlers =
                new uint[
                    layout.Chunks.Count];


            // =====================================================
            // CHUNK DATA
            // =====================================================

            for (int i = 0;
                 i < layout.Chunks.Count;
                 i++)
            {
                EcfChunk chunk =
                    layout.Chunks[i];


                int alignment =
                    GetAlignment(
                        chunk.AlignmentLog2);


                Align(
                    output,
                    alignment);


                if (output.Position >
                    uint.MaxValue)
                {
                    throw new InvalidDataException(
                        "XTT exceeded the supported size.");
                }


                int newOffset =
                    checked(
                        (int)output.Position);


                byte[] data;


                if (chunk.Id ==
                    AlbedoChunkId)
                {
                    data =
                        newAlbedo;
                }
                else
                {
                    data =
                        original
                            .AsSpan(
                                chunk.DataOffset,
                                chunk.Size)
                            .ToArray();
                }


                output.Write(
                    data,
                    0,
                    data.Length);


                newOffsets[i] =
                    newOffset;

                newSizes[i] =
                    data.Length;

                newAdlers[i] =
                    EraCompressionService
                        .Adler32(
                            data);
            }


            // Preserve any data following the final ECF chunk.
            // Normally there is none, but keeping it makes this
            // writer safer for variants we haven't encountered yet.
            int originalLastChunkEnd =
                layout.Chunks
                    .Max(
                        x =>
                            checked(
                                x.DataOffset +
                                x.Size));


            if (originalLastChunkEnd <
                original.Length)
            {
                output.Write(
                    original,
                    originalLastChunkEnd,
                    original.Length -
                        originalLastChunkEnd);
            }


            byte[] result =
                output.ToArray();


            // =====================================================
            // REWRITE CHUNK HEADERS
            // =====================================================

            for (int i = 0;
                 i < layout.Chunks.Count;
                 i++)
            {
                EcfChunk chunk =
                    layout.Chunks[i];


                WriteUInt32BigEndian(
                    result,
                    chunk.TableOffset +
                        8,
                    checked(
                        (uint)newOffsets[i]));


                WriteUInt32BigEndian(
                    result,
                    chunk.TableOffset +
                        12,
                    checked(
                        (uint)newSizes[i]));


                WriteUInt32BigEndian(
                    result,
                    chunk.TableOffset +
                        16,
                    newAdlers[i]);
            }


            // =====================================================
            // ECF FILE SIZE
            // =====================================================

            WriteUInt32BigEndian(
                result,
                12,
                checked(
                    (uint)result.Length));


            // =====================================================
            // ECF HEADER ADLER32
            //
            // Ensemble's original ECF writer skips:
            //
            // Magic
            // HeaderSize
            // HeaderAdler
            //
            // and hashes the remaining header beginning at byte 12.
            // =====================================================

            uint headerAdler =
                EraCompressionService
                    .Adler32(
                        result.AsSpan(
                            12,
                            checked(
                                layout.HeaderSize -
                                12)));


            WriteUInt32BigEndian(
                result,
                8,
                headerAdler);


            ValidateEcf(
                result);


            return result;
        }


        // =========================================================
        // ECF PARSING
        // =========================================================

        private static EcfLayout ParseLayout(
            byte[] data)
        {
            if (data.Length <
                EcfBaseHeaderSize)
            {
                throw new InvalidDataException(
                    "XTT ECF file is too small.");
            }


            uint magic =
                ReadUInt32BigEndian(
                    data,
                    0);


            if (magic !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    $"Invalid XTT ECF magic 0x{magic:X8}.");
            }


            int headerSize =
                checked(
                    (int)
                    ReadUInt32BigEndian(
                        data,
                        4));


            int numChunks =
                ReadUInt16BigEndian(
                    data,
                    16);


            int extraSize =
                ReadUInt16BigEndian(
                    data,
                    24);


            if (headerSize <
                EcfBaseHeaderSize)
            {
                throw new InvalidDataException(
                    "Invalid XTT ECF header size.");
            }


            int chunkHeaderSize =
                checked(
                    EcfChunkHeaderSize +
                    extraSize);


            int tableEnd =
                checked(
                    headerSize +
                    numChunks *
                    chunkHeaderSize);


            if (tableEnd >
                data.Length)
            {
                throw new InvalidDataException(
                    "XTT ECF chunk table extends " +
                    "outside the file.");
            }


            List<EcfChunk> chunks =
                new List<EcfChunk>(
                    numChunks);


            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int tableOffset =
                    checked(
                        headerSize +
                        i *
                        chunkHeaderSize);


                ulong id =
                    ReadUInt64BigEndian(
                        data,
                        tableOffset);


                int dataOffset =
                    checked(
                        (int)
                        ReadUInt32BigEndian(
                            data,
                            tableOffset +
                            8));


                int size =
                    checked(
                        (int)
                        ReadUInt32BigEndian(
                            data,
                            tableOffset +
                            12));


                byte alignmentLog2 =
                    data[
                        tableOffset +
                        21];


                if (dataOffset <
                        tableEnd ||
                    size <
                        0 ||
                    (long)dataOffset +
                        size >
                    data.Length)
                {
                    throw new InvalidDataException(
                        $"XTT ECF chunk {i} points " +
                        "outside the file.");
                }


                chunks.Add(
                    new EcfChunk
                    {
                        Index =
                            i,

                        Id =
                            id,

                        TableOffset =
                            tableOffset,

                        DataOffset =
                            dataOffset,

                        Size =
                            size,

                        AlignmentLog2 =
                            alignmentLog2
                    });
            }


            return new EcfLayout
            {
                HeaderSize =
                    headerSize,

                ChunkHeaderSize =
                    chunkHeaderSize,

                Chunks =
                    chunks
            };
        }


        // =========================================================
        // VALIDATION
        // =========================================================

        private static void ValidateEcf(
            byte[] data)
        {
            EcfLayout layout =
                ParseLayout(
                    data);


            uint storedFileSize =
                ReadUInt32BigEndian(
                    data,
                    12);


            if (storedFileSize !=
                data.Length)
            {
                throw new InvalidDataException(
                    "Rebuilt XTT ECF file-size " +
                    "verification failed.");
            }


            uint storedHeaderAdler =
                ReadUInt32BigEndian(
                    data,
                    8);


            uint calculatedHeaderAdler =
                EraCompressionService
                    .Adler32(
                        data.AsSpan(
                            12,
                            checked(
                                layout.HeaderSize -
                                12)));


            if (storedHeaderAdler !=
                calculatedHeaderAdler)
            {
                throw new InvalidDataException(
                    "Rebuilt XTT ECF header " +
                    "Adler32 verification failed.");
            }


            foreach (EcfChunk chunk
                     in layout.Chunks)
            {
                uint storedAdler =
                    ReadUInt32BigEndian(
                        data,
                        chunk.TableOffset +
                        16);


                uint calculatedAdler =
                    EraCompressionService
                        .Adler32(
                            data.AsSpan(
                                chunk.DataOffset,
                                chunk.Size));


                if (storedAdler !=
                    calculatedAdler)
                {
                    throw new InvalidDataException(
                        $"Rebuilt XTT chunk " +
                        $"{chunk.Index} " +
                        $"(0x{chunk.Id:X}) " +
                        "failed Adler32 verification.");
                }


                int alignment =
                    GetAlignment(
                        chunk.AlignmentLog2);


                if ((chunk.DataOffset %
                     alignment) !=
                    0)
                {
                    throw new InvalidDataException(
                        $"Rebuilt XTT chunk {chunk.Index} " +
                        "has invalid alignment.");
                }
            }


            // Ensure the new albedo itself is structurally readable.
            ReadAlbedoInfo(
                data);
        }


        // =========================================================
        // BC1 SIZE
        // =========================================================

        public static int CalculateBc1MipPayloadSize(
            int width,
            int height,
            int mipCount)
        {
            if (width <=
                    0 ||
                height <=
                    0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width));
            }


            if (mipCount <=
                0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipCount));
            }


            int total =
                0;


            for (int mip = 0;
                 mip < mipCount;
                 mip++)
            {
                int blocksX =
                    Math.Max(
                        1,
                        (width + 3) /
                        4);


                int blocksY =
                    Math.Max(
                        1,
                        (height + 3) /
                        4);


                total =
                    checked(
                        total +
                        blocksX *
                        blocksY *
                        8);


                width =
                    Math.Max(
                        1,
                        width /
                        2);

                height =
                    Math.Max(
                        1,
                        height /
                        2);
            }


            return total;
        }


        // =========================================================
        // ALIGNMENT
        // =========================================================

        private static int GetAlignment(
            byte alignmentLog2)
        {
            if (alignmentLog2 >
                30)
            {
                throw new InvalidDataException(
                    "XTT contains an unsupported " +
                    "chunk alignment.");
            }


            return
                1 <<
                alignmentLog2;
        }


        private static void Align(
            MemoryStream stream,
            int alignment)
        {
            long remainder =
                stream.Position %
                alignment;


            if (remainder ==
                0)
            {
                return;
            }


            int padding =
                checked(
                    (int)(
                        alignment -
                        remainder));


            stream.Write(
                new byte[
                    padding]);
        }


        // =========================================================
        // BIG-ENDIAN READERS
        // =========================================================

        private static ushort ReadUInt16BigEndian(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt16BigEndian(
                        data.AsSpan(
                            offset,
                            2));
        }


        private static uint ReadUInt32BigEndian(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4));
        }


        private static ulong ReadUInt64BigEndian(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt64BigEndian(
                        data.AsSpan(
                            offset,
                            8));
        }


        private static int ReadInt32BigEndian(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4));
        }


        // =========================================================
        // BIG-ENDIAN WRITERS
        // =========================================================

        private static void WriteUInt32BigEndian(
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


        private static void WriteInt32BigEndian(
            byte[] data,
            int offset,
            int value)
        {
            BinaryPrimitives
                .WriteInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4),
                    value);
        }


        // =========================================================
        // INTERNAL ECF MODELS
        // =========================================================

        private sealed class EcfLayout
        {
            public int HeaderSize
            {
                get;
                init;
            }


            public int ChunkHeaderSize
            {
                get;
                init;
            }


            public List<EcfChunk> Chunks
            {
                get;
                init;
            } =
                new();
        }


        private sealed class EcfChunk
        {
            public int Index
            {
                get;
                init;
            }


            public ulong Id
            {
                get;
                init;
            }


            public int TableOffset
            {
                get;
                init;
            }


            public int DataOffset
            {
                get;
                init;
            }


            public int Size
            {
                get;
                init;
            }


            public byte AlignmentLog2
            {
                get;
                init;
            }
        }
    }
}