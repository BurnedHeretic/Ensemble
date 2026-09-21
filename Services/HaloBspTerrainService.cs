using Ensemble.Models;
using System.Numerics;

namespace Ensemble.Services
{
    internal enum HaloTerrainSurfaceMode
    {
        LowestWalkable,
        HighestWalkable,
        NearestToTemplate
    }

    /// <summary>
    /// Converts the already-scaled / centred Halo BSP geometry used by the
    /// custom-UGX compiler into the currently-open Halo Wars XTD heightfield.
    ///
    /// Halo FPS BSPs can contain caves, ceilings, bridges and stacked floors;
    /// an RTS XTD is a single height per X/Z sample. The user therefore chooses
    /// which walkable envelope to project. Near-vertical faces are ignored.
    /// </summary>
    internal static class HaloBspTerrainService
    {
        internal sealed class Result
        {
            public required float[] Heights { get; init; }
            public int CoveredVertices { get; init; }
            public int ChangedVertices { get; init; }
            public int ClampedVertices { get; init; }
            public int TriangleCountConsidered { get; init; }
            public int TriangleCountRejectedBySlope { get; init; }
            public float MinGeneratedHeight { get; init; }
            public float MaxGeneratedHeight { get; init; }
        }

        public static Result Generate(
            CustomMeshGeometryService.Geometry geometry,
            TerrainHeightMap target,
            Vector3 placementPosition,
            Vector3 placementForward,
            HaloTerrainSurfaceMode mode,
            float maxSlopeDegrees,
            int smoothingPasses)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            ArgumentNullException.ThrowIfNull(target);

            if (geometry.TriangleVertices.Count < 3)
                throw new InvalidDataException(
                    "The imported Halo BSP contains no triangle geometry.");

            if (target.Width <= 0 ||
                target.Height <= 0 ||
                target.Heights.Length !=
                    checked(target.Width * target.Height))
            {
                throw new InvalidDataException(
                    "The target Halo Wars terrain heightfield is invalid.");
            }

            if (!float.IsFinite(target.TileScale) ||
                target.TileScale <= 0)
            {
                throw new InvalidDataException(
                    "The target Halo Wars terrain has an invalid tile scale.");
            }

            maxSlopeDegrees =
                Math.Clamp(
                    maxSlopeDegrees,
                    5.0f,
                    89.0f);

            smoothingPasses =
                Math.Clamp(
                    smoothingPasses,
                    0,
                    8);

            float minNormalY =
                MathF.Cos(
                    maxSlopeDegrees *
                    (MathF.PI / 180.0f));

            float yaw =
                placementForward.LengthSquared() >
                    0.000001f
                    ? MathF.Atan2(
                        placementForward.X,
                        placementForward.Z)
                    : 0.0f;

            float sin =
                MathF.Sin(yaw);

            float cos =
                MathF.Cos(yaw);

            Vector3 Transform(
                Vector3 local)
            {
                float rx =
                    local.X * cos +
                    local.Z * sin;

                float rz =
                    -local.X * sin +
                    local.Z * cos;

                return new Vector3(
                    rx + placementPosition.X,
                    local.Y + placementPosition.Y,
                    rz + placementPosition.Z);
            }

            int count =
                target.Heights.Length;

            float[] generated =
                target.Heights.ToArray();

            float[] candidate =
                new float[count];

            float[] candidateScore =
                new float[count];

            bool[] covered =
                new bool[count];

            Array.Fill(
                candidate,
                float.NaN);

            Array.Fill(
                candidateScore,
                float.PositiveInfinity);

            int considered =
                0;

            int rejectedBySlope =
                0;

            const float barycentricTolerance =
                0.00025f;

