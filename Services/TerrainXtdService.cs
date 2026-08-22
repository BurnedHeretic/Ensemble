using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Ensemble.Services
{
    internal static class TerrainXtdService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const ulong XtdHeaderChunkId =
            0x1111;

        private const ulong XtdAtlasChunkId =
            0x8888;

        private const int ExpectedXtdVersion =
            0x000C;

        private const int EcfBaseHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;


        public static TerrainHeightMap Read(
            byte[] xtdData)
        {
            if (xtdData == null)
            {
                throw new ArgumentNullException(
                    nameof(xtdData));
            }

            Dictionary<ulong, byte[]> chunks =
                ReadEcfChunks(
                    xtdData);

            if (!chunks.TryGetValue(
                    XtdHeaderChunkId,
                    out byte[]? headerData))
            {
                throw new InvalidDataException(
                    "XTD contains no header chunk 0x1111.");
            }

            if (!chunks.TryGetValue(
                    XtdAtlasChunkId,
                    out byte[]? atlasData))
            {
                throw new InvalidDataException(
                    "XTD contains no terrain atlas chunk 0x8888.");
            }

            if (headerData.Length <
                40)
            {
                throw new InvalidDataException(
                    "XTD header chunk is too small.");
            }


            int version =
                ReadInt32(
                    headerData,
                    0);

            int numXVerts =
                ReadInt32(
                    headerData,
                    4);

            int numXChunks =
                ReadInt32(
                    headerData,
                    8);

            float tileScale =
                ReadSingle(
                    headerData,
                    12);

            Vector3 worldMin =
                ReadVector3(
                    headerData,
                    16);

            Vector3 worldMax =
                ReadVector3(
                    headerData,
                    28);


            if (version !=
                ExpectedXtdVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported XTD version 0x{version:X}. " +
                    $"Expected 0x{ExpectedXtdVersion:X}.");
            }

            if (numXVerts <=
                    0 ||
                numXVerts >
                    16384)
            {
                throw new InvalidDataException(
                    $"Invalid XTD vertex dimension: {numXVerts}.");
            }

            if (!float.IsFinite(
                    tileScale) ||
                tileScale <=
                    0)
            {
                throw new InvalidDataException(
                    $"Invalid XTD tile scale: {tileScale}.");
            }


            int vertexCount =
                checked(
                    numXVerts *
                    numXVerts);

            // Atlas:
            //
            // 16 bytes position compression minimum
            // 16 bytes position compression range
            // N * 4 position values
            // N * 4 normal values

            int expectedMinimumSize =
                checked(
                    32 +
                    vertexCount *
                    8);

            if (atlasData.Length <
                expectedMinimumSize)
            {
                throw new InvalidDataException(
                    "XTD terrain atlas is smaller than expected.\n\n" +
                    $"Expected at least: {expectedMinimumSize:N0} bytes\n" +
                    $"Actual: {atlasData.Length:N0} bytes");
            }


            Vector3 compressionMin =
                ReadVector3(
                    atlasData,
                    0);

            Vector3 compressionRange =
                ReadVector3(
                    atlasData,
                    16);

            float[] heights =
                new float[
                    vertexCount];

            float minHeight =
                float.MaxValue;

            float maxHeight =
                float.MinValue;

            const int positionDataOffset =
                32;


            for (int x = 0;
                x < numXVerts;
                x++)
            {
                for (int z = 0;
                     z < numXVerts;
                     z++)
                {
                    // Halo Wars DE stores the position grid
                    // column-major:
                    //
                    // raw index = X * gridHeight + Z

                    int rawIndex =
                        checked(
                            x *
                            numXVerts +
                            z);

                    int sourceOffset =
                        checked(
                            positionDataOffset +
                            rawIndex *
                            4);

                    uint packed =
                        ReadUInt32LittleEndian(
                            atlasData,
                            sourceOffset);

                    // Halo Wars DE PC:
                    //
                    // X = bits 20-29
                    // Y = bits 10-19
                    // Z = bits  0-9
                    //
                    // Remaining upper 2 bits are unused here.

                    uint encodedY =
                        (packed >> 10) &
                        0x3FF;

                    float normalizedY =
                        encodedY /
                        1023.0f;

                    float height =
                        normalizedY *
                        compressionRange.Y -
                        compressionMin.Y;

                    // Ensemble's bitmap/model representation is
                    // conventional row-major Z,X.

                    int destinationIndex =
                        checked(
                            z *
                            numXVerts +
                            x);

                    heights[destinationIndex] =
                        height;

                    if (height <
                        minHeight)
                    {
                        minHeight =
                            height;
                    }

                    if (height >
                        maxHeight)
                    {
                        maxHeight =
                            height;
                    }
                }
            }


            return new TerrainHeightMap
            {
                Width =
                    numXVerts,

                Height =
                    numXVerts,

                TileScale =
                    tileScale,

                WorldMin =
                    worldMin,

                WorldMax =
                    worldMax,

                MinHeight =
                    minHeight,

                MaxHeight =
                    maxHeight,

                Heights =
                    heights
            };
        }

        private static uint ReadUInt32LittleEndian(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt32LittleEndian(
                    data.AsSpan(
                        offset,
                        4));
        }


        // =========================================================
        // ECF
        // =========================================================

        private static Dictionary<ulong, byte[]>
            ReadEcfChunks(
                byte[] data)
        {
            if (data.Length <
                EcfBaseHeaderSize)
            {
                throw new InvalidDataException(
                    "XTD ECF file is too small.");
            }

            uint magic =
                ReadUInt32(
                    data,
                    0);

            if (magic !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    $"Invalid XTD ECF magic 0x{magic:X8}.");
            }

            uint headerSize =
                ReadUInt32(
                    data,
                    4);

            ushort numChunks =
                ReadUInt16(
                    data,
                    16);

            ushort extraSize =
                ReadUInt16(
                    data,
                    24);

            int chunkHeaderSize =
                checked(
                    EcfChunkHeaderSize +
                    extraSize);

            long tableEnd =
                (long)headerSize +
                (long)numChunks *
                chunkHeaderSize;

            if (headerSize <
                    EcfBaseHeaderSize ||
                tableEnd >
                    data.Length)
            {
                throw new InvalidDataException(
                    "Invalid XTD ECF chunk table.");
            }


            Dictionary<ulong, byte[]> result =
                new();


            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int p =
                    checked(
                        (int)headerSize +
                        i *
                        chunkHeaderSize);

                ulong id =
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

                long end =
                    (long)offset +
                    size;

                if (end >
                    data.Length)
                {
                    throw new InvalidDataException(
                        $"XTD ECF chunk {i} points outside the file.");
                }

                byte[] chunk =
                    data.AsSpan(
                            checked(
                                (int)offset),
                            checked(
                                (int)size))
                        .ToArray();

                result[id] =
                    chunk;
            }

            return result;
        }


        // =========================================================
        // BIG ENDIAN READERS
        // =========================================================

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

        private static int ReadInt32(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4));
        }

        private static float ReadSingle(
            byte[] data,
            int offset)
        {
            int bits =
                ReadInt32(
                    data,
                    offset);

            return BitConverter
                .Int32BitsToSingle(
                    bits);
        }

        private static Vector3 ReadVector3(
            byte[] data,
            int offset)
        {
            return new Vector3(
                ReadSingle(
                    data,
                    offset),

                ReadSingle(
                    data,
                    offset + 4),

                ReadSingle(
                    data,
                    offset + 8));
        }
    }
}