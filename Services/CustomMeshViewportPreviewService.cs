using Ensemble.Controls;
using Ensemble.Models;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Ensemble.Services
{
    /// <summary>
    /// Keeps the editor viewport in sync with generated custom UGX geometry
    /// before the ERA is saved/reopened. The normal viewport resolves visuals
    /// from the currently-open ERA, so a newly generated UGX is not physically
    /// present there yet. This service temporarily replaces the borrowed stock
    /// visual with the exact geometry Ensemble just compiled.
    ///
    /// v35.6 also keeps a lightweight editor-side uniform scale for pending
    /// custom meshes. Scale is previewed as a WPF transform while dragging and
    /// is baked into the queued UGX only once, when the scale drag is committed.
    /// </summary>
    internal static class CustomMeshViewportPreviewService
    {
        private sealed class PreviewEntry
        {
            public required string ScenarioKey { get; init; }
            public required int ArtObjectId { get; init; }
            public required CustomMeshGeometryService.Geometry Geometry { get; init; }
            public required MeshGeometry3D Mesh { get; init; }
            public required Material Material { get; init; }
            public byte[] TemplateUgx { get; init; } = Array.Empty<byte>();
            public float Scale { get; set; } = 1.0f;
        }

        private static readonly object Sync = new();
        private static readonly List<PreviewEntry> Entries = new();

        public static void Register(
            string scenarioFile,
            int artObjectId,
            CustomMeshGeometryService.Geometry geometry,
            byte[]? templateUgx = null)
        {
            ArgumentNullException.ThrowIfNull(geometry);

            if (artObjectId <= 0)
                return;

            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            PreviewEntry entry =
                new PreviewEntry
                {
                    ScenarioKey = key,
                    ArtObjectId = artObjectId,
                    Geometry = geometry,
                    Mesh = BuildPreviewMesh(geometry),
                    Material = BuildPreviewMaterial(),
                    TemplateUgx = templateUgx?.ToArray() ?? Array.Empty<byte>(),
                    Scale = 1.0f
                };

            lock (Sync)
            {
                Entries.RemoveAll(
                    item =>
                        item.ArtObjectId == artObjectId &&
                        item.ScenarioKey.Equals(
                            key,
                            StringComparison.OrdinalIgnoreCase));

                Entries.Add(entry);
            }
        }

        public static void Remove(
            string scenarioFile,
            int artObjectId)
        {
            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            lock (Sync)
            {
                Entries.RemoveAll(
                    item =>
                        item.ArtObjectId == artObjectId &&
                        item.ScenarioKey.Equals(
                            key,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        public static bool HasPreview(
            string scenarioFile,
            int artObjectId)
        {
            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            lock (Sync)
            {
                return Entries.Any(
                    item =>
                        item.ArtObjectId == artObjectId &&
                        item.ScenarioKey.Equals(
                            key,
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        public static bool TryGetScale(
            string scenarioFile,
            int artObjectId,
            out float scale)
        {
            scale = 1.0f;

            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            lock (Sync)
            {
                PreviewEntry? entry =
                    Entries.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return false;

                scale = entry.Scale;
                return true;
            }
        }

        public static bool SetPreviewScale(
            string scenarioFile,
            int artObjectId,
            float scale)
        {
            if (!float.IsFinite(scale))
                return false;

            scale = Math.Clamp(scale, 0.05f, 20.0f);

            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            lock (Sync)
            {
                PreviewEntry? entry =
                    Entries.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return false;

                entry.Scale = scale;
                return true;
            }
        }

        public static bool CommitScale(
            string scenarioFile,
            int artObjectId,
            float scale,
            out int triangleCount,
            out int sectionCount)
        {
            triangleCount = 0;
            sectionCount = 0;

            if (!float.IsFinite(scale))
                return false;

            scale = Math.Clamp(scale, 0.05f, 20.0f);

            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            PreviewEntry? entry;

            lock (Sync)
            {
                entry =
                    Entries.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));
            }

            if (entry == null ||
                entry.TemplateUgx.Length == 0)
            {
                return false;
            }

            CustomMeshGeometryService.Geometry scaledGeometry =
                ScaleGeometry(
                    entry.Geometry,
                    scale);

            CustomUgxConversionService.Result conversion =
                CustomUgxConversionService.Convert(
                    entry.TemplateUgx,
                    scaledGeometry);

            if (!CustomMeshPendingAssetService.UpdateDataForArtObject(
                    scenarioFile,
                    artObjectId,
                    conversion.UgxData))
            {
                return false;
            }

            lock (Sync)
            {
                PreviewEntry? current =
                    Entries.LastOrDefault(
                        item =>
                            item.ArtObjectId == artObjectId &&
                            item.ScenarioKey.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

                if (current != null)
                    current.Scale = scale;
            }

            triangleCount = conversion.TriangleCount;
            sectionCount = conversion.SectionCount;
            return true;
        }

        public static void Apply(
            MapViewport3D viewport,
            string scenarioFile,
            IReadOnlyList<ScenarioArtObject> artObjects,
            object? selectedItem)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(artObjects);

            string key =
                CustomMeshPendingAssetService.NormalizeScenarioKey(
                    scenarioFile);

            Dictionary<int, PreviewEntry> previews;

            lock (Sync)
            {
                previews =
                    Entries
                        .Where(
                            item =>
                                item.ScenarioKey.Equals(
                                    key,
                                    StringComparison.OrdinalIgnoreCase))
                        .GroupBy(item => item.ArtObjectId)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Last());
            }

            if (previews.Count == 0)
                return;

            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.NonPublic;

            FieldInfo? objectsField =
                typeof(MapViewport3D)
                    .GetField("_objects", flags);

            FieldInfo? modelToItemField =
                typeof(MapViewport3D)
                    .GetField("_modelToItem", flags);

            FieldInfo? itemToModelsField =
                typeof(MapViewport3D)
                    .GetField("_itemToModels", flags);

            FieldInfo? baseMaterialsField =
                typeof(MapViewport3D)
                    .GetField("_baseMaterials", flags);

            if (objectsField?.GetValue(viewport)
                    is not Model3DGroup objects ||
                modelToItemField?.GetValue(viewport)
                    is not Dictionary<GeometryModel3D, object> modelToItem ||
                itemToModelsField?.GetValue(viewport)
                    is not Dictionary<object, List<GeometryModel3D>> itemToModels ||
                baseMaterialsField?.GetValue(viewport)
                    is not Dictionary<GeometryModel3D, Material> baseMaterials)
            {
                return;
            }

            foreach (ScenarioArtObject artObject in artObjects)
            {
                if (!previews.TryGetValue(
                        artObject.Id,
                        out PreviewEntry? preview))
                {
                    continue;
                }

                if (itemToModels.TryGetValue(
                        artObject,
                        out List<GeometryModel3D>? oldModels))
                {
                    foreach (GeometryModel3D oldModel in oldModels.ToArray())
                    {
                        objects.Children.Remove(oldModel);
                        modelToItem.Remove(oldModel);
                        baseMaterials.Remove(oldModel);
                    }

                    itemToModels.Remove(artObject);
                }

                GeometryModel3D model =
                    BuildPreviewModel(
                        preview,
                        artObject.Position,
                        artObject.Forward);

                objects.Children.Add(model);
                modelToItem[model] = artObject;
                baseMaterials[model] = preview.Material;
                itemToModels[artObject] =
                    new List<GeometryModel3D>
                    {
                        model
                    };
            }

            // Re-run the viewport's normal selection material logic so the
            // replacement geometry highlights exactly like stock objects.
            viewport.SelectItem(selectedItem);
        }

        private static MeshGeometry3D BuildPreviewMesh(
            CustomMeshGeometryService.Geometry geometry)
        {
            MeshGeometry3D mesh =
                new MeshGeometry3D();

            for (int i = 0;
                 i < geometry.TriangleVertices.Count;
                 i++)
            {
                CustomMeshGeometryService.Vertex vertex =
                    geometry.TriangleVertices[i];

                mesh.Positions.Add(
                    new Point3D(
                        vertex.Position.X,
                        vertex.Position.Y,
                        vertex.Position.Z));

                mesh.Normals.Add(
                    new Vector3D(
                        vertex.Normal.X,
                        vertex.Normal.Y,
                        vertex.Normal.Z));

                mesh.TextureCoordinates.Add(
                    new Point(
                        vertex.TexCoord.X,
                        vertex.TexCoord.Y));

                mesh.TriangleIndices.Add(i);
            }

            if (mesh.CanFreeze)
                mesh.Freeze();

            return mesh;
        }

        private static Material BuildPreviewMaterial()
        {
            MaterialGroup material =
                new MaterialGroup();

            SolidColorBrush diffuseBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x86,
                        0xA8,
                        0xB5));

            SolidColorBrush specularBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        0x45,
                        0x68,
                        0x75));

            if (diffuseBrush.CanFreeze)
                diffuseBrush.Freeze();

            if (specularBrush.CanFreeze)
                specularBrush.Freeze();

            material.Children.Add(
                new DiffuseMaterial(diffuseBrush));

            material.Children.Add(
                new SpecularMaterial(
                    specularBrush,
                    10));

            if (material.CanFreeze)
                material.Freeze();

            return material;
        }

        private static GeometryModel3D BuildPreviewModel(
            PreviewEntry preview,
            System.Numerics.Vector3 position,
            System.Numerics.Vector3 forward)
        {
            Transform3DGroup transform =
                new Transform3DGroup();

            if (Math.Abs(preview.Scale - 1.0f) > 0.00001f)
            {
                transform.Children.Add(
                    new ScaleTransform3D(
                        preview.Scale,
                        preview.Scale,
                        preview.Scale));
            }

            if (forward.LengthSquared() > 0.000001f)
            {
                double yaw =
                    Math.Atan2(
                        forward.X,
                        forward.Z)
                    * 180.0 /
                    Math.PI;

                transform.Children.Add(
                    new RotateTransform3D(
                        new AxisAngleRotation3D(
                            new Vector3D(0, 1, 0),
                            yaw)));
            }

            transform.Children.Add(
                new TranslateTransform3D(
                    position.X,
                    position.Y,
                    position.Z));

            return new GeometryModel3D(
                preview.Mesh,
                preview.Material)
            {
                BackMaterial = preview.Material,
                Transform = transform
            };
        }

        private static CustomMeshGeometryService.Geometry ScaleGeometry(
            CustomMeshGeometryService.Geometry source,
            float scale)
        {
            if (Math.Abs(scale - 1.0f) <= 0.00001f)
                return source;

            System.Numerics.Vector3 boundsMin =
                source.BoundsMin * scale;

            System.Numerics.Vector3 boundsMax =
                source.BoundsMax * scale;

            CustomMeshGeometryService.Geometry result =
                new CustomMeshGeometryService.Geometry
                {
                    BoundsMin =
                        System.Numerics.Vector3.Min(
                            boundsMin,
                            boundsMax),

                    BoundsMax =
                        System.Numerics.Vector3.Max(
                            boundsMin,
                            boundsMax),

                    BoundsRadius =
                        source.BoundsRadius * scale
                };

            foreach (CustomMeshGeometryService.Vertex vertex
                     in source.TriangleVertices)
            {
                result.TriangleVertices.Add(
                    new CustomMeshGeometryService.Vertex(
                        vertex.Position * scale,
                        vertex.Normal,
                        vertex.Tangent,
                        vertex.TexCoord));
            }

            return result;
        }
    }
}