            for (int tri = 0;
                 tri + 2 < geometry.TriangleVertices.Count;
                 tri += 3)
            {
                Vector3 a =
                    Transform(
                        geometry.TriangleVertices[tri]
                            .Position);

                Vector3 b =
                    Transform(
                        geometry.TriangleVertices[tri + 1]
                            .Position);

                Vector3 c =
                    Transform(
                        geometry.TriangleVertices[tri + 2]
                            .Position);

                Vector3 normal =
                    Vector3.Cross(
                        b - a,
                        c - a);

                float normalLength =
                    normal.Length();

                if (!float.IsFinite(normalLength) ||
                    normalLength <
                        0.000001f)
                {
                    continue;
                }

                float upwardness =
                    MathF.Abs(
                        normal.Y) /
                    normalLength;

                if (upwardness <
                    minNormalY)
                {
                    rejectedBySlope++;
                    continue;
                }

                float minX =
                    MathF.Min(
                        a.X,
                        MathF.Min(
                            b.X,
                            c.X));

                float maxX =
                    MathF.Max(
                        a.X,
                        MathF.Max(
                            b.X,
                            c.X));

                float minZ =
                    MathF.Min(
                        a.Z,
                        MathF.Min(
                            b.Z,
                            c.Z));

                float maxZ =
                    MathF.Max(
                        a.Z,
                        MathF.Max(
                            b.Z,
                            c.Z));

                int minGridX =
                    Math.Clamp(
                        (int)MathF.Floor(
                            (minX -
                             target.WorldMin.X) /
                            target.TileScale),
                        0,
                        target.Width - 1);

                int maxGridX =
                    Math.Clamp(
                        (int)MathF.Ceiling(
                            (maxX -
                             target.WorldMin.X) /
                            target.TileScale),
                        0,
                        target.Width - 1);

                int minGridZ =
                    Math.Clamp(
                        (int)MathF.Floor(
                            (minZ -
                             target.WorldMin.Z) /
                            target.TileScale),
                        0,
                        target.Height - 1);

                int maxGridZ =
                    Math.Clamp(
                        (int)MathF.Ceiling(
                            (maxZ -
                             target.WorldMin.Z) /
                            target.TileScale),
                        0,
                        target.Height - 1);

                if (maxGridX <
                        minGridX ||
                    maxGridZ <
                        minGridZ)
                {
                    continue;
                }

                float denominator =
                    (b.Z - c.Z) *
                        (a.X - c.X)
                    +
                    (c.X - b.X) *
                        (a.Z - c.Z);

                if (!float.IsFinite(
                        denominator) ||
                    MathF.Abs(
                        denominator) <
                        0.000001f)
                {
                    continue;
                }

                considered++;

                for (int z = minGridZ;
                     z <= maxGridZ;
                     z++)
                {
                    float worldZ =
                        target.WorldMin.Z +
                        z *
                        target.TileScale;

                    for (int x = minGridX;
                         x <= maxGridX;
                         x++)
                    {
                        float worldX =
                            target.WorldMin.X +
                            x *
                            target.TileScale;

                        float w1 =
                            (
                                (b.Z - c.Z) *
                                    (worldX - c.X)
                                +
                                (c.X - b.X) *
                                    (worldZ - c.Z)
                            ) /
                            denominator;

                        float w2 =
                            (
                                (c.Z - a.Z) *
                                    (worldX - c.X)
                                +
                                (a.X - c.X) *
                                    (worldZ - c.Z)
                            ) /
                            denominator;

                        float w3 =
                            1.0f -
                            w1 -
                            w2;

                        if (w1 <
                                -barycentricTolerance ||
                            w2 <
                                -barycentricTolerance ||
                            w3 <
                                -barycentricTolerance)
                        {
                            continue;
                        }

                        float y =
                            w1 * a.Y +
                            w2 * b.Y +
                            w3 * c.Y;

                        if (!float.IsFinite(
                                y))
                        {
                            continue;
                        }

                        int index =
                            checked(
                                z *
                                target.Width +
                                x);

                        bool accept;

                        switch (mode)
                        {
                            case HaloTerrainSurfaceMode.HighestWalkable:
                                accept =
                                    !covered[index] ||
                                    y >
                                    candidate[index];
                                break;

                            case HaloTerrainSurfaceMode.NearestToTemplate:
                                float score =
                                    MathF.Abs(
                                        y -
                                        target.Heights[index]);

                                accept =
                                    !covered[index] ||
                                    score <
                                    candidateScore[index];

                                if (accept)
                                {
                                    candidateScore[index] =
                                        score;
                                }
                                break;

                            default:
                                accept =
                                    !covered[index] ||
                                    y <
                                    candidate[index];
                                break;
                        }

                        if (!accept)
                            continue;

                        candidate[index] =
                            y;

                        covered[index] =
                            true;
                    }
                }
            }

            int coveredCount =
                covered.Count(
                    value =>
                        value);

            if (coveredCount ==
                0)
            {
                throw new InvalidDataException(
                    "The Halo BSP did not overlap any target XTD terrain samples. " +
                    "Check the import scale / map coverage.");
            }

            int clamped =
                0;

            float minGenerated =
                float.PositiveInfinity;

            float maxGenerated =
                float.NegativeInfinity;

            for (int i = 0;
                 i < count;
                 i++)
            {
                if (!covered[i])
                    continue;

                float value =
                    candidate[i];

                float clampedValue =
                    Math.Clamp(
                        value,
                        target.EncodableMinHeight,
                        target.EncodableMaxHeight);

                if (MathF.Abs(
                        value -
                        clampedValue) >
                    0.0001f)
                {
                    clamped++;
                }

                generated[i] =
                    QuantizeHeight(
                        clampedValue,
                        target);

                minGenerated =
                    MathF.Min(
                        minGenerated,
                        generated[i]);

                maxGenerated =
                    MathF.Max(
                        maxGenerated,
                        generated[i]);
            }

