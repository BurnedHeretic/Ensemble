using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    internal static class TerrainXttService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const ulong AlbedoChunkId =
            0x6666;

        private const int EcfBaseHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;


        public static TerrainTextureMap Read(
            byte[] xttData)
        {
            if (xttData == null)
            {
                throw new ArgumentNullException(
                    nameof(xttData));
            }

            Dictionary<ulong, byte[]> chunks =
                ReadEcfChunks(
                    xttData);

            if (!chunks.TryGetValue(
                    AlbedoChunkId,
                    out byte[]? albedo))
            {
                throw new InvalidDataException(
                    "XTT contains no albedo chunk 0x6666.");
            }

            if (albedo.Length <
                16)
            {
                throw new InvalidDataException(
                    "XTT albedo chunk is too small.");
            }


            int memSize =
                ReadInt32BigEndian(
                    albedo,
                    0);

            int width =
                ReadInt32BigEndian(
                    albedo,
                    4);

            int height =
                ReadInt32BigEndian(
                    albedo,
                    8);

            int mipCount =
                ReadInt32BigEndian(
                    albedo,
                    12);


            if (width <= 0 ||
                height <= 0 ||
                width > 16384 ||
                height > 16384)
            {
                throw new InvalidDataException(
                    $"Invalid XTT dimensions: {width}×{height}.");
            }

            if (mipCount <= 0)
            {
                throw new InvalidDataException(
                    $"Invalid XTT mip count: {mipCount}.");
            }


            int blocksX =
                (width + 3) /
                4;

            int blocksY =
                (height + 3) /
                4;

            int firstMipSize =
                checked(
                    blocksX *
                    blocksY *
                    8);

            const int dataOffset =
                16;

            if (dataOffset +
                    firstMipSize >
                albedo.Length)
            {
                throw new InvalidDataException(
                    "XTT first DXT1 mip extends outside " +
                    "the albedo chunk.\n\n" +
                    $"Required: {firstMipSize:N0} bytes\n" +
                    $"Available: {albedo.Length - dataOffset:N0} bytes");
            }


            byte[] sourcePixels =
                DecodeDxt1(
                    albedo,
                    dataOffset,
                    width,
                    height);

            // Cartographer's proven transform:
            //
            // rotate 90° CCW
            // + flip horizontal
            // + flip vertical
            //
            // This is mathematically equivalent
            // to one 90° clockwise rotation.

            byte[] rotated =
                Rotate90Clockwise(
                    sourcePixels,
                    width,
                    height);

            return new TerrainTextureMap
            {
                Width =
                    height,

                Height =
                    width,

                MipCount =
                    mipCount,

                BgraPixels =
                    rotated
            };
        }


        // =========================================================
        // DXT1 / BC1
        // =========================================================

        private static byte[] DecodeDxt1(
            byte[] source,
            int sourceOffset,
            int width,
            int height)
        {
            int stride =
                checked(
                    width *
                    4);

            byte[] pixels =
                new byte[
                    checked(
                        stride *
                        height)];

            int blocksX =
                (width + 3) /
                4;

            int blocksY =
                (height + 3) /
                4;


            for (int blockY = 0;
                 blockY < blocksY;
                 blockY++)
            {
                for (int blockX = 0;
                     blockX < blocksX;
                     blockX++)
                {
                    int blockIndex =
                        checked(
                            blockY *
                            blocksX +
                            blockX);

                    int p =
                        checked(
                            sourceOffset +
                            blockIndex *
                            8);

                    ushort c0 =
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(
                                source.AsSpan(
                                    p,
                                    2));

                    ushort c1 =
                        BinaryPrimitives
                            .ReadUInt16LittleEndian(
                                source.AsSpan(
                                    p + 2,
                                    2));

                    uint lookup =
                        BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                source.AsSpan(
                                    p + 4,
                                    4));


                    Rgb colour0 =
                        DecodeRgb565(
                            c0);

                    Rgb colour1 =
                        DecodeRgb565(
                            c1);

                    Rgb colour2;
                    Rgb colour3;


                    if (c0 >
                        c1)
                    {
                        colour2 =
                            Interpolate(
                                colour0,
                                colour1,
                                2,
                                1,
                                3);

                        colour3 =
                            Interpolate(
                                colour0,
                                colour1,
                                1,
                                2,
                                3);
                    }
                    else
                    {
                        colour2 =
                            Interpolate(
                                colour0,
                                colour1,
                                1,
                                1,
                                2);

                        // Cartographer treats palette slot 3
                        // as black for terrain RGB output.
                        colour3 =
                            new Rgb(
                                0,
                                0,
                                0);
                    }


                    for (int pixelIndex = 0;
                         pixelIndex < 16;
                         pixelIndex++)
                    {
                        int colourIndex =
                            (int)(
                                (lookup >>
                                 (pixelIndex * 2))
                                &
                                0x3);

                        int localX =
                            pixelIndex %
                            4;

                        int localY =
                            pixelIndex /
                            4;

                        int x =
                            blockX *
                            4 +
                            localX;

                        int y =
                            blockY *
                            4 +
                            localY;

                        if (x >= width ||
                            y >= height)
                        {
                            continue;
                        }


                        Rgb colour =
                            colourIndex switch
                            {
                                0 => colour0,
                                1 => colour1,
                                2 => colour2,
                                _ => colour3
                            };

                        int destination =
                            checked(
                                y *
                                stride +
                                x *
                                4);

                        // WPF BGRA32
                        pixels[destination] =
                            colour.B;

                        pixels[destination + 1] =
                            colour.G;

                        pixels[destination + 2] =
                            colour.R;

                        pixels[destination + 3] =
                            255;
                    }
                }
            }

            return pixels;
        }


        private static Rgb DecodeRgb565(
            ushort value)
        {
            byte r =
                (byte)(
                    ((value >> 11) &
                     0x1F) *
                    255 /
                    31);

            byte g =
                (byte)(
                    ((value >> 5) &
                     0x3F) *
                    255 /
                    63);

            byte b =
                (byte)(
                    (value &
                     0x1F) *
                    255 /
                    31);

            return new Rgb(
                r,
                g,
                b);
        }


        private static Rgb Interpolate(
            Rgb a,
            Rgb b,
            int weightA,
            int weightB,
            int divisor)
        {
            return new Rgb(
                (byte)(
                    (a.R * weightA +
                     b.R * weightB) /
                    divisor),

                (byte)(
                    (a.G * weightA +
                     b.G * weightB) /
                    divisor),

                (byte)(
                    (a.B * weightA +
                     b.B * weightB) /
                    divisor));
        }


        private static byte[] Rotate90Clockwise(
            byte[] source,
            int sourceWidth,
            int sourceHeight)
        {
            int destinationWidth =
                sourceHeight;

            int destinationHeight =
                sourceWidth;

            byte[] destination =
                new byte[
                    checked(
                        destinationWidth *
                        destinationHeight *
                        4)];


            for (int sourceY = 0;
                 sourceY < sourceHeight;
                 sourceY++)
            {
                for (int sourceX = 0;
                     sourceX < sourceWidth;
                     sourceX++)
                {
                    int destinationX =
                        sourceHeight -
                        1 -
                        sourceY;

                    int destinationY =
                        sourceX;

                    int sourceIndex =
                        checked(
                            (sourceY *
                             sourceWidth +
                             sourceX) *
                            4);

                    int destinationIndex =
                        checked(
                            (destinationY *
                             destinationWidth +
                             destinationX) *
                            4);

                    destination[destinationIndex] =
                        source[sourceIndex];

                    destination[destinationIndex + 1] =
                        source[sourceIndex + 1];

                    destination[destinationIndex + 2] =
                        source[sourceIndex + 2];

                    destination[destinationIndex + 3] =
                        source[sourceIndex + 3];
                }
            }

            return destination;
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
                    "Invalid XTT ECF chunk table.");
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
                    ReadUInt64BigEndian(
                        data,
                        p);

                uint offset =
                    ReadUInt32BigEndian(
                        data,
                        p + 8);

                uint size =
                    ReadUInt32BigEndian(
                        data,
                        p + 12);

                long end =
                    (long)offset +
                    size;

                if (end >
                    data.Length)
                {
                    throw new InvalidDataException(
                        $"XTT ECF chunk {i} points outside the file.");
                }

                result[id] =
                    data.AsSpan(
                            checked(
                                (int)offset),
                            checked(
                                (int)size))
                        .ToArray();
            }

            return result;
        }


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


        private readonly record struct Rgb(
            byte R,
            byte G,
            byte B);
    }
}