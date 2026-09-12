using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    /// <summary>
    /// Small DDS decoder used by the native UGX viewport renderer.
    ///
    /// Halo Wars DE object diffuse maps observed in stock ERAs are standard
    /// DDS payloads stored with a .ddx extension.  The first native pass
    /// supports the common BC1/DXT1, BC2/DXT3 and BC3/DXT5 formats.  Unknown
    /// formats simply fall back to the editor's neutral UGX material.
    /// </summary>
    internal static class DdsTextureDecoderService
    {
        private const uint DdsMagic =
            0x20534444;

        public static BitmapSource? TryDecode(
            byte[] data)
        {
            if (data == null)
            {
                return null;
            }

            try
            {
                return Decode(
                    data);
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource Decode(
            byte[] data)
        {
            if (data.Length <
                128)
            {
                throw new InvalidDataException(
                    "DDS data is too small.");
            }

            uint magic =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    data.AsSpan(
                        0,
                        4));

            if (magic !=
                DdsMagic)
            {
                throw new InvalidDataException(
                    "Texture is not a DDS/DDX image.");
            }

            int height =
                checked(
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(
                        data.AsSpan(
                            12,
                            4)));

            int width =
                checked(
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(
                        data.AsSpan(
                            16,
                            4)));

            if (width <=
                    0 ||
                height <=
                    0 ||
                width >
                    8192 ||
                height >
                    8192)
            {
                throw new InvalidDataException(
                    "DDS dimensions are invalid.");
            }

            string fourCc =
                System.Text.Encoding.ASCII.GetString(
                    data,
                    84,
                    4);

            int payloadOffset =
                128;

            CompressionKind kind;

            if (fourCc ==
                "DXT1")
            {
                kind =
                    CompressionKind.Bc1;
            }
            else if (
                fourCc ==
                "DXT3")
            {
                kind =
                    CompressionKind.Bc2;
            }
            else if (
                fourCc ==
                "DXT5")
            {
                kind =
                    CompressionKind.Bc3;
            }
            else if (
                fourCc ==
                "DX10")
            {
                if (data.Length <
                    148)
                {
                    throw new InvalidDataException(
                        "DDS DX10 header is truncated.");
                }

                uint dxgiFormat =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        data.AsSpan(
                            128,
                            4));

                payloadOffset =
                    148;

                // BC1_UNORM / BC1_UNORM_SRGB
                if (dxgiFormat ==
                        71 ||
                    dxgiFormat ==
                        72)
                {
                    kind =
                        CompressionKind.Bc1;
                }
                // BC2_UNORM / BC2_UNORM_SRGB
                else if (
                    dxgiFormat ==
                        74 ||
                    dxgiFormat ==
                        75)
                {
                    kind =
                        CompressionKind.Bc2;
                }
                // BC3_UNORM / BC3_UNORM_SRGB
                else if (
                    dxgiFormat ==
                        77 ||
                    dxgiFormat ==
                        78)
                {
                    kind =
                        CompressionKind.Bc3;
                }
                else
                {
                    throw new InvalidDataException(
                        $"Unsupported DDS DXGI format {dxgiFormat}.");
                }
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported DDS compression '{fourCc}'.");
            }

            byte[] pixels =
                new byte[
                    checked(
                        width *
                        height *
                        4)];

            int blockBytes =
                kind ==
                    CompressionKind.Bc1
                    ? 8
                    : 16;

            int blockWidth =
                Math.Max(
                    1,
                    (width + 3) /
                    4);

            int blockHeight =
                Math.Max(
                    1,
                    (height + 3) /
                    4);

            int required =
                checked(
                    payloadOffset +
                    blockWidth *
                    blockHeight *
                    blockBytes);

            if (required >
                data.Length)
            {
                throw new InvalidDataException(
                    "DDS top mip is truncated.");
            }

            int sourceOffset =
                payloadOffset;

            for (int by = 0;
                 by <
                 blockHeight;
                 by++)
            {
                for (int bx = 0;
                     bx <
                     blockWidth;
                     bx++)
                {
                    if (kind ==
                        CompressionKind.Bc1)
                    {
                        DecodeBc1Block(
                            data,
                            sourceOffset,
                            pixels,
                            width,
                            height,
                            bx *
                            4,
                            by *
                            4,
                            allowTransparentFourthColor:
                                true,
                            alphaOverride:
                                null);
                    }
                    else if (
                        kind ==
                        CompressionKind.Bc2)
                    {
                        byte[] alpha =
                            DecodeBc2Alpha(
                                data,
                                sourceOffset);

                        DecodeBc1Block(
                            data,
                            sourceOffset +
                            8,
                            pixels,
                            width,
                            height,
                            bx *
                            4,
                            by *
                            4,
                            allowTransparentFourthColor:
                                false,
                            alphaOverride:
                                alpha);
                    }
                    else
                    {
                        byte[] alpha =
                            DecodeBc3Alpha(
                                data,
                                sourceOffset);

                        DecodeBc1Block(
                            data,
                            sourceOffset +
                            8,
                            pixels,
                            width,
                            height,
                            bx *
                            4,
                            by *
                            4,
                            allowTransparentFourthColor:
                                false,
                            alphaOverride:
                                alpha);
                    }

                    sourceOffset +=
                        blockBytes;
                }
            }

            BitmapSource bitmap =
                BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    checked(
                        width *
                        4));

            bitmap.Freeze();

            return bitmap;
        }

        private static void DecodeBc1Block(
            byte[] source,
            int offset,
            byte[] destination,
            int width,
            int height,
            int startX,
            int startY,
            bool allowTransparentFourthColor,
            byte[]? alphaOverride)
        {
            ushort c0 =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    source.AsSpan(
                        offset,
                        2));

            ushort c1 =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    source.AsSpan(
                        offset +
                        2,
                        2));

            Rgba[] colours =
                new Rgba[4];

            colours[0] =
                Decode565(
                    c0);

            colours[1] =
                Decode565(
                    c1);

            if (!allowTransparentFourthColor ||
                c0 >
                c1)
            {
                colours[2] =
                    Lerp(
                        colours[0],
                        colours[1],
                        2,
                        1,
                        3);

                colours[3] =
                    Lerp(
                        colours[0],
                        colours[1],
                        1,
                        2,
                        3);
            }
            else
            {
                colours[2] =
                    Lerp(
                        colours[0],
                        colours[1],
                        1,
                        1,
                        2);

                colours[3] =
                    new Rgba(
                        0,
                        0,
                        0,
                        0);
            }

            uint indices =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    source.AsSpan(
                        offset +
                        4,
                        4));

            for (int py = 0;
                 py <
                 4;
                 py++)
            {
                for (int px = 0;
                     px <
                     4;
                     px++)
                {
                    int x =
                        startX +
                        px;

                    int y =
                        startY +
                        py;

                    int pixelIndex =
                        py *
                        4 +
                        px;

                    int colourIndex =
                        (int)(
                            (indices >>
                             (pixelIndex *
                              2)) &
                            0x03);

                    if (x >=
                            width ||
                        y >=
                            height)
                    {
                        continue;
                    }

                    Rgba colour =
                        colours[
                            colourIndex];

                    byte alpha =
                        alphaOverride !=
                            null
                            ? alphaOverride[
                                pixelIndex]
                            : colour.A;

                    int destinationOffset =
                        checked(
                            (
                                y *
                                width +
                                x
                            ) *
                            4);

                    destination[
                        destinationOffset +
                        0] =
                            colour.B;

                    destination[
                        destinationOffset +
                        1] =
                            colour.G;

                    destination[
                        destinationOffset +
                        2] =
                            colour.R;

                    destination[
                        destinationOffset +
                        3] =
                            alpha;
                }
            }
        }

        private static byte[] DecodeBc2Alpha(
            byte[] data,
            int offset)
        {
            byte[] result =
                new byte[16];

            ulong bits =
                BinaryPrimitives.ReadUInt64LittleEndian(
                    data.AsSpan(
                        offset,
                        8));

            for (int i = 0;
                 i <
                 16;
                 i++)
            {
                int value =
                    (int)(
                        (bits >>
                         (i *
                          4)) &
                        0x0F);

                result[i] =
                    (byte)(
                        value *
                        17);
            }

            return result;
        }

        private static byte[] DecodeBc3Alpha(
            byte[] data,
            int offset)
        {
            byte a0 =
                data[
                    offset];

            byte a1 =
                data[
                    offset +
                    1];

            byte[] palette =
                new byte[8];

            palette[0] =
                a0;

            palette[1] =
                a1;

            if (a0 >
                a1)
            {
                for (int i = 1;
                     i <=
                     6;
                     i++)
                {
                    palette[
                        i +
                        1] =
                            (byte)(
                                (
                                    (
                                        7 -
                                        i
                                    ) *
                                    a0 +
                                    i *
                                    a1
                                ) /
                                7);
                }
            }
            else
            {
                for (int i = 1;
                     i <=
                     4;
                     i++)
                {
                    palette[
                        i +
                        1] =
                            (byte)(
                                (
                                    (
                                        5 -
                                        i
                                    ) *
                                    a0 +
                                    i *
                                    a1
                                ) /
                                5);
                }

                palette[6] =
                    0;

                palette[7] =
                    255;
            }

            ulong indexBits =
                0;

            for (int i = 0;
                 i <
                 6;
                 i++)
            {
                indexBits |=
                    (ulong)data[
                        offset +
                        2 +
                        i]
                    <<
                    (i *
                     8);
            }

            byte[] result =
                new byte[16];

            for (int i = 0;
                 i <
                 16;
                 i++)
            {
                int paletteIndex =
                    (int)(
                        (indexBits >>
                         (i *
                          3)) &
                        0x07);

                result[i] =
                    palette[
                        paletteIndex];
            }

            return result;
        }

        private static Rgba Decode565(
            ushort value)
        {
            int r5 =
                (
                    value >>
                    11
                ) &
                0x1F;

            int g6 =
                (
                    value >>
                    5
                ) &
                0x3F;

            int b5 =
                value &
                0x1F;

            byte r =
                (byte)(
                    (
                        r5 *
                        255 +
                        15
                    ) /
                    31);

            byte g =
                (byte)(
                    (
                        g6 *
                        255 +
                        31
                    ) /
                    63);

            byte b =
                (byte)(
                    (
                        b5 *
                        255 +
                        15
                    ) /
                    31);

            return new Rgba(
                r,
                g,
                b,
                255);
        }

        private static Rgba Lerp(
            Rgba a,
            Rgba b,
            int aWeight,
            int bWeight,
            int divisor)
        {
            return new Rgba(
                (byte)(
                    (
                        a.R *
                        aWeight +
                        b.R *
                        bWeight
                    ) /
                    divisor),

                (byte)(
                    (
                        a.G *
                        aWeight +
                        b.G *
                        bWeight
                    ) /
                    divisor),

                (byte)(
                    (
                        a.B *
                        aWeight +
                        b.B *
                        bWeight
                    ) /
                    divisor),

                255);
        }

        private readonly struct Rgba
        {
            public Rgba(
                byte r,
                byte g,
                byte b,
                byte a)
            {
                R =
                    r;

                G =
                    g;

                B =
                    b;

                A =
                    a;
            }

            public byte R
            {
                get;
            }

            public byte G
            {
                get;
            }

            public byte B
            {
                get;
            }

            public byte A
            {
                get;
            }
        }

        private enum CompressionKind
        {
            Bc1,
            Bc2,
            Bc3
        }
    }
}
