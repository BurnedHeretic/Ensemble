using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    internal static class TerrainViewportTextureService
    {
        public static ImageSource? TryBuild(
            FrameworkElement source)
        {
            if (source == null)
            {
                return null;
            }

            source.UpdateLayout();

            ImageSource? largestImage =
                TryFindLargestImageSource(
                    source);

            if (largestImage != null)
            {
                return largestImage;
            }

            return TryRenderSnapshot(
                source);
        }

        private static ImageSource? TryFindLargestImageSource(
            DependencyObject root)
        {
            ImageSource? best = null;
            double bestArea = 0;

            VisitVisualTree(
                root,
                element =>
                {
                    if (element is Image image &&
                        image.Source != null)
                    {
                        double width =
                            image.ActualWidth > 1
                                ? image.ActualWidth
                                : image.Width;

                        double height =
                            image.ActualHeight > 1
                                ? image.ActualHeight
                                : image.Height;

                        double area = width * height;

                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = image.Source;
                        }
                    }
                });

            return best;
        }

        private static ImageSource? TryRenderSnapshot(
            FrameworkElement source)
        {
            double width =
                source.ActualWidth > 1
                    ? source.ActualWidth
                    : (source.Width > 1 ? source.Width : 1024);

            double height =
                source.ActualHeight > 1
                    ? source.ActualHeight
                    : (source.Height > 1 ? source.Height : 1024);

            width = System.Math.Max(64, width);
            height = System.Math.Max(64, height);

            source.Measure(
                new Size(width, height));
            source.Arrange(
                new Rect(0, 0, width, height));
            source.UpdateLayout();

            RenderTargetBitmap bitmap =
                new RenderTargetBitmap(
                    (int)System.Math.Ceiling(width),
                    (int)System.Math.Ceiling(height),
                    96,
                    96,
                    PixelFormats.Pbgra32);

            bitmap.Render(source);
            bitmap.Freeze();
            return bitmap;
        }

        private static void VisitVisualTree(
            DependencyObject root,
            System.Action<DependencyObject> visitor)
        {
            visitor(root);

            int count =
                VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child =
                    VisualTreeHelper.GetChild(root, i);

                VisitVisualTree(
                    child,
                    visitor);
            }
        }
    }
}
