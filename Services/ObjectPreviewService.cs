using Ensemble.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ensemble.Services
{
    /// <summary>
    /// Creates lightweight editor thumbnails for the Object Browser.
    ///
    /// These are schematic mini-viewport renders generated from object
    /// metadata. They are intentionally independent from Halo Wars model
    /// assets, so the browser remains fast and works before a full UGX/model
    /// renderer is implemented.
    /// </summary>
    internal static class ObjectPreviewService
    {
        private const int Width =
            180;

        private const int Height =
            96;

        public static ImageSource Create(
            ObjectCatalogLayer layer,
            string name,
            string type)
        {
            DrawingVisual visual =
                new DrawingVisual();

            using DrawingContext dc =
                visual.RenderOpen();

            DrawBackground(
                dc);

            DrawPerspectiveGrid(
                dc);

            string key =
                (
                    name +
                    " " +
                    type
                )
                .ToLowerInvariant();

            Brush accent =
                layer switch
                {
                    ObjectCatalogLayer.ArtObject =>
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x43,
                                0xC8,
                                0xF5)),

                    ObjectCatalogLayer.ImportedMesh =>
                        new SolidColorBrush(
                            Color.FromRgb(
                                0xD7,
                                0xB2,
                                0xFF)),

                    _ =>
                        new SolidColorBrush(
                            Color.FromRgb(
                                0xA6,
                                0xE8,
                                0xFF))
                };

            Pen pen =
                new Pen(
                    accent,
                    2.0);

            if (ContainsAny(
                    key,
                    "tree",
                    "bush",
                    "shrub",
                    "fern",
                    "grass",
                    "palm",
                    "plant"))
            {
                DrawTree(
                    dc,
                    accent,
                    pen);
            }
            else if (
                ContainsAny(
                    key,
                    "rock",
                    "boulder",
                    "cliff",
                    "stone"))
            {
                DrawRock(
                    dc,
                    accent,
                    pen);
            }
            else if (
                ContainsAny(
                    key,
                    "building",
                    "base",
                    "tower",
                    "wall",
                    "bridge",
                    "structure",
                    "reactor"))
            {
                DrawStructure(
                    dc,
                    accent,
                    pen);
            }
            else if (
                ContainsAny(
                    key,
                    "vehicle",
                    "warthog",
                    "tank",
                    "scorpion",
                    "pelican",
                    "hornet",
                    "wraith",
                    "ghost",
                    "banshee"))
            {
                DrawVehicle(
                    dc,
                    accent,
                    pen);
            }
            else if (
                ContainsAny(
                    key,
                    "unit",
                    "marine",
                    "grunt",
                    "elite",
                    "spartan",
                    "squad",
                    "infantry"))
            {
                DrawUnit(
                    dc,
                    accent,
                    pen);
            }
            else
            {
                DrawGenericObject(
                    dc,
                    accent,
                    pen);
            }

            DrawLayerBadge(
                dc,
                layer);

            RenderTargetBitmap bitmap =
                new RenderTargetBitmap(
                    Width,
                    Height,
                    96,
                    96,
                    PixelFormats.Pbgra32);

            bitmap.Render(
                visual);

            bitmap.Freeze();

            return bitmap;
        }

        private static void DrawBackground(
            DrawingContext dc)
        {
            dc.DrawRectangle(
                new LinearGradientBrush(
                    Color.FromRgb(
                        0x0D,
                        0x20,
                        0x2E),
                    Color.FromRgb(
                        0x04,
                        0x0B,
                        0x12),
                    90),
                null,
                new Rect(
                    0,
                    0,
                    Width,
                    Height));

            dc.DrawRectangle(
                null,
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            0x31,
                            0x5E,
                            0x73)),
                    1),
                new Rect(
                    0.5,
                    0.5,
                    Width - 1,
                    Height - 1));
        }

        private static void DrawPerspectiveGrid(
            DrawingContext dc)
        {
            Pen gridPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(
                            75,
                            0x43,
                            0xC8,
                            0xF5)),
                    0.8);

            double horizon =
                62;

            dc.DrawLine(
                gridPen,
                new Point(
                    8,
                    horizon),
                new Point(
                    Width - 8,
                    horizon));

            for (int i = -3;
                 i <= 3;
                 i++)
            {
                dc.DrawLine(
                    gridPen,
                    new Point(
                        Width / 2.0 +
                        i * 13,
                        horizon),
                    new Point(
                        Width / 2.0 +
                        i * 29,
                        Height - 6));
            }

            for (int i = 1;
                 i <= 3;
                 i++)
            {
                double y =
                    horizon +
                    i * i * 3.2;

                dc.DrawLine(
                    gridPen,
                    new Point(
                        8,
                        y),
                    new Point(
                        Width - 8,
                        y));
            }
        }

        private static void DrawTree(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            dc.DrawRectangle(
                brush,
                null,
                new Rect(
                    84,
                    45,
                    12,
                    28));

            dc.DrawEllipse(
                brush,
                pen,
                new Point(
                    90,
                    34),
                23,
                24);

            dc.DrawEllipse(
                brush,
                null,
                new Point(
                    72,
                    42),
                14,
                15);

            dc.DrawEllipse(
                brush,
                null,
                new Point(
                    108,
                    42),
                14,
                15);
        }

        private static void DrawRock(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            StreamGeometry geometry =
                new StreamGeometry();

            using (
                StreamGeometryContext gc =
                    geometry.Open())
            {
                gc.BeginFigure(
                    new Point(
                        54,
                        66),
                    true,
                    true);

                gc.LineTo(
                    new Point(
                        68,
                        42),
                    true,
                    false);

                gc.LineTo(
                    new Point(
                        97,
                        31),
                    true,
                    false);

                gc.LineTo(
                    new Point(
                        126,
                        49),
                    true,
                    false);

                gc.LineTo(
                    new Point(
                        136,
                        67),
                    true,
                    false);
            }

            geometry.Freeze();

            dc.DrawGeometry(
                brush,
                pen,
                geometry);
        }

        private static void DrawStructure(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            dc.DrawRectangle(
                brush,
                pen,
                new Rect(
                    56,
                    34,
                    68,
                    37));

            dc.DrawRectangle(
                new SolidColorBrush(
                    Color.FromRgb(
                        0x07,
                        0x14,
                        0x1F)),
                pen,
                new Rect(
                    83,
                    49,
                    17,
                    22));

            dc.DrawLine(
                pen,
                new Point(
                    48,
                    34),
                new Point(
                    132,
                    34));

            dc.DrawLine(
                pen,
                new Point(
                    60,
                    27),
                new Point(
                    120,
                    27));
        }

        private static void DrawVehicle(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            dc.DrawRoundedRectangle(
                brush,
                pen,
                new Rect(
                    55,
                    44,
                    72,
                    23),
                5,
                5);

            dc.DrawRectangle(
                brush,
                pen,
                new Rect(
                    76,
                    34,
                    30,
                    15));

            dc.DrawEllipse(
                new SolidColorBrush(
                    Color.FromRgb(
                        0x04,
                        0x0B,
                        0x12)),
                pen,
                new Point(
                    70,
                    69),
                9,
                9);

            dc.DrawEllipse(
                new SolidColorBrush(
                    Color.FromRgb(
                        0x04,
                        0x0B,
                        0x12)),
                pen,
                new Point(
                    113,
                    69),
                9,
                9);
        }

        private static void DrawUnit(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            dc.DrawEllipse(
                brush,
                pen,
                new Point(
                    90,
                    31),
                10,
                10);

            dc.DrawRoundedRectangle(
                brush,
                pen,
                new Rect(
                    76,
                    41,
                    28,
                    28),
                5,
                5);

            dc.DrawLine(
                pen,
                new Point(
                    78,
                    49),
                new Point(
                    62,
                    62));

            dc.DrawLine(
                pen,
                new Point(
                    102,
                    49),
                new Point(
                    118,
                    62));

            dc.DrawLine(
                pen,
                new Point(
                    84,
                    68),
                new Point(
                    78,
                    80));

            dc.DrawLine(
                pen,
                new Point(
                    96,
                    68),
                new Point(
                    102,
                    80));
        }

        private static void DrawGenericObject(
            DrawingContext dc,
            Brush brush,
            Pen pen)
        {
            Point a =
                new Point(
                    90,
                    25);

            Point b =
                new Point(
                    122,
                    42);

            Point c =
                new Point(
                    90,
                    60);

            Point d =
                new Point(
                    58,
                    42);

            Point lowerA =
                new Point(
                    90,
                    72);

            dc.DrawLine(
                pen,
                a,
                b);

            dc.DrawLine(
                pen,
                b,
                c);

            dc.DrawLine(
                pen,
                c,
                d);

            dc.DrawLine(
                pen,
                d,
                a);

            dc.DrawLine(
                pen,
                d,
                lowerA);

            dc.DrawLine(
                pen,
                c,
                lowerA);

            dc.DrawLine(
                pen,
                b,
                lowerA);

            dc.DrawEllipse(
                brush,
                null,
                a,
                3,
                3);

            dc.DrawEllipse(
                brush,
                null,
                b,
                3,
                3);

            dc.DrawEllipse(
                brush,
                null,
                c,
                3,
                3);

            dc.DrawEllipse(
                brush,
                null,
                d,
                3,
                3);

            dc.DrawEllipse(
                brush,
                null,
                lowerA,
                3,
                3);
        }

        private static void DrawLayerBadge(
            DrawingContext dc,
            ObjectCatalogLayer layer)
        {
            string text =
                layer switch
                {
                    ObjectCatalogLayer.ArtObject =>
                        "SC2",

                    ObjectCatalogLayer.ImportedMesh =>
                        "MESH",

                    _ =>
                        "SCN"
                };

            FormattedText formatted =
                new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI Semibold"),
                    10,
                    new SolidColorBrush(
                        Color.FromRgb(
                            0xE7,
                            0xF7,
                            0xFF)),
                    1.0);

            Rect badge =
                new Rect(
                    8,
                    8,
                    36,
                    17);

            dc.DrawRoundedRectangle(
                new SolidColorBrush(
                    Color.FromArgb(
                        210,
                        0x24,
                        0x7B,
                        0x9E)),
                null,
                badge,
                2,
                2);

            dc.DrawText(
                formatted,
                new Point(
                    15,
                    9));
        }

        private static bool ContainsAny(
            string text,
            params string[] terms)
        {
            foreach (string term
                     in terms)
            {
                if (text.Contains(
                        term,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
