using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    /// <summary>
    /// Converts ordinary image files into the exact DDS/DX10
    /// format Halo Wars DE uses for skirmish map thumbnails.
    ///
    /// Output:
    ///     1024x512
    ///     BC7_UNORM
    ///     DDS/DX10
    ///     8 mip levels
    /// </summary>
    internal static class ThumbnailImageConversionService
    {
        public static byte[] ConvertToMapThumbnail(
            string imagePath)
        {
            if (string.IsNullOrWhiteSpace(
                    imagePath))
            {
                throw new ArgumentException(
                    "Image path is empty.",
                    nameof(imagePath));
            }


            if (!File.Exists(
                    imagePath))
            {
                throw new FileNotFoundException(
                    "Thumbnail image was not found.",
                    imagePath);
            }


            BitmapSource source =
                LoadBitmap(
                    imagePath);


            byte[] bgraPixels =
                RenderThumbnailPixels(
                    source);


            byte[] dds =
                EncodeBc7Dds(
                    bgraPixels);


            // -----------------------------------------------------
            // IMPORTANT:
            //
            // Do not merely trust the encoder.
            //
            // Run the generated file through the exact same
            // validation used for native Halo Wars thumbnails.
            // -----------------------------------------------------

            DdxTextureService
                .ValidateMapThumbnail(
                    dds);


            return dds;
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
                    "The selected image contains no image frame.");
            }


            BitmapFrame frame =
                decoder.Frames[0];


            if (frame.PixelWidth <=
                    0 ||
                frame.PixelHeight <=
                    0)
            {
                throw new InvalidDataException(
                    "The selected image has invalid dimensions.");
            }


            return frame;
        }


        // =========================================================
        // CENTER CROP + RESIZE
        // =========================================================

        private static byte[] RenderThumbnailPixels(
            BitmapSource source)
        {
            int targetWidth =
                DdxTextureService
                    .MapThumbnailWidth;

            int targetHeight =
                DdxTextureService
                    .MapThumbnailHeight;


            double scaleX =
                (double)targetWidth /
                source.PixelWidth;

            double scaleY =
                (double)targetHeight /
                source.PixelHeight;


            // Aspect-fill:
            //
            // Whichever scale is larger guarantees the entire
            // 1024x512 target is covered. Excess image area is
            // cropped equally from opposite sides.
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
                // Flatten transparent PNGs against black.
                //
                // Halo Wars thumbnails do not require transparency,
                // and this also means PBGRA and BGRA contain the
                // same RGB values when copied below.
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
        // BC7 DDS ENCODING
        // =========================================================

        private static byte[] EncodeBc7Dds(
            byte[] bgraPixels)
        {
            BcEncoder encoder =
                new BcEncoder();


            encoder.Options.IsParallel =
                true;


            encoder.OutputOptions.Format =
                CompressionFormat.Bc7;

            encoder.OutputOptions.FileFormat =
                OutputFileFormat.Dds;

            encoder.OutputOptions.Quality =
                CompressionQuality.Balanced;

            encoder.OutputOptions.GenerateMipMaps =
                true;

            encoder.OutputOptions.MaxMipMapLevel =
                DdxTextureService
                    .MapThumbnailMipCount;


            using MemoryStream output =
                new MemoryStream();


            encoder.EncodeToStream(
                bgraPixels,
                DdxTextureService.MapThumbnailWidth,
                DdxTextureService.MapThumbnailHeight,
                BCnEncoder.Encoder.PixelFormat.Bgra32,
                output);


            return output.ToArray();
        }
    }
}