using Ensemble.Controls;
using Ensemble.Models;
using System.Reflection;

namespace Ensemble.Services
{
    /// <summary>
    /// Keeps the target map's already-loaded terrain attached when Ensemble
    /// performs an in-place ScenarioMap refresh.
    ///
    /// MainWindow.EnsembleNext refreshes the scenario object layer after a
    /// cross-ERA SCN import by calling MapCanvas.SetMap with the SAME map
    /// instance. SetMap was originally written for opening a new map, so it
    /// clears the XTD/XTT preview fields. The terrain undo stack itself is not
    /// cleared, which leaves the document dirty but without a TerrainHeightMap
    /// and causes SaveModifiedEraToPath to reject the save.
    ///
    /// This bridge snapshots only the visual terrain fields that SetMap clears
    /// and restores them only when the exact same ScenarioMap instance is
    /// still active. A genuinely newly-opened map therefore cannot inherit the
    /// previous map's terrain.
    /// </summary>
    internal static class MapCanvasTerrainStateService
    {
        private const BindingFlags InstancePrivate =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        private static readonly FieldInfo? TerrainHeightMapField =
            typeof(MapCanvas).GetField(
                "_terrainHeightMap",
                InstancePrivate);

        private static readonly FieldInfo? TerrainHeightBitmapField =
            typeof(MapCanvas).GetField(
                "_terrainHeightBitmap",
                InstancePrivate);

        private static readonly FieldInfo? TerrainTextureMapField =
            typeof(MapCanvas).GetField(
                "_terrainTextureMap",
                InstancePrivate);

        private static readonly FieldInfo? TerrainTextureBitmapField =
            typeof(MapCanvas).GetField(
                "_terrainTextureBitmap",
                InstancePrivate);

        private static readonly MethodInfo? RenderMapMethod =
            typeof(MapCanvas).GetMethod(
                "RenderMap",
                InstancePrivate);

        internal sealed class Snapshot
        {
            public required ScenarioMap OwnerMap
            {
                get;
                init;
            }

            public object? TerrainHeightMap
            {
                get;
                init;
            }

            public object? TerrainHeightBitmap
            {
                get;
                init;
            }

            public object? TerrainTextureMap
            {
                get;
                init;
            }

            public object? TerrainTextureBitmap
            {
                get;
                init;
            }
        }

        public static Snapshot? Capture(
            MapCanvas canvas)
        {
            ArgumentNullException.ThrowIfNull(
                canvas);

            ScenarioMap? map =
                canvas.Scenario;

            if (map ==
                null)
            {
                return null;
            }

            object? heightMap =
                TerrainHeightMapField?.GetValue(
                    canvas);

            object? heightBitmap =
                TerrainHeightBitmapField?.GetValue(
                    canvas);

            object? textureMap =
                TerrainTextureMapField?.GetValue(
                    canvas);

            object? textureBitmap =
                TerrainTextureBitmapField?.GetValue(
                    canvas);

            if (heightMap ==
                    null &&
                heightBitmap ==
                    null &&
                textureMap ==
                    null &&
                textureBitmap ==
                    null)
            {
                return null;
            }

            return new Snapshot
            {
                OwnerMap =
                    map,

                TerrainHeightMap =
                    heightMap,

                TerrainHeightBitmap =
                    heightBitmap,

                TerrainTextureMap =
                    textureMap,

                TerrainTextureBitmap =
                    textureBitmap
            };
        }

        public static bool RestoreIfCleared(
            MapCanvas canvas,
            Snapshot? snapshot)
        {
            ArgumentNullException.ThrowIfNull(
                canvas);

            if (snapshot ==
                    null ||
                !ReferenceEquals(
                    canvas.Scenario,
                    snapshot.OwnerMap))
            {
                return false;
            }

            bool restored =
                false;

            restored |= RestoreMissing(
                TerrainHeightMapField,
                canvas,
                snapshot.TerrainHeightMap);

            restored |= RestoreMissing(
                TerrainHeightBitmapField,
                canvas,
                snapshot.TerrainHeightBitmap);

            restored |= RestoreMissing(
                TerrainTextureMapField,
                canvas,
                snapshot.TerrainTextureMap);

            restored |= RestoreMissing(
                TerrainTextureBitmapField,
                canvas,
                snapshot.TerrainTextureBitmap);

            if (restored)
            {
                RenderMapMethod?.Invoke(
                    canvas,
                    null);
            }

            return restored;
        }

        private static bool RestoreMissing(
            FieldInfo? field,
            MapCanvas canvas,
            object? savedValue)
        {
            if (field ==
                    null ||
                savedValue ==
                    null ||
                field.GetValue(
                    canvas) !=
                    null)
            {
                return false;
            }

            field.SetValue(
                canvas,
                savedValue);

            return true;
        }
    }
}