            for (int pass = 0;
                 pass < smoothingPasses;
                 pass++)
            {
                generated =
                    SmoothCovered(
                        generated,
                        covered,
                        target.Width,
                        target.Height,
                        target);
            }

            int changed =
                0;

            for (int i = 0;
                 i < count;
                 i++)
            {
                if (MathF.Abs(
                        generated[i] -
                        target.Heights[i]) >
                    0.000001f)
                {
                    changed++;
                }
            }

            if (!float.IsFinite(
                    minGenerated))
            {
                minGenerated =
                    target.MinHeight;
            }

            if (!float.IsFinite(
                    maxGenerated))
            {
                maxGenerated =
                    target.MaxHeight;
            }

            return new Result
            {
                Heights =
                    generated,

                CoveredVertices =
                    coveredCount,

                ChangedVertices =
                    changed,

                ClampedVertices =
                    clamped,

                TriangleCountConsidered =
                    considered,

                TriangleCountRejectedBySlope =
                    rejectedBySlope,

                MinGeneratedHeight =
                    minGenerated,

                MaxGeneratedHeight =
                    maxGenerated
            };
        }

        public static float ChoosePlacementGroundHeight(
            TerrainHeightMap terrain)
        {
            ArgumentNullException.ThrowIfNull(
                terrain);

            List<float> values =
                terrain.Heights
                    .Where(
                        float.IsFinite)
                    .OrderBy(
                        value =>
                            value)
                    .ToList();

            if (values.Count ==
                0)
            {
                return 0.0f;
            }

            int index =
                Math.Clamp(
                    (int)MathF.Round(
                        (values.Count - 1) *
                        0.10f),
                    0,
                    values.Count - 1);

            return QuantizeHeight(
                Math.Clamp(
                    values[index],
                    terrain.EncodableMinHeight,
                    terrain.EncodableMaxHeight),
                terrain);
        }

        private static float[] SmoothCovered(
            float[] source,
            bool[] covered,
            int width,
            int height,
            TerrainHeightMap terrain)
        {
            float[] result =
                source.ToArray();

            for (int z = 0;
                 z < height;
                 z++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int index =
                        z *
                        width +
                        x;

                    if (!covered[index])
                        continue;

                    float sum =
                        source[index] *
                        2.0f;

                    float weight =
                        2.0f;

                    for (int dz = -1;
                         dz <= 1;
                         dz++)
                    {
                        for (int dx = -1;
                             dx <= 1;
                             dx++)
                        {
                            if (dx == 0 &&
                                dz == 0)
                            {
                                continue;
                            }

                            int nx =
                                x +
                                dx;

                            int nz =
                                z +
                                dz;

                            if (nx < 0 ||
                                nx >= width ||
                                nz < 0 ||
                                nz >= height)
                            {
                                continue;
                            }

                            int neighbour =
                                nz *
                                width +
                                nx;

                            if (!covered[neighbour])
                                continue;

                            sum +=
                                source[neighbour];

                            weight +=
                                1.0f;
                        }
                    }

                    float average =
                        sum /
                        Math.Max(
                            1.0f,
                            weight);

                    // Keep most of the original shape so one smoothing pass
                    // does not erase Halo cliffs or ramps.
                    float blended =
                        source[index] *
                            0.65f +
                        average *
                            0.35f;

                    result[index] =
                        QuantizeHeight(
                            Math.Clamp(
                                blended,
                                terrain.EncodableMinHeight,
                                terrain.EncodableMaxHeight),
                            terrain);
                }
            }

            return result;
        }

        private static float QuantizeHeight(
            float value,
            TerrainHeightMap terrain)
        {
            if (!float.IsFinite(
                    terrain.PositionCompressionRange.Y) ||
                terrain.PositionCompressionRange.Y <=
                    0)
            {
                return value;
            }

            float normalized =
                (
                    value +
                    terrain.PositionCompressionMin.Y
                ) /
                terrain.PositionCompressionRange.Y;

            normalized =
                Math.Clamp(
                    normalized,
                    0.0f,
                    1.0f);

            int encoded =
                Math.Clamp(
                    (int)MathF.Round(
                        normalized *
                        1023.0f),
                    0,
                    1023);

            return
                encoded /
                    1023.0f *
                    terrain.PositionCompressionRange.Y
                -
                terrain.PositionCompressionMin.Y;
        }
    }
}
