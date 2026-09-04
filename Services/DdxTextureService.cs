using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Ensemble.Services
{
    internal static class DdxTextureService
    {
        public const int MapThumbnailWidth =
            1024;

        public const int MapThumbnailHeight =
            512;

        public const int MapThumbnailMipCount =
            8;

        public const uint Bc7UnormDxgiFormat =
            98;

        private const int DdsDx10HeaderSize =
            148;

        private static readonly byte[] DdsMagic =
            Encoding.ASCII.GetBytes(
                "DDS ");

        private static readonly byte[] Dx10FourCc =
            Encoding.ASCII.GetBytes(
                "DX10");


        /// <summary>
        /// Validates the native Halo Wars DE skirmish-map thumbnail
        /// format observed in pregameUI.era:
        ///
        /// DDS/DX10, 1024x512, 8 mip levels, BC7_UNORM.
        /// </summary>
        public static void ValidateMapThumbnail(
            byte[] data)
        {
            ArgumentNullException.ThrowIfNull(
                data);


            if (data.Length <
                DdsDx10HeaderSize)
            {
                throw new InvalidDataException(
                    "The selected DDX is too small to contain " +
                    "a DDS/DX10 texture.");
            }


            if (!data
                    .AsSpan(
                        0,
                        4)
                    .SequenceEqual(
                        DdsMagic))
            {
                throw new InvalidDataException(
                    "The selected file is not a Halo Wars DE DDX " +
                    "texture.\n\nExpected DDS magic.");
            }


            uint headerSize =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            4,
                            4));


            if (headerSize !=
                124)
            {
                throw new InvalidDataException(
                    "The selected DDX has an unexpected DDS header size.");
            }


            uint height =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            12,
                            4));


            uint width =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            16,
                            4));


            uint mipCount =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            28,
                            4));


            if (!data
                    .AsSpan(
                        84,
                        4)
                    .SequenceEqual(
                        Dx10FourCc))
            {
                throw new InvalidDataException(
                    "The selected DDX is not a DDS DX10 texture.");
            }


            uint dxgiFormat =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            128,
                            4));


            uint resourceDimension =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            132,
                            4));


            uint arraySize =
                BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        data.AsSpan(
                            140,
                            4));


            if (width !=
                    MapThumbnailWidth ||
                height !=
                    MapThumbnailHeight)
            {
                throw new InvalidDataException(
                    "Halo Wars DE map thumbnails must be " +
                    $"{MapThumbnailWidth}x{MapThumbnailHeight}.\n\n" +
                    $"Selected DDX: {width}x{height}");
            }


            if (mipCount !=
                MapThumbnailMipCount)
            {
                throw new InvalidDataException(
                    "Halo Wars DE map thumbnails must contain " +
                    $"{MapThumbnailMipCount} mip levels.\n\n" +
                    $"Selected DDX: {mipCount}");
            }


            if (dxgiFormat !=
                Bc7UnormDxgiFormat)
            {
                throw new InvalidDataException(
                    "Halo Wars DE map thumbnails must use BC7_UNORM.\n\n" +
                    $"DXGI format: {dxgiFormat}");
            }


            // D3D10_RESOURCE_DIMENSION_TEXTURE2D
            if (resourceDimension !=
                3)
            {
                throw new InvalidDataException(
                    "Halo Wars DE map thumbnails must be 2D textures.");
            }


            if (arraySize !=
                1)
            {
                throw new InvalidDataException(
                    "Halo Wars DE map thumbnails must contain " +
                    "exactly one texture.");
            }


            int expectedPayloadSize =
                CalculateBc7MipPayloadSize(
                    MapThumbnailWidth,
                    MapThumbnailHeight,
                    MapThumbnailMipCount);


            int expectedFileSize =
                checked(
                    DdsDx10HeaderSize +
                    expectedPayloadSize);


            if (data.Length !=
                expectedFileSize)
            {
                throw new InvalidDataException(
                    "The selected DDX has an unexpected BC7 payload size.\n\n" +
                    $"Expected: {expectedFileSize:N0} bytes\n" +
                    $"Actual:   {data.Length:N0} bytes");
            }
        }


        private static int CalculateBc7MipPayloadSize(
            int width,
            int height,
            int mipCount)
        {
            int total =
                0;


            for (int mip = 0;
                 mip < mipCount;
                 mip++)
            {
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


                total =
                    checked(
                        total +
                        blockWidth *
                        blockHeight *
                        16);


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
    }
}
