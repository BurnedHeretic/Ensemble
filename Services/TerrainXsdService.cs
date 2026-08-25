using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    internal static class TerrainXsdService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const ulong HeaderChunkId =
            0x1111;

        private const ulong SimHeightsChunkId =
            0x2222;

        private const int ExpectedVersion =
            0x0004;

        private const int EcfBaseHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;


        public static TerrainSimulationMap Read(
            byte[] xsdData,
            TerrainHeightMap referenceTerrain)
        {
            if (xsdData == null)
            {
                throw new ArgumentNullException(
                    nameof(xsdData));
            }

            if (referenceTerrain == null)
            {
                throw new ArgumentNullException(
                    nameof(referenceTerrain));
            }


            EcfChunkLocation headerChunk =
                FindEcfChunk(
                    xsdData,
                    HeaderChunkId);

            EcfChunkLocation heightsChunk =
                FindEcfChunk(
                    xsdData,
                    SimHeightsChunkId);


            if (headerChunk.Size <
                32)
            {
                throw new InvalidDataException(
                    "XSD header chunk is too small.");
            }


            int version =
                ReadInt32BigEndian(
                    xsdData,
                    headerChunk.DataOffset);

            if (version !=
                ExpectedVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported XSD version 0x{version:X}. " +
                    $"Expected 0x{ExpectedVersion:X}.");
            }


            int width =
                ReadInt32BigEndian(
                    xsdData,
                    headerChunk.DataOffset +
                    16);

            int paddedWidth =
                ReadInt32BigEndian(
                    xsdData,
                    headerChunk.DataOffset +
                    20);

            float heightTileScale =
                ReadSingleBigEndian(
                    xsdData,
                    headerChunk.DataOffset +
                    24);


            if (width <=
                    1 ||
                paddedWidth <
                    width ||
                paddedWidth %
                    8 !=
                    0)
            {
                throw new InvalidDataException(
                    "XSD contains invalid simulation " +
                    "height dimensions.\n\n" +
                    $"Width: {width}\n" +
                    $"Padded width: {paddedWidth}");
            }


            int expectedBytes =
                checked(
                    paddedWidth *
                    paddedWidth *
                    2);

            if (heightsChunk.Size <
                expectedBytes)
            {
                throw new InvalidDataException(
                    "XSD SimHeights chunk is smaller " +
                    "than the header specifies.");
            }


            StorageCandidate candidate =
                DetectStorage(
                    xsdData,
                    heightsChunk,
                    width,
                    paddedWidth,
                    referenceTerrain);


            float[] heights =
                new float[
                    checked(
                        width *
                        width)];


            for (int z = 0;
                 z < width;
                 z++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int storageIndex =
                        GetStorageIndex(
                            x,
                            z,
                            paddedWidth,
                            candidate.RuntimeLayout);

                    float height =
                        ReadHalf(
                            xsdData,
                            heightsChunk.DataOffset +
                            storageIndex *
                            2,
                            candidate.BigEndian);

                    heights[
                        z *
                        width +
                        x] =
                            height;
                }
            }


            return new TerrainSimulationMap
            {
                Width =
                    width,

                PaddedWidth =
                    paddedWidth,

                HeightTileScale =
                    heightTileScale,

                Heights =
                    heights,

                StorageDescription =
                    $"{(candidate.BigEndian ? "BE" : "LE")} Half | " +
                    $"{(candidate.RuntimeLayout ? "Runtime" : "Exporter")} blocks" +
                    (candidate.FlipX
                        ? " | Flip X"
                        : string.Empty) +
                    (candidate.FlipZ
                        ? " | Flip Z"
                        : string.Empty),

                ReferenceMatchError =
                    candidate.Score
            };
        }


        // =====================================================
        // SYNCHRONISED WRITE
        // =====================================================

        public static byte[] WriteSynchronizedHeights(
            byte[] originalXsdData,
            TerrainHeightMap originalVisualTerrain,
            TerrainHeightMap editedVisualTerrain)
        {
            if (originalXsdData == null)
            {
                throw new ArgumentNullException(
                    nameof(originalXsdData));
            }


            EcfChunkLocation headerChunk =
                FindEcfChunk(
                    originalXsdData,
                    HeaderChunkId);

            EcfChunkLocation heightsChunk =
                FindEcfChunk(
                    originalXsdData,
                    SimHeightsChunkId);


            int width =
                ReadInt32BigEndian(
                    originalXsdData,
                    headerChunk.DataOffset +
                    16);

            int paddedWidth =
                ReadInt32BigEndian(
                    originalXsdData,
                    headerChunk.DataOffset +
                    20);


            StorageCandidate candidate =
                DetectStorage(
                    originalXsdData,
                    heightsChunk,
                    width,
                    paddedWidth,
                    originalVisualTerrain);


            byte[] result =
                originalXsdData.ToArray();


            for (int z = 0;
                 z < width;
                 z++)
            {
                float v =
                    z /
                    (float)(
                        width -
                        1);

                if (candidate.FlipZ)
                {
                    v =
                        1.0f -
                        v;
                }


                for (int x = 0;
                     x < width;
                     x++)
                {
                    float u =
                        x /
                        (float)(
                            width -
                            1);

                    if (candidate.FlipX)
                    {
                        u =
                            1.0f -
                            u;
                    }


                    float originalVisual =
                        SampleTerrain(
                            originalVisualTerrain,
                            u,
                            v);

                    float editedVisual =
                        SampleTerrain(
                            editedVisualTerrain,
                            u,
                            v);

                    float delta =
                        editedVisual -
                        originalVisual;


                    int storageIndex =
                        GetStorageIndex(
                            x,
                            z,
                            paddedWidth,
                            candidate.RuntimeLayout);

                    int byteOffset =
                        checked(
                            heightsChunk.DataOffset +
                            storageIndex *
                            2);


                    float originalSimHeight =
                        ReadHalf(
                            originalXsdData,
                            byteOffset,
                            candidate.BigEndian);


                    float newSimHeight =
                        originalSimHeight +
                        delta;


                    if (!float.IsFinite(
                            newSimHeight))
                    {
                        throw new InvalidDataException(
                            "XSD terrain synchronization produced " +
                            "a non-finite height.");
                    }


                    WriteHalf(
                        result,
                        byteOffset,
                        newSimHeight,
                        candidate.BigEndian);
                }
            }


            UpdateChunkAndHeaderChecksums(
                result,
                heightsChunk);


            ValidateEcfChecksums(
                result);


            // Make sure the file we just built can
            // actually be decoded again.
            TerrainSimulationMap verification =
                Read(
                    result,
                    editedVisualTerrain);

            if (verification.Width !=
                width)
            {
                throw new InvalidDataException(
                    "Rebuilt XSD failed terrain " +
                    "simulation verification.");
            }


            return result;
        }


        // =====================================================
        // STORAGE DETECTION
        // =====================================================

        private static StorageCandidate DetectStorage(
            byte[] data,
            EcfChunkLocation heightsChunk,
            int width,
            int paddedWidth,
            TerrainHeightMap referenceTerrain)
        {
            StorageCandidate? best =
                null;


            foreach (bool bigEndian
                     in new[]
                     {
                         true,
                         false
                     })
            {
                foreach (bool runtimeLayout
                         in new[]
                         {
                             true,
                             false
                         })
                {
                    foreach (bool flipX
                             in new[]
                             {
                                 false,
                                 true
                             })
                    {
                        foreach (bool flipZ
                                 in new[]
                                 {
                                     false,
                                     true
                                 })
                        {
                            float score =
                                ScoreCandidate(
                                    data,
                                    heightsChunk,
                                    width,
                                    paddedWidth,
                                    referenceTerrain,
                                    bigEndian,
                                    runtimeLayout,
                                    flipX,
                                    flipZ);


                            // Tiny preference toward the format
                            // demonstrated by the original game
                            // runtime/source when scores are
                            // essentially identical.
                            if (bigEndian)
                            {
                                score -=
                                    0.00001f;
                            }

                            if (runtimeLayout)
                            {
                                score -=
                                    0.000005f;
                            }


                            StorageCandidate candidate =
                                new StorageCandidate(
                                    bigEndian,
                                    runtimeLayout,
                                    flipX,
                                    flipZ,
                                    score);


                            if (best == null ||
                                candidate.Score <
                                    best.Value.Score)
                            {
                                best =
                                    candidate;
                            }
                        }
                    }
                }
            }


            if (best == null)
            {
                throw new InvalidDataException(
                    "Unable to determine XSD height storage.");
            }


            return best.Value;
        }


        private static float ScoreCandidate(
            byte[] data,
            EcfChunkLocation heightsChunk,
            int width,
            int paddedWidth,
            TerrainHeightMap referenceTerrain,
            bool bigEndian,
            bool runtimeLayout,
            bool flipX,
            bool flipZ)
        {
            int stride =
                Math.Max(
                    1,
                    (width - 1) /
                    24);

            double total =
                0;

            int count =
                0;


            for (int z = 0;
                 z < width;
                 z += stride)
            {
                float v =
                    z /
                    (float)(
                        width -
                        1);

                if (flipZ)
                {
                    v =
                        1.0f -
                        v;
                }


                for (int x = 0;
                     x < width;
                     x += stride)
                {
                    float u =
                        x /
                        (float)(
                            width -
                            1);

                    if (flipX)
                    {
                        u =
                            1.0f -
                            u;
                    }


                    int storageIndex =
                        GetStorageIndex(
                            x,
                            z,
                            paddedWidth,
                            runtimeLayout);

                    int byteOffset =
                        checked(
                            heightsChunk.DataOffset +
                            storageIndex *
                            2);


                    float simHeight =
                        ReadHalf(
                            data,
                            byteOffset,
                            bigEndian);


                    if (!float.IsFinite(
                            simHeight) ||
                        MathF.Abs(
                            simHeight) >
                            10000)
                    {
                        total +=
                            100000;

                        count++;

                        continue;
                    }


                    float visualHeight =
                        SampleTerrain(
                            referenceTerrain,
                            u,
                            v);

                    double difference =
                        simHeight -
                        visualHeight;

                    total +=
                        difference *
                        difference;

                    count++;
                }
            }


            if (count ==
                0)
            {
                return float.MaxValue;
            }


            return
                (float)Math.Sqrt(
                    total /
                    count);
        }


        // =====================================================
        // 8x8 CACHE-FRIENDLY STORAGE
        // =====================================================

        private static int GetStorageIndex(
            int x,
            int z,
            int paddedWidth,
            bool runtimeLayout)
        {
            int blockCount =
                paddedWidth /
                8;


            if (runtimeLayout)
            {
                // Halo Wars BTerrainSimRep::getHeight()
                //
                // block Z
                // → block X
                // → local Z
                // → local X

                int blockX =
                    x >>
                    3;

                int blockZ =
                    z >>
                    3;

                int localX =
                    x &
                    7;

                int localZ =
                    z &
                    7;


                return
                    blockZ *
                    blockCount *
                    64
                    +
                    blockX *
                    64
                    +
                    localZ *
                    8
                    +
                    localX;
            }
            else
            {
                // Phoenix Editor export ordering.
                //
                // We support this as a detected fallback
                // because the shipping/export code uses
                // opposite traversal terminology.

                int blockX =
                    x >>
                    3;

                int blockZ =
                    z >>
                    3;

                int localX =
                    x &
                    7;

                int localZ =
                    z &
                    7;


                return
                    blockX *
                    blockCount *
                    64
                    +
                    blockZ *
                    64
                    +
                    localX *
                    8
                    +
                    localZ;
            }
        }


        // =====================================================
        // TERRAIN SAMPLING
        // =====================================================

        private static float SampleTerrain(
            TerrainHeightMap terrain,
            float u,
            float v)
        {
            u =
                Math.Clamp(
                    u,
                    0,
                    1);

            v =
                Math.Clamp(
                    v,
                    0,
                    1);


            float fx =
                u *
                (terrain.Width -
                 1);

            float fz =
                v *
                (terrain.Height -
                 1);


            int x0 =
                Math.Clamp(
                    (int)MathF.Floor(
                        fx),
                    0,
                    terrain.Width -
                    1);

            int z0 =
                Math.Clamp(
                    (int)MathF.Floor(
                        fz),
                    0,
                    terrain.Height -
                    1);

            int x1 =
                Math.Min(
                    x0 +
                    1,
                    terrain.Width -
                    1);

            int z1 =
                Math.Min(
                    z0 +
                    1,
                    terrain.Height -
                    1);


            float tx =
                fx -
                x0;

            float tz =
                fz -
                z0;


            float h00 =
                terrain.Heights[
                    z0 *
                    terrain.Width +
                    x0];

            float h10 =
                terrain.Heights[
                    z0 *
                    terrain.Width +
                    x1];

            float h01 =
                terrain.Heights[
                    z1 *
                    terrain.Width +
                    x0];

            float h11 =
                terrain.Heights[
                    z1 *
                    terrain.Width +
                    x1];


            float top =
                h00 +
                (h10 -
                 h00) *
                tx;

            float bottom =
                h01 +
                (h11 -
                 h01) *
                tx;


            return
                top +
                (bottom -
                 top) *
                tz;
        }


        // =====================================================
        // HALF FLOAT
        // =====================================================

        private static float ReadHalf(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            ushort bits =
                bigEndian
                    ? BinaryPrimitives
                        .ReadUInt16BigEndian(
                            data.AsSpan(
                                offset,
                                2))
                    : BinaryPrimitives
                        .ReadUInt16LittleEndian(
                            data.AsSpan(
                                offset,
                                2));


            Half value =
                BitConverter
                    .UInt16BitsToHalf(
                        bits);


            return
                (float)value;
        }


        private static void WriteHalf(
            byte[] data,
            int offset,
            float value,
            bool bigEndian)
        {
            Half half =
                (Half)value;

            ushort bits =
                BitConverter
                    .HalfToUInt16Bits(
                        half);


            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteUInt16BigEndian(
                        data.AsSpan(
                            offset,
                            2),
                        bits);
            }
            else
            {
                BinaryPrimitives
                    .WriteUInt16LittleEndian(
                        data.AsSpan(
                            offset,
                            2),
                        bits);
            }
        }


        // =====================================================
        // ECF
        // =====================================================

        private static void UpdateChunkAndHeaderChecksums(
            byte[] data,
            EcfChunkLocation changedChunk)
        {
            uint chunkAdler =
                EraCompressionService.Adler32(
                    data.AsSpan(
                        changedChunk.DataOffset,
                        changedChunk.Size));

            BinaryPrimitives
                .WriteUInt32BigEndian(
                    data.AsSpan(
                        changedChunk.TableOffset +
                        16,
                        4),
                    chunkAdler);


            uint headerSize =
                ReadUInt32BigEndian(
                    data,
                    4);


            uint headerAdler =
                EraCompressionService.Adler32(
                    data.AsSpan(
                        12,
                        checked(
                            (int)headerSize -
                            12)));


            BinaryPrimitives
                .WriteUInt32BigEndian(
                    data.AsSpan(
                        8,
                        4),
                    headerAdler);
        }


        private static void ValidateEcfChecksums(
            byte[] data)
        {
            uint headerSize =
                ReadUInt32BigEndian(
                    data,
                    4);

            uint expectedHeaderAdler =
                ReadUInt32BigEndian(
                    data,
                    8);

            uint actualHeaderAdler =
                EraCompressionService.Adler32(
                    data.AsSpan(
                        12,
                        checked(
                            (int)headerSize -
                            12)));


            if (expectedHeaderAdler !=
                actualHeaderAdler)
            {
                throw new InvalidDataException(
                    "Rebuilt XSD ECF header failed " +
                    "its Adler32 check.");
            }


            ushort numChunks =
                ReadUInt16BigEndian(
                    data,
                    16);

            ushort extraSize =
                ReadUInt16BigEndian(
                    data,
                    24);

            int entrySize =
                checked(
                    EcfChunkHeaderSize +
                    extraSize);


            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int tableOffset =
                    checked(
                        (int)headerSize +
                        i *
                        entrySize);

                uint offset =
                    ReadUInt32BigEndian(
                        data,
                        tableOffset +
                        8);

                uint size =
                    ReadUInt32BigEndian(
                        data,
                        tableOffset +
                        12);

                uint expected =
                    ReadUInt32BigEndian(
                        data,
                        tableOffset +
                        16);


                uint actual =
                    EraCompressionService.Adler32(
                        data.AsSpan(
                            checked(
                                (int)offset),
                            checked(
                                (int)size)));


                if (expected !=
                    actual)
                {
                    throw new InvalidDataException(
                        $"Rebuilt XSD chunk {i} failed " +
                        "its Adler32 check.");
                }
            }
        }


        private static EcfChunkLocation FindEcfChunk(
            byte[] data,
            ulong wantedId)
        {
            if (data.Length <
                EcfBaseHeaderSize)
            {
                throw new InvalidDataException(
                    "XSD ECF is too small.");
            }


            if (ReadUInt32BigEndian(
                    data,
                    0) !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    "Invalid XSD ECF magic.");
            }


            uint headerSize =
                ReadUInt32BigEndian(
                    data,
                    4);

            ushort numChunks =
                ReadUInt16BigEndian(
                    data,
                    16);

            ushort extraSize =
                ReadUInt16BigEndian(
                    data,
                    24);

            int entrySize =
                checked(
                    EcfChunkHeaderSize +
                    extraSize);


            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int tableOffset =
                    checked(
                        (int)headerSize +
                        i *
                        entrySize);


                ulong id =
                    ReadUInt64BigEndian(
                        data,
                        tableOffset);


                if (id !=
                    wantedId)
                {
                    continue;
                }


                uint offset =
                    ReadUInt32BigEndian(
                        data,
                        tableOffset +
                        8);

                uint size =
                    ReadUInt32BigEndian(
                        data,
                        tableOffset +
                        12);


                if ((long)offset +
                        size >
                    data.Length)
                {
                    throw new InvalidDataException(
                        "XSD ECF chunk points outside file.");
                }


                return
                    new EcfChunkLocation(
                        tableOffset,
                        checked(
                            (int)offset),
                        checked(
                            (int)size));
            }


            throw new InvalidDataException(
                $"XSD contains no ECF chunk " +
                $"0x{wantedId:X}.");
        }


        // =====================================================
        // BIG ENDIAN HELPERS
        // =====================================================

        private static ushort ReadUInt16BigEndian(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt16BigEndian(
                    data.AsSpan(
                        offset,
                        2));
        }


        private static uint ReadUInt32BigEndian(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4));
        }


        private static ulong ReadUInt64BigEndian(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadUInt64BigEndian(
                    data.AsSpan(
                        offset,
                        8));
        }


        private static int ReadInt32BigEndian(
            byte[] data,
            int offset)
        {
            return BinaryPrimitives
                .ReadInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4));
        }


        private static float ReadSingleBigEndian(
            byte[] data,
            int offset)
        {
            return BitConverter
                .Int32BitsToSingle(
                    ReadInt32BigEndian(
                        data,
                        offset));
        }


        private readonly struct EcfChunkLocation
        {
            public EcfChunkLocation(
                int tableOffset,
                int dataOffset,
                int size)
            {
                TableOffset =
                    tableOffset;

                DataOffset =
                    dataOffset;

                Size =
                    size;
            }

            public int TableOffset
            {
                get;
            }

            public int DataOffset
            {
                get;
            }

            public int Size
            {
                get;
            }
        }


        private readonly struct StorageCandidate
        {
            public StorageCandidate(
                bool bigEndian,
                bool runtimeLayout,
                bool flipX,
                bool flipZ,
                float score)
            {
                BigEndian =
                    bigEndian;

                RuntimeLayout =
                    runtimeLayout;

                FlipX =
                    flipX;

                FlipZ =
                    flipZ;

                Score =
                    score;
            }

            public bool BigEndian
            {
                get;
            }

            public bool RuntimeLayout
            {
                get;
            }

            public bool FlipX
            {
                get;
            }

            public bool FlipZ
            {
                get;
            }

            public float Score
            {
                get;
            }
        }
    }
}