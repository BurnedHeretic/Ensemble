using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    /// <summary>
    /// Converts ordinary image files into the raw BC1/DXT1
    /// mip payload used by Halo Wars DE XTT albedo chunk 0x6666.
    ///
    /// User-facing images are treated as being in Ensemble's
    /// displayed map orientation.
    ///
    /// Native XTT storage is rotated 90 degrees relative to that
    /// display orientation, so the image is rotated 90 degrees
    /// counter-clockwise before compression.
    /// </summary>
    internal static class TerrainTextureImageConversionService
    {
        // =========================================================
        // COMPLETE XTT REPLACEMENT
        // =========================================================

        public static byte[] ReplaceXttAlbedoFromImage(
            byte[] originalXttData,
            string imagePath)
        {
            ArgumentNullException.ThrowIfNull(
                originalXttData);


            TerrainXttRebuildService.AlbedoInfo info =
                TerrainXttRebuildService
                    .ReadAlbedoInfo(
                        originalXttData);


            byte[] bc1MipData =
                ConvertToBc1MipData(
                    imagePath,
                    info.Width,
                    info.Height,
                    info.MipCount);


            byte[] rebuilt =
                TerrainXttRebuildService
                .ReplaceAlbedo(
            originalXttData,
            bc1MipData,
            info.Width,
            info.Height,
            info.MipCount);


            // ---------------------------------------------------------
            // Halo Wars normally creates per-chunk composited terrain
            // textures from the XTT 0x2222 splat/linker chunks.
            //
            // For imported full-map albedo textures, disable those
            // linker chunks so the renderer falls back to XTT 0x6666,
            // the large unique albedo atlas we just replaced.
            // ---------------------------------------------------------

            return TerrainXttRebuildService
                .ReplaceAlbedo(
                originalXttData,
                bc1MipData,
                info.Width,
                info.Height,
                info.MipCount);
        }


        // =========================================================
        // IMAGE -> RAW BC1 MIPS
        // =========================================================

        public static byte[] ConvertToBc1MipData(
            string imagePath,
            int nativeWidth,
            int nativeHeight,
            int mipCount)
        {
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


            if (nativeWidth <=
                    0 ||
                nativeHeight <=
                    0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nativeWidth));
            }


            if (mipCount <=
                0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipCount));
            }


            BitmapSource source =
                LoadBitmap(
                    imagePath);


            // -----------------------------------------------------
            // XTT READER ORIENTATION
            //
            // Native XTT:
            //
            // nativeWidth x nativeHeight
            //
            // Ensemble's reader then rotates that 90 degrees CW,
            // producing:
            //
            // displayWidth  = nativeHeight
            // displayHeight = nativeWidth
            //
            // Therefore import into those display dimensions first.
            // -----------------------------------------------------

            int displayWidth =
                nativeHeight;

            int displayHeight =
                nativeWidth;


            byte[] displayPixels =
                RenderImage(
                    source,
                    displayWidth,
                    displayHeight);


            // Inverse of TerrainXttService.Read():
            //
            // Read:
            // native -> rotate CW -> display
            //
            // Write:
            // display -> rotate CCW -> native

            byte[] nativePixels =
                Rotate90CounterClockwise(
                    displayPixels,
                    displayWidth,
                    displayHeight);


            byte[] bc1MipData =
                EncodeBc1MipChain(
                    nativePixels,
                    nativeWidth,
                    nativeHeight,
                    mipCount);


            int expectedSize =
                TerrainXttRebuildService
                    .CalculateBc1MipPayloadSize(
                        nativeWidth,
                        nativeHeight,
                        mipCount);


            if (bc1MipData.Length !=
                expectedSize)
            {
                throw new InvalidDataException(
                    "Encoded XTT BC1 mip payload has " +
                    "an unexpected size.\n\n" +
                    $"Expected: {expectedSize:N0} bytes\n" +
                    $"Actual:   {bc1MipData.Length:N0} bytes");
            }


            return bc1MipData;
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
        // RESIZE TO DISPLAY MAP DIMENSIONS
        // =========================================================

        private static byte[] RenderImage(
            BitmapSource source,
            int targetWidth,
            int targetHeight)
        {
            DrawingVisual visual =
                new DrawingVisual();


            RenderOptions.SetBitmapScalingMode(
                visual,
                BitmapScalingMode.HighQuality);


            using (
                DrawingContext drawing =
                    visual.RenderOpen())
            {
                // Terrain BC1 should always be opaque.
                //
                // Flatten transparent PNG pixels against black so
                // BC1 does not accidentally enter transparent mode.

                drawing.DrawRectangle(
                    Brushes.Black,
                    null,
                    new Rect(
                        0,
                        0,
                        targetWidth,
                        targetHeight));


                // Terrain artwork represents the complete map.
                //
                // Unlike the thumbnail converter, deliberately do
                // NOT crop here. The source image is scaled to the
                // exact terrain atlas dimensions.

                drawing.DrawImage(
                    source,
                    new Rect(
                        0,
                        0,
                        targetWidth,
                        targetHeight));
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


            // Because the image was composited over opaque black,
            // alpha is now 255 everywhere. Therefore PBGRA colour
            // values are equivalent to ordinary BGRA values.

            return pixels;
        }


        // =========================================================
        // DISPLAY -> NATIVE XTT ORIENTATION
        // =========================================================

        private static byte[] Rotate90CounterClockwise(
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
                    // 90 degree counter-clockwise:
                    //
                    // dstX = srcY
                    // dstY = dstHeight - 1 - srcX

                    int destinationX =
                        sourceY;

                    int destinationY =
                        destinationHeight -
                        1 -
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
                        255;
                }
            }


            return destination;
        }


        // =========================================================
        // BC1 / DXT1 ENCODING
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
                    "BC1 encoder generated the wrong " +
                    "number of XTT mip levels.\n\n" +
                    $"Expected: {mipCount}\n" +
                    $"Actual:   {encodedMips.Length}");
            }


            int totalSize =
                0;


            foreach (byte[] mip
                     in encodedMips)
            {
                totalSize =
                    checked(
                        totalSize +
                        mip.Length);
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
    }
}