using Ensemble.Models;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NumericsVector3 = System.Numerics.Vector3;

namespace Ensemble.Controls
{
    /// <summary>
    /// Bridges the existing, proven MapCanvas terrain-sculpt implementation
    /// into the 3D viewport without duplicating the XTD edit / undo logic.
    ///
    /// MapCanvas originally owned the mouse capture because sculpting happened
    /// directly on the 2D canvas. The new 3D viewport sits above that canvas,
    /// so its mouse events never reach MapCanvas. This bridge starts and
    /// advances the same private sculpt stroke state without asking MapCanvas
    /// to capture the mouse; the 3D viewport keeps capture instead.
    /// </summary>
    internal static class ViewportTerrainSculptBridge
    {
        private const BindingFlags InstancePrivate =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        private static readonly FieldInfo? TerrainStrokeBeforeField =
            typeof(MapCanvas).GetField(
                "_terrainStrokeBefore",
                InstancePrivate);

        private static readonly FieldInfo? IsTerrainSculptingField =
            typeof(MapCanvas).GetField(
                "_isTerrainSculpting",
                InstancePrivate);

        private static readonly FieldInfo? LastTerrainStampPositionField =
            typeof(MapCanvas).GetField(
                "_lastTerrainStampPosition",
                InstancePrivate);

        private static readonly FieldInfo? TerrainBrushRadiusField =
            typeof(MapCanvas).GetField(
                "_terrainBrushRadius",
                InstancePrivate);

        private static readonly MethodInfo? ApplyTerrainBrushStampMethod =
            typeof(MapCanvas).GetMethod(
                "ApplyTerrainBrushStamp",
                InstancePrivate);

        private static readonly MethodInfo? FinishTerrainStrokeMethod =
            typeof(MapCanvas).GetMethod(
                "FinishTerrainStroke",
                InstancePrivate);

        private static readonly FieldInfo? WpfViewportField =
            typeof(MapViewport3D).GetField(
                "_viewport",
                InstancePrivate);

        private static readonly FieldInfo? RootModelField =
            typeof(MapViewport3D).GetField(
                "_root",
                InstancePrivate);

        public static bool IsStrokeInProgress(
            MapCanvas canvas)
        {
            return IsTerrainSculptingField?.GetValue(
                       canvas)
                   is bool active &&
                   active;
        }

        public static bool BeginStroke(
            MapCanvas canvas,
            float worldX,
            float worldZ)
        {
            if (!canvas.IsTerrainSculptActive ||
                canvas.TerrainHeightMap == null ||
                !IsInsideTerrain(
                    canvas,
                    worldX,
                    worldZ) ||
                TerrainStrokeBeforeField == null ||
                IsTerrainSculptingField == null ||
                LastTerrainStampPositionField == null ||
                ApplyTerrainBrushStampMethod == null)
            {
                return false;
            }

            if (TerrainStrokeBeforeField.GetValue(
                    canvas)
                is not Dictionary<int, float> strokeBefore)
            {
                return false;
            }

            strokeBefore.Clear();

            IsTerrainSculptingField.SetValue(
                canvas,
                true);

            LastTerrainStampPositionField.SetValue(
                canvas,
                new NumericsVector3(
                    worldX,
                    0,
                    worldZ));

            ApplyTerrainBrushStampMethod.Invoke(
                canvas,
                new object[]
                {
                    worldX,
                    worldZ
                });

            return true;
        }

        public static bool ContinueStroke(
            MapCanvas canvas,
            float worldX,
            float worldZ)
        {
            if (!IsStrokeInProgress(
                    canvas) ||
                !canvas.IsTerrainSculptActive ||
                canvas.TerrainHeightMap == null ||
                !IsInsideTerrain(
                    canvas,
                    worldX,
                    worldZ) ||
                LastTerrainStampPositionField == null ||
                TerrainBrushRadiusField == null ||
                ApplyTerrainBrushStampMethod == null)
            {
                return false;
            }

            if (LastTerrainStampPositionField.GetValue(
                    canvas)
                is not NumericsVector3 lastStamp ||
                TerrainBrushRadiusField.GetValue(
                    canvas)
                is not float radius)
            {
                return false;
            }

            NumericsVector3 currentStamp =
                new NumericsVector3(
                    worldX,
                    0,
                    worldZ);

            float minimumStampDistance =
                Math.Max(
                    1.0f,
                    radius *
                    0.15f);

            if (NumericsVector3.Distance(
                    currentStamp,
                    lastStamp) <
                minimumStampDistance)
            {
                return false;
            }

            LastTerrainStampPositionField.SetValue(
                canvas,
                currentStamp);

            ApplyTerrainBrushStampMethod.Invoke(
                canvas,
                new object[]
                {
                    worldX,
                    worldZ
                });

            return true;
        }

        public static void EndStroke(
            MapCanvas canvas)
        {
            if (!IsStrokeInProgress(
                    canvas) ||
                FinishTerrainStrokeMethod == null)
            {
                return;
            }

            // The 3D viewport owns mouse capture, so tell the existing stroke
            // finalizer not to release MapCanvas capture. It still creates the
            // normal terrain undo record and raises TerrainPreviewChanged.
            FinishTerrainStrokeMethod.Invoke(
                canvas,
                new object[]
                {
                    false
                });
        }

        public static bool TryGetTerrainPoint(
            MapViewport3D viewport,
            Point pointInViewportControl,
            out NumericsVector3 worldPoint)
        {
            worldPoint =
                default;

            if (WpfViewportField?.GetValue(
                    viewport)
                    is not Viewport3D wpfViewport ||
                RootModelField?.GetValue(
                    viewport)
                    is not Model3DGroup root)
            {
                return false;
            }

            GeometryModel3D? terrainModel =
                root.Children
                    .OfType<GeometryModel3D>()
                    .FirstOrDefault();

            if (terrainModel ==
                null)
            {
                return false;
            }

            Point point =
                viewport.TranslatePoint(
                    pointInViewportControl,
                    wpfViewport);

            RayMeshGeometry3DHitTestResult?
                terrainHit =
                    null;

            VisualTreeHelper.HitTest(
                wpfViewport,
                null,
                result =>
                {
                    if (result
                            is RayMeshGeometry3DHitTestResult ray &&
                        ReferenceEquals(
                            ray.ModelHit,
                            terrainModel))
                    {
                        terrainHit =
                            ray;

                        return HitTestResultBehavior.Stop;
                    }

                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(
                    point));

            if (terrainHit ==
                null)
            {
                return false;
            }

            Point3D hit =
                terrainHit.PointHit;

            if (!double.IsFinite(
                    hit.X) ||
                !double.IsFinite(
                    hit.Y) ||
                !double.IsFinite(
                    hit.Z))
            {
                return false;
            }

            worldPoint =
                new NumericsVector3(
                    (float)hit.X,
                    (float)hit.Y,
                    (float)hit.Z);

            return true;
        }

        private static bool IsInsideTerrain(
            MapCanvas canvas,
            float worldX,
            float worldZ)
        {
            TerrainHeightMap? terrain =
                canvas.TerrainHeightMap;

            if (terrain ==
                null)
            {
                return false;
            }

            return
                worldX >=
                    terrain.WorldMin.X &&
                worldX <=
                    terrain.WorldMax.X &&
                worldZ >=
                    terrain.WorldMin.Z &&
                worldZ <=
                    terrain.WorldMax.Z;
        }
    }
}
