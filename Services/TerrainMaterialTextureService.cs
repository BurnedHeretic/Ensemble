using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    /// <summary>
    /// Handles the actual Halo Wars terrain material textures referenced
    /// by the XTT 0x1111 header.
    ///
    /// Example XTT material:
    ///
    ///     sw interior\grass_01
    ///
    /// resolves to:
    ///
    ///     art\terrain\sw interior\grass_01_df.ddx
    ///
    /// The original DDX header is preserved exactly. Only its raw
    /// BC1/DXT1 mip payload is replaced.
    /// </summary>
    internal static class TerrainMaterialTextureService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const ulong XttHeaderChunkId =
            0x1111;

        private const int ExpectedXttVersion =
            0x0004;

        private const int EcfBaseHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;

        private const int XttHeaderBaseSize =
            16;

        private const int XttFilenameSize =
            256;

        // 256-byte filename
        // + int32 UScale
        // + int32 VScale
        // + int32 BlendOp
        private const int ActiveTextureRecordSize =
            268;

        private const int LegacyDdsFileHeaderSize =
            128;


        private static readonly byte[] DdsMagic =
            Encoding.ASCII.GetBytes(
                "DDS ");

        private static readonly byte[] Dxt1FourCc =
            Encoding.ASCII.GetBytes(
                "DXT1");


        // =========================================================
        // RESULT
        // =========================================================

        internal sealed class ImportResult
        {
            public Dictionary<int, byte[]> Replacements
            {
                get;
                init;
            } =
                new();


            public List<string> MaterialNames
            {
                get;
                init;
            } =
                new();


            public List<string> ArchivePaths
            {
                get;
                init;
            } =
                new();
        }


        // =========================================================
        // MAIN ENTRY
        // =========================================================

        public static ImportResult BuildDiffuseReplacements(
            EraArchiveInfo archive,
            byte[] xttData,
            string imagePath)
        {
            ArgumentNullException.ThrowIfNull(
                archive);

            ArgumentNullException.ThrowIfNull(
                xttData);


            if (string.IsNullOrWhiteSpace(
                    imagePath))
            {
                throw new ArgumentException(
                    "Terrain texture image path is empty.",
                    nameof(imagePath));
            }


            if (!File.Exists(
                    imagePath))
            {
                throw new FileNotFoundException(
                    "Terrain texture image was not found.",
                    imagePath);
            }


            List<string> materialNames =
                ReadActiveTextureNames(
                    xttData);


            if (materialNames.Count ==
                0)
            {
                throw new InvalidDataException(
                    "The XTT contains no active terrain textures.");
            }


            BitmapSource source =
                LoadBitmap(
                    imagePath);


            ImportResult result =
                new ImportResult();


            // Different maps could theoretically use different
            // diffuse resolutions / mip counts.
            //
            // Encode once for every unique texture format instead
            // of encoding the same image once per material.

            Dictionary<
                (int Width, int Height, int MipCount),
                byte[]>
                encodedPayloadCache =
                    new();


            foreach (string materialName
                     in materialNames)
            {
                string archivePath =
                    BuildDiffuseArchivePath(
                        materialName);


                List<EraChunkInfo> matches =
                    archive
                        .Chunks
                        .Where(
                            chunk =>
                                string.Equals(
                                    NormalizeArchivePath(
                                        chunk.FileName),
                                    archivePath,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();


                if (matches.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        "Halo Wars terrain diffuse material " +
                        "could not be found in the ERA.\n\n" +
                        $"Material:\n{materialName}\n\n" +
                        $"Expected archive file:\n{archivePath}");
                }


                if (matches.Count >
                    1)
                {
                    throw new InvalidDataException(
                        "The ERA contains multiple terrain " +
                        "diffuse files with the same path.\n\n" +
                        archivePath);
                }


                EraChunkInfo chunk =
                    matches[0];


                byte[] originalDdx =
                    EraExtractionService
                        .ExtractChunk(
                            archive,
                            chunk);


                DdsInfo info =
                    ReadBc1DdsInfo(
                        originalDdx);


                (
                    int Width,
                    int Height,
                    int MipCount
                ) key =
                    (
                        info.Width,
                        info.Height,
                        info.MipCount
                    );


                if (!encodedPayloadCache
                        .TryGetValue(
                            key,
                            out byte[]? encodedPayload))
                {
                    byte[] pixels =
                        RenderMaterialImage(
                            source,
                            info.Width,
                            info.Height);


                    encodedPayload =
                        EncodeBc1MipChain(
                            pixels,
                            info.Width,
                            info.Height,
                            info.MipCount);


                    if (encodedPayload.Length !=
                        info.PayloadSize)
                    {
                        throw new InvalidDataException(
                            "Encoded terrain material BC1 payload " +
                            "has an unexpected size.\n\n" +
                            $"Texture: {info.Width}x{info.Height}\n" +
                            $"Mips: {info.MipCount}\n\n" +
                            $"Expected: {info.PayloadSize:N0} bytes\n" +
                            $"Actual:   {encodedPayload.Length:N0} bytes");
                    }


                    encodedPayloadCache[
                        key] =
                            encodedPayload;
                }


                // -------------------------------------------------
                // PRESERVE STOCK DDS HEADER EXACTLY
                //
                // We know the game's shipping terrain DDX files are
                // ordinary legacy DDS/DXT1 files.
                //
                // Preserve all 128 bytes of the original DDS header
                // and replace only the BC1 mip data after it.
                // -------------------------------------------------

                byte[] replacement =
                    originalDdx
                        .ToArray();


                Buffer.BlockCopy(
                    encodedPayload,
                    0,
                    replacement,
                    LegacyDdsFileHeaderSize,
                    encodedPayload.Length);


                ValidateTerrainDiffuseDdx(
                    replacement);


                result.Replacements[
                    chunk.Index] =
                        replacement;


                result.MaterialNames.Add(
                    materialName);


                result.ArchivePaths.Add(
                    archivePath);
            }


            return result;
        }


        // =========================================================
        // XTT 0x1111 MATERIAL LIST
        // =========================================================

        public static List<string> ReadActiveTextureNames(
            byte[] xttData)
        {
            ArgumentNullException.ThrowIfNull(
                xttData);


            EcfChunkLocation headerChunk =
                FindEcfChunk(
                    xttData,
                    XttHeaderChunkId);


            if (headerChunk.Size <
                XttHeaderBaseSize)
            {
                throw new InvalidDataException(
                    "XTT 0x1111 header chunk is too small.");
            }


            int p =
                headerChunk.DataOffset;


            int version =
                ReadInt32BigEndian(
                    xttData,
                    p);


            int activeTextureCount =
                ReadInt32BigEndian(
                    xttData,
                    p + 4);


            int activeDecalCount =
                ReadInt32BigEndian(
                    xttData,
                    p + 8);


            int activeDecalInstanceCount =
                ReadInt32BigEndian(
                    xttData,
                    p + 12);


            if (version !=
                ExpectedXttVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported XTT version 0x{version:X}. " +
                    $"Expected 0x{ExpectedXttVersion:X}.");
            }


            if (activeTextureCount <
                    0 ||
                activeTextureCount >
                    4096)
            {
                throw new InvalidDataException(
                    "XTT contains an invalid active terrain " +
                    $"texture count: {activeTextureCount}.");
            }


            if (activeDecalCount <
                    0 ||
                activeDecalInstanceCount <
                    0)
            {
                throw new InvalidDataException(
                    "XTT contains invalid decal counts.");
            }


            int requiredSize =
                checked(
                    XttHeaderBaseSize +
                    activeTextureCount *
                    ActiveTextureRecordSize);


            if (requiredSize >
                headerChunk.Size)
            {
                throw new InvalidDataException(
                    "XTT active terrain texture list extends " +
                    "outside the 0x1111 header chunk.");
            }


            List<string> result =
                new List<string>(
                    activeTextureCount);


            p +=
                XttHeaderBaseSize;


            for (int i = 0;
                 i < activeTextureCount;
                 i++)
            {
                string name =
                    ReadFixedAsciiString(
                        xttData,
                        p,
                        XttFilenameSize);


                if (string.IsNullOrWhiteSpace(
                        name))
                {
                    throw new InvalidDataException(
                        $"XTT active terrain texture {i} " +
                        "contains an empty filename.");
                }


                name =
                    NormalizeMaterialName(
                        name);


                if (!result.Contains(
                        name,
                        StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(
                        name);
                }


                p =
                    checked(
                        p +
                        ActiveTextureRecordSize);
            }


            return result;
        }


        // =========================================================
        // DDX VALIDATION
        // =========================================================

        public static void ValidateTerrainDiffuseDdx(
            byte[] data)
        {
            ReadBc1DdsInfo(
                data);
        }


        private static DdsInfo ReadBc1DdsInfo(
            byte[] data)
        {
            ArgumentNullException.ThrowIfNull(
                data);


            if (data.Length <
                LegacyDdsFileHeaderSize)
            {
                throw new InvalidDataException(
                    "Terrain DDX is too small to contain " +
                    "a legacy DDS header.");
            }


            if (!data
                    .AsSpan(
                        0,
                        4)
                    .SequenceEqual(
                        DdsMagic))
            {
                throw new InvalidDataException(
                    "Terrain DDX does not contain DDS magic.");
            }


            uint headerSize =
                ReadUInt32LittleEndian(
                    data,
                    4);


            if (headerSize !=
                124)
            {
                throw new InvalidDataException(
                    "Terrain DDX has an unexpected DDS header size.");
            }


            int height =
                checked(
                    (int)
                    ReadUInt32LittleEndian(
                        data,
                        12));


            int width =
                checked(
                    (int)
                    ReadUInt32LittleEndian(
                        data,
                        16));


            uint rawMipCount =
                ReadUInt32LittleEndian(
                    data,
                    28);


            int mipCount =
                rawMipCount ==
                    0
                    ? 1
                    : checked(
                        (int)rawMipCount);


            uint pixelFormatSize =
                ReadUInt32LittleEndian(
                    data,
                    76);


            uint pixelFormatFlags =
                ReadUInt32LittleEndian(
                    data,
                    80);


            if (pixelFormatSize !=
                32)
            {
                throw new InvalidDataException(
                    "Terrain DDX has an unexpected " +
                    "DDS pixel-format size.");
            }


            // DDPF_FOURCC
            if ((pixelFormatFlags &
                 0x00000004) ==
                0)
            {
                throw new InvalidDataException(
                    "Terrain DDX is not a FourCC " +
                    "compressed DDS texture.");
            }


            if (!data
                    .AsSpan(
                        84,
                        4)
                    .SequenceEqual(
                        Dxt1FourCc))
            {
                string fourCc =
                    Encoding.ASCII.GetString(
                        data,
                        84,
                        4);


                throw new InvalidDataException(
                    "Terrain diffuse DDX is not BC1/DXT1.\n\n" +
                    $"FourCC: {fourCc}");
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
                    $"Invalid terrain diffuse dimensions: " +
                    $"{width}x{height}.");
            }


            if (mipCount <=
                    0 ||
                mipCount >
                    32)
            {
                throw new InvalidDataException(
                    $"Invalid terrain diffuse mip count: {mipCount}.");
            }


            int payloadSize =
                CalculateBc1MipPayloadSize(
                    width,
                    height,
                    mipCount);


            int expectedFileSize =
                checked(
                    LegacyDdsFileHeaderSize +
                    payloadSize);


            if (data.Length !=
                expectedFileSize)
            {
                throw new InvalidDataException(
                    "Terrain diffuse DDX has an unexpected " +
                    "BC1 payload size.\n\n" +
                    $"Dimensions: {width}x{height}\n" +
                    $"Mips: {mipCount}\n\n" +
                    $"Expected: {expectedFileSize:N0} bytes\n" +
                    $"Actual:   {data.Length:N0} bytes");
            }


            return new DdsInfo
            {
                Width =
                    width,

                Height =
                    height,

                MipCount =
                    mipCount,

                PayloadSize =
                    payloadSize
            };
        }


        // =========================================================
        // IMAGE LOADING
        // =========================================================

        private static BitmapSource LoadBitmap(
            string imagePath)
        {
            BitmapDecoder decoder =
                BitmapDecoder.Create(
                    new Uri(
                        Path.GetFullPath(
                            imagePath)),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);


            if (decoder.Frames.Count ==
                0)
            {
                throw new InvalidDataException(
                    "The selected terrain texture " +
                    "contains no image frame.");
            }


            BitmapFrame frame =
                decoder.Frames[0];


            if (frame.PixelWidth <=
                    0 ||
                frame.PixelHeight <=
                    0)
            {
                throw new InvalidDataException(
                    "The selected terrain texture " +
                    "has invalid dimensions.");
            }


            return frame;
        }


        // =========================================================
        // IMAGE -> TERRAIN MATERIAL PIXELS
        // =========================================================

        private static byte[] RenderMaterialImage(
            BitmapSource source,
            int targetWidth,
            int targetHeight)
        {
            double scaleX =
                (double)targetWidth /
                source.PixelWidth;


            double scaleY =
                (double)targetHeight /
                source.PixelHeight;


            // Preserve image proportions.
            //
            // Terrain materials are repeating tiles rather than the
            // unique whole-map XTT atlas, so aspect-fill / centre
            // crop is preferable to stretching the artwork.

            double scale =
                Math.Max(
                    scaleX,
                    scaleY);


            double renderedWidth =
                source.PixelWidth *
                scale;


            double renderedHeight =
                source.PixelHeight *
                scale;


            double x =
                (targetWidth -
                 renderedWidth) /
                2.0;


            double y =
                (targetHeight -
                 renderedHeight) /
                2.0;


            DrawingVisual visual =
                new DrawingVisual();


            RenderOptions.SetBitmapScalingMode(
                visual,
                BitmapScalingMode.HighQuality);


            using (
                DrawingContext drawing =
                    visual.RenderOpen())
            {
                // DXT1 terrain diffuse resources are opaque.

                drawing.DrawRectangle(
                    Brushes.Black,
                    null,
                    new Rect(
                        0,
                        0,
                        targetWidth,
                        targetHeight));


                drawing.DrawImage(
                    source,
                    new Rect(
                        x,
                        y,
                        renderedWidth,
                        renderedHeight));
            }


            RenderTargetBitmap rendered =
                new RenderTargetBitmap(
                    targetWidth,
                    targetHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32);


            rendered.Render(
                visual);


            int stride =
                checked(
                    targetWidth *
                    4);


            byte[] pixels =
                new byte[
                    checked(
                        stride *
                        targetHeight)];


            rendered.CopyPixels(
                pixels,
                stride,
                0);


            return pixels;
        }


        // =========================================================
        // RAW BC1 MIP ENCODING
        // =========================================================

        private static byte[] EncodeBc1MipChain(
            byte[] bgraPixels,
            int width,
            int height,
            int mipCount)
        {
            BcEncoder encoder =
                new BcEncoder();


            encoder.Options.IsParallel =
                true;


            encoder.OutputOptions.Format =
                CompressionFormat.Bc1;


            encoder.OutputOptions.Quality =
                CompressionQuality.Balanced;


            encoder.OutputOptions.GenerateMipMaps =
                true;


            encoder.OutputOptions.MaxMipMapLevel =
                mipCount;


            byte[][] encodedMips =
                encoder.EncodeToRawBytes(
                    bgraPixels,
                    width,
                    height,
                    BCnEncoder.Encoder.PixelFormat.Bgra32);


            if (encodedMips.Length !=
                mipCount)
            {
                throw new InvalidDataException(
                    "BC1 encoder generated the wrong number " +
                    "of terrain diffuse mip levels.\n\n" +
                    $"Expected: {mipCount}\n" +
                    $"Actual:   {encodedMips.Length}");
            }


            int totalSize =
                0;


            int mipWidth =
                width;


            int mipHeight =
                height;


            for (int mip = 0;
                 mip < encodedMips.Length;
                 mip++)
            {
                int expectedMipSize =
                    CalculateBc1SingleMipSize(
                        mipWidth,
                        mipHeight);


                if (encodedMips[mip].Length !=
                    expectedMipSize)
                {
                    throw new InvalidDataException(
                        $"BC1 mip {mip} has an unexpected size.\n\n" +
                        $"Expected: {expectedMipSize:N0} bytes\n" +
                        $"Actual:   {encodedMips[mip].Length:N0} bytes");
                }


                totalSize =
                    checked(
                        totalSize +
                        encodedMips[mip].Length);


                mipWidth =
                    Math.Max(
                        1,
                        mipWidth /
                        2);


                mipHeight =
                    Math.Max(
                        1,
                        mipHeight /
                        2);
            }


            byte[] result =
                new byte[
                    totalSize];


            int destinationOffset =
                0;


            foreach (byte[] mip
                     in encodedMips)
            {
                Buffer.BlockCopy(
                    mip,
                    0,
                    result,
                    destinationOffset,
                    mip.Length);


                destinationOffset =
                    checked(
                        destinationOffset +
                        mip.Length);
            }


            return result;
        }


        // =========================================================
        // BC1 SIZE
        // =========================================================

        private static int CalculateBc1MipPayloadSize(
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
                total =
                    checked(
                        total +
                        CalculateBc1SingleMipSize(
                            width,
                            height));


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


        private static int CalculateBc1SingleMipSize(
            int width,
            int height)
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


            return checked(
                blocksX *
                blocksY *
                8);
        }


        // =========================================================
        // XTT ECF
        // =========================================================

        private static EcfChunkLocation FindEcfChunk(
            byte[] data,
            ulong wantedId)
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


            int chunkHeaderSize =
                checked(
                    EcfChunkHeaderSize +
                    extraSize);


            int tableEnd =
                checked(
                    headerSize +
                    numChunks *
                    chunkHeaderSize);


            if (headerSize <
                    EcfBaseHeaderSize ||
                tableEnd >
                    data.Length)
            {
                throw new InvalidDataException(
                    "Invalid XTT ECF chunk table.");
            }


            EcfChunkLocation? found =
                null;


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


                if (id !=
                    wantedId)
                {
                    continue;
                }


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


                if (dataOffset <
                        tableEnd ||
                    size <
                        0 ||
                    (long)dataOffset +
                        size >
                    data.Length)
                {
                    throw new InvalidDataException(
                        "XTT ECF chunk points outside the file.");
                }


                if (found !=
                    null)
                {
                    throw new InvalidDataException(
                        $"XTT contains more than one " +
                        $"0x{wantedId:X} chunk.");
                }


                found =
                    new EcfChunkLocation(
                        dataOffset,
                        size);
            }


            return found
                ?? throw new InvalidDataException(
                    $"XTT contains no 0x{wantedId:X} chunk.");
        }


        // =========================================================
        // PATHS
        // =========================================================

        private static string BuildDiffuseArchivePath(
            string materialName)
        {
            return NormalizeArchivePath(
                "art\\terrain\\" +
                NormalizeMaterialName(
                    materialName) +
                "_df.ddx");
        }


        private static string NormalizeMaterialName(
            string value)
        {
            string result =
                value
                    .Replace(
                        '/',
                        '\\')
                    .Trim();


            while (result.StartsWith(
                "\\",
                StringComparison.Ordinal))
            {
                result =
                    result[1..];
            }


            return result;
        }


        private static string NormalizeArchivePath(
            string value)
        {
            string result =
                value
                    .Replace(
                        '/',
                        '\\')
                    .Trim();


            while (result.StartsWith(
                "\\",
                StringComparison.Ordinal))
            {
                result =
                    result[1..];
            }


            return result;
        }


        // =========================================================
        // STRING
        // =========================================================

        private static string ReadFixedAsciiString(
            byte[] data,
            int offset,
            int length)
        {
            int end =
                offset;


            int maximum =
                checked(
                    offset +
                    length);


            while (end <
                       maximum &&
                   data[end] !=
                       0)
            {
                end++;
            }


            return Encoding.ASCII
                .GetString(
                    data,
                    offset,
                    end -
                        offset);
        }


        // =========================================================
        // ENDIAN
        // =========================================================

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
        // MODELS
        // =========================================================

        private readonly struct EcfChunkLocation
        {
            public EcfChunkLocation(
                int dataOffset,
                int size)
            {
                DataOffset =
                    dataOffset;

                Size =
                    size;
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


        private sealed class DdsInfo
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


            public int PayloadSize
            {
                get;
                init;
            }
        }
    }
}