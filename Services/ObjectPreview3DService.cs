using Ensemble.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Ensemble.Services
{
    /// <summary>
    /// Builds a small native 3D thumbnail from the same UGX resolver used by
    /// the main 3D viewport. If the object has no resolvable UGX asset the
    /// caller can fall back to ObjectPreviewService's 2D schematic preview.
    /// </summary>
    internal static class ObjectPreview3DService
    {
        public static ImageSource? TryCreate(
            EraArchiveInfo? archive,
            ObjectCatalogEntry entry,
            int width = 180,
            int height = 96)
        {
            ArgumentNullException.ThrowIfNull(
                entry);

            if (archive ==
                    null ||
                entry.Layer ==
                    ObjectCatalogLayer.ImportedMesh)
            {
                return null;
            }

            try
            {
                string resolverName =
                    string.IsNullOrWhiteSpace(
                        entry.InternalName)
                        ? entry.Name
                        : entry.InternalName;

                UgxMeshService.UgxMeshAsset? asset =
                    UgxMeshService.TryLoadForObject(
                        archive,
                        entry.Type,
                        resolverName);

                if (asset ==
                        null ||
                    asset.Parts.Count ==
                        0)
                {
                    return null;
                }

                return RenderAsset(
                    asset,
                    width,
                    height);
            }
            catch
            {
                // Preview generation is non-critical. An unsupported or fuzzy
                // mesh must not stop Add Object from opening or being usable.
                return null;
            }
        }

        private static ImageSource? RenderAsset(
            UgxMeshService.UgxMeshAsset asset,
            int width,
            int height)
        {
            Model3DGroup scene =
                new();

            scene.Children.Add(
                new AmbientLight(
                    Color.FromRgb(
                        0x90,
                        0x98,
                        0xA0)));

            scene.Children.Add(
                new DirectionalLight(
                    Colors.White,
                    new Vector3D(
                        -0.45,
                        -1.0,
                        -0.65)));

            scene.Children.Add(
                new DirectionalLight(
                    Color.FromRgb(
                        0x61,
                        0xB8,
                        0xD8),
                    new Vector3D(
                        0.70,
                        -0.35,
                        0.40)));

            bool hasBounds =
                false;

            double minX = 0;
            double minY = 0;
            double minZ = 0;
            double maxX = 0;
            double maxY = 0;
            double maxZ = 0;

            foreach (UgxMeshService.UgxMeshPart part
                     in asset.Parts)
            {
                MeshGeometry3D geometry =
                    part.Geometry;

                if (geometry.Positions.Count ==
                    0)
                {
                    continue;
                }

                foreach (Point3D point
                         in geometry.Positions)
                {
                    if (!hasBounds)
                    {
                        minX = maxX = point.X;
                        minY = maxY = point.Y;
                        minZ = maxZ = point.Z;
                        hasBounds = true;
                    }
                    else
                    {
                        minX = Math.Min(
                            minX,
                            point.X);

                        minY = Math.Min(
                            minY,
                            point.Y);

                        minZ = Math.Min(
                            minZ,
                            point.Z);

                        maxX = Math.Max(
                            maxX,
                            point.X);

                        maxY = Math.Max(
                            maxY,
                            point.Y);

                        maxZ = Math.Max(
                            maxZ,
                            point.Z);
                    }
                }

                Material material =
                    CreateMaterial(
                        part.DiffuseTexture);

                GeometryModel3D model =
                    new(
                        geometry,
                        material)
                    {
                        BackMaterial =
                            material
                    };

                scene.Children.Add(
                    model);
            }

            if (!hasBounds)
            {
                return null;
            }

            Point3D centre =
                new(
                    (minX + maxX) * 0.5,
                    (minY + maxY) * 0.5,
                    (minZ + maxZ) * 0.5);

            double sizeX =
                Math.Max(
                    0.01,
                    maxX - minX);

            double sizeY =
                Math.Max(
                    0.01,
                    maxY - minY);

            double sizeZ =
                Math.Max(
                    0.01,
                    maxZ - minZ);

            double radius =
                Math.Max(
                    sizeX,
                    Math.Max(
                        sizeY,
                        sizeZ)) *
                0.5;

            const double fieldOfView =
                36.0;

            double halfFovRadians =
                fieldOfView *
                Math.PI /
                360.0;

            double distance =
                Math.Max(
                    1.0,
                    radius /
                    Math.Tan(
                        halfFovRadians) *
                    1.45);

            Vector3D viewDirection =
                new(
                    1.30,
                    0.85,
                    1.55);

            viewDirection.Normalize();

            Point3D cameraPosition =
                centre +
                viewDirection *
                distance;

            PerspectiveCamera camera =
                new()
                {
                    Position =
                        cameraPosition,

                    LookDirection =
                        centre -
                        cameraPosition,

                    UpDirection =
                        new Vector3D(
                            0,
                            1,
                            0),

                    FieldOfView =
                        fieldOfView,

                    NearPlaneDistance =
                        Math.Max(
                            0.01,
                            distance /
                            200.0),

                    FarPlaneDistance =
                        Math.Max(
                            1000.0,
                            distance *
                            20.0)
                };

            Viewport3D viewport =
                new()
                {
                    Camera =
                        camera,

                    ClipToBounds =
                        true,

                    IsHitTestVisible =
                        false
                };

            viewport.Children.Add(
                new ModelVisual3D
                {
                    Content =
                        scene
                });

            Grid host =
                new()
                {
                    Width =
                        width,

                    Height =
                        height,

                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x05,
                                0x0D,
                                0x14))
                };

            host.Children.Add(
                viewport);

            // A faint floor line helps small props read as solid 3D objects
            // without adding a full scene or affecting the mesh itself.
            Border footer =
                new()
                {
                    Height =
                        1,

                    VerticalAlignment =
                        VerticalAlignment.Bottom,

                    Margin =
                        new Thickness(
                            12,
                            0,
                            12,
                            8),

                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(
                                100,
                                0x67,
                                0xB9,
                                0xD5)),

                    IsHitTestVisible =
                        false
                };

            host.Children.Add(
                footer);

            Size renderSize =
                new(
                    width,
                    height);

            host.Measure(
                renderSize);

            host.Arrange(
                new Rect(
                    renderSize));

            host.UpdateLayout();

            RenderTargetBitmap bitmap =
                new(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Pbgra32);

            bitmap.Render(
                host);

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }

        private static Material CreateMaterial(
            ImageSource? diffuseTexture)
        {
            MaterialGroup material =
                new();

            if (diffuseTexture !=
                null)
            {
                ImageBrush brush =
                    new(
                        diffuseTexture)
                    {
                        Stretch =
                            Stretch.Fill,

                        TileMode =
                            TileMode.None,

                        ViewportUnits =
                            BrushMappingMode.RelativeToBoundingBox
                    };

                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }

                material.Children.Add(
                    new DiffuseMaterial(
                        brush));
            }
            else
            {
                material.Children.Add(
                    new DiffuseMaterial(
                        new SolidColorBrush(
                            Color.FromRgb(
                                0x83,
                                0x94,
                                0x9D))));
            }

            material.Children.Add(
                new SpecularMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            70,
                            235,
                            245,
                            250)),
                    18));

            return material;
        }
    }
}
