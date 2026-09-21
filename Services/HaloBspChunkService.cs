using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Ensemble.Services
{
    /// <summary>
    /// Spatially partitions an Ensemble-generated Halo OBJ into compact local
    /// meshes suitable for Halo Wars object culling.
    ///
    /// The original Halo BSP importer deliberately keeps one editable ArtObject
    /// while the creator positions / rotates / scales the source Halo environment.
    /// Once finalized, this service splits that local mesh into grid cells and
    /// recentres every cell around its own local origin. MainWindow then places
    /// each cell at the equivalent transformed world position.
    ///
    /// Giving each generated UGX an origin close to its own geometry is the
    /// important part: a map-sized object with one distant origin can disappear
    /// when Halo Wars performs normal object-distance/frustum culling.
    /// </summary>
    internal static class HaloBspChunkService
    {
        internal sealed class Chunk
        {
            public required string ObjPath
            {
                get;
                init;
            }

            public int CellX
            {
                get;
                init;
            }

            public int CellZ
            {
                get;
                init;
            }

            public int TriangleCount
            {
                get;
                init;
            }

            public int VertexCount
            {
                get;
                init;
            }

            /// <summary>
            /// Offset from the original BSP object's bottom-centre local
            /// pivot to this chunk's bottom-centre local pivot, after the
            /// currently committed uniform custom-mesh scale has been applied.
            /// </summary>
            public Vector3 LocalOrigin
            {
                get;
                init;
            }
        }

        private readonly record struct FaceVertex(
            int PositionIndex,
            int NormalIndex);

        private readonly record struct Triangle(
            FaceVertex A,
            FaceVertex B,
            FaceVertex C);

        public static IReadOnlyList<Chunk> SplitGeneratedHaloObj(
            string sourceObjPath,
            string outputDirectory,
            int cellsPerAxis,
            float uniformScale)
        {
            if (string.IsNullOrWhiteSpace(
                    sourceObjPath) ||
                !File.Exists(
                    sourceObjPath))
            {
                throw new FileNotFoundException(
                    "The imported Halo BSP OBJ could not be found.",
                    sourceObjPath);
            }

            if (cellsPerAxis < 2 ||
                cellsPerAxis > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellsPerAxis),
                    "Halo BSP optimization supports 2-8 spatial cells per axis.");
            }

            if (!float.IsFinite(
                    uniformScale) ||
                uniformScale <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(uniformScale));
            }

            Directory.CreateDirectory(
                outputDirectory);

            List<Vector3> positions =
                new();

            List<Vector3> normals =
                new();

            ReadPositionsAndNormals(
                sourceObjPath,
                uniformScale,
                positions,
                normals);

            if (positions.Count == 0)
            {
                throw new InvalidDataException(
                    "The Halo BSP OBJ contains no vertices.");
            }

            float minX =
                positions.Min(
                    value => value.X);

            float maxX =
                positions.Max(
                    value => value.X);

            float minZ =
                positions.Min(
                    value => value.Z);

            float maxZ =
                positions.Max(
                    value => value.Z);

            float minY =
                positions.Min(
                    value => value.Y);

            // CustomMeshGeometryService compiles the editable monolithic BSP
            // around a bottom-centre pivot. Spatial chunks must therefore use
            // offsets RELATIVE TO THAT SAME SOURCE PIVOT, not absolute source-game
            // coordinates. The older optimizer used absolute chunk centres,
            // which could move finalized cells far away from the placed BSP.
            Vector3 sourcePivot =
                new Vector3(
                    (minX + maxX) * 0.5f,
                    minY,
                    (minZ + maxZ) * 0.5f);

            float width =
                Math.Max(
                    0.0001f,
                    maxX - minX);

            float depth =
                Math.Max(
                    0.0001f,
                    maxZ - minZ);

            List<Triangle>[] cells =
                Enumerable.Range(
                        0,
                        checked(
                            cellsPerAxis *
                            cellsPerAxis))
                    .Select(
                        _ =>
                            new List<Triangle>())
                    .ToArray();

            ReadAndPartitionFaces(
                sourceObjPath,
                positions,
                normals.Count,
                minX,
                minZ,
                width,
                depth,
                cellsPerAxis,
                cells);

            string baseName =
                Path.GetFileNameWithoutExtension(
                    sourceObjPath);

            List<Chunk> result =
                new();

            for (int z = 0;
                 z < cellsPerAxis;
                 z++)
            {
                for (int x = 0;
                     x < cellsPerAxis;
                     x++)
                {
                    List<Triangle> triangles =
                        cells[
                            z *
                            cellsPerAxis +
                            x];

                    if (triangles.Count == 0)
                        continue;

                    string path =
                        Path.Combine(
                            outputDirectory,
                            $"{baseName}_chunk_{x:D2}_{z:D2}.obj");

                    Chunk chunk =
                        WriteChunk(
                            path,
                            x,
                            z,
                            triangles,
                            positions,
                            normals,
                            sourcePivot);

                    result.Add(
                        chunk);
                }
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException(
                    "Halo BSP spatial partitioning produced no non-empty chunks.");
            }

            return result;
        }

        public static bool IsEnsembleHaloObj(
            string path)
        {
            if (string.IsNullOrWhiteSpace(
                    path) ||
                !File.Exists(
                    path))
            {
                return false;
            }

            try
            {
                foreach (string line
                         in File.ReadLines(
                                 path)
                             .Take(12))
                {
                    string marker =
                        line.Trim();

                    if (marker.StartsWith(
                            "# ENSHALOOBJ ",
                            StringComparison.Ordinal) &&
                        !marker.StartsWith(
                            "# ENSHALOOBJCHUNK",
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void ReadPositionsAndNormals(
            string path,
            float scale,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            foreach (string raw
                     in File.ReadLines(
                         path))
            {
                if (raw.StartsWith(
                        "v ",
                        StringComparison.Ordinal))
                {
                    Vector3 value =
                        ParseVector3Line(
                            raw,
                            "vertex");

                    positions.Add(
                        value *
                        scale);

                    continue;
                }

                if (raw.StartsWith(
                        "vn ",
                        StringComparison.Ordinal))
                {
                    Vector3 value =
                        ParseVector3Line(
                            raw,
                            "normal");

                    if (!float.IsFinite(
                            value.X) ||
                        !float.IsFinite(
                            value.Y) ||
                        !float.IsFinite(
                            value.Z) ||
                        value.LengthSquared() <
                            0.000001f)
                    {
                        value =
                            Vector3.UnitY;
                    }
                    else
                    {
                        value =
                            Vector3.Normalize(
                                value);
                    }

                    normals.Add(
                        value);
                }
            }
        }

        private static Vector3 ParseVector3Line(
            string raw,
            string label)
        {
            string[] parts =
                raw.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 4 ||
                !float.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float x) ||
                !float.TryParse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float y) ||
                !float.TryParse(
                    parts[3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float z) ||
                !float.IsFinite(x) ||
                !float.IsFinite(y) ||
                !float.IsFinite(z))
            {
                throw new InvalidDataException(
                    $"The generated Halo OBJ contains an invalid {label} line.");
            }

            return new Vector3(
                x,
                y,
                z);
        }

        private static void ReadAndPartitionFaces(
            string path,
            IReadOnlyList<Vector3> positions,
            int normalCount,
            float minX,
            float minZ,
            float width,
            float depth,
            int cellsPerAxis,
            IReadOnlyList<List<Triangle>> cells)
        {
            foreach (string raw
                     in File.ReadLines(
                         path))
            {
                if (!raw.StartsWith(
                        "f ",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string[] tokens =
                    raw.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length < 4)
                    continue;

                List<FaceVertex> polygon =
                    new();

                for (int i = 1;
                     i < tokens.Length;
                     i++)
                {
                    polygon.Add(
                        ParseFaceVertex(
                            tokens[i],
                            positions.Count,
                            normalCount));
                }

                for (int i = 1;
                     i < polygon.Count - 1;
                     i++)
                {
                    Triangle triangle =
                        new Triangle(
                            polygon[0],
                            polygon[i],
                            polygon[i + 1]);

                    Vector3 a =
                        positions[
                            triangle.A.PositionIndex];

                    Vector3 b =
                        positions[
                            triangle.B.PositionIndex];

                    Vector3 c =
                        positions[
                            triangle.C.PositionIndex];

                    Vector3 centroid =
                        (
                            a +
                            b +
                            c
                        ) /
                        3.0f;

                    int cellX =
                        Math.Clamp(
                            (int)MathF.Floor(
                                (
                                    centroid.X -
                                    minX
                                ) /
                                width *
                                cellsPerAxis),
                            0,
                            cellsPerAxis - 1);

                    int cellZ =
                        Math.Clamp(
                            (int)MathF.Floor(
                                (
                                    centroid.Z -
                                    minZ
                                ) /
                                depth *
                                cellsPerAxis),
                            0,
                            cellsPerAxis - 1);

                    cells[
                            cellZ *
                            cellsPerAxis +
                            cellX]
                        .Add(
                            triangle);
                }
            }
        }

        private static FaceVertex ParseFaceVertex(
            string token,
            int positionCount,
            int normalCount)
        {
            string[] parts =
                token.Split('/');

            if (parts.Length == 0 ||
                !int.TryParse(
                    parts[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rawPosition))
            {
                throw new InvalidDataException(
                    "The generated Halo OBJ contains an invalid face index.");
            }

            int positionIndex =
                ResolveObjIndex(
                    rawPosition,
                    positionCount);

            int normalIndex =
                -1;

            if (parts.Length >= 3 &&
                !string.IsNullOrWhiteSpace(
                    parts[2]) &&
                int.TryParse(
                    parts[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rawNormal))
            {
                normalIndex =
                    ResolveObjIndex(
                        rawNormal,
                        normalCount);
            }
            else if (positionIndex <
                     normalCount)
            {
                // Ensemble's native Halo OBJ writer emits one normal per
                // vertex and uses matching indices.
                normalIndex =
                    positionIndex;
            }

            return new FaceVertex(
                positionIndex,
                normalIndex);
        }

        private static int ResolveObjIndex(
            int raw,
            int count)
        {
            int index =
                raw > 0
                    ? raw - 1
                    : count + raw;

            if (index < 0 ||
                index >= count)
            {
                throw new InvalidDataException(
                    "The generated Halo OBJ contains a face index outside its vertex/normal table.");
            }

            return index;
        }

        private static Chunk WriteChunk(
            string path,
            int cellX,
            int cellZ,
            IReadOnlyList<Triangle> triangles,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals,
            Vector3 sourcePivot)
        {
            HashSet<int> positionIndices =
                new();

            HashSet<int> normalIndices =
                new();

            foreach (Triangle triangle
                     in triangles)
            {
                AddUsed(
                    triangle.A,
                    positionIndices,
                    normalIndices);

                AddUsed(
                    triangle.B,
                    positionIndices,
                    normalIndices);

                AddUsed(
                    triangle.C,
                    positionIndices,
                    normalIndices);
            }

            Vector3 boundsMin =
                new Vector3(
                    float.PositiveInfinity);

            Vector3 boundsMax =
                new Vector3(
                    float.NegativeInfinity);

            foreach (int index
                     in positionIndices)
            {
                Vector3 value =
                    positions[index];

                boundsMin =
                    Vector3.Min(
                        boundsMin,
                        value);

                boundsMax =
                    Vector3.Max(
                        boundsMax,
                        value);
            }

            // Match CustomMeshGeometryService's bottom-centre pivot rule.
            // If we used the 3D bounds centre here, importing this generated
            // chunk would bottom-centre it a second time and its vertical world
            // placement would drift. Keeping the OBJ pivot at bottom-centre
            // makes the compiler's normalization a no-op.
            Vector3 chunkPivot =
                new Vector3(
                    (boundsMin.X + boundsMax.X) * 0.5f,
                    boundsMin.Y,
                    (boundsMin.Z + boundsMax.Z) * 0.5f);

            Vector3 localOrigin =
                chunkPivot -
                sourcePivot;

            List<int> orderedPositions =
                positionIndices
                    .OrderBy(
                        value => value)
                    .ToList();

            List<int> orderedNormals =
                normalIndices
                    .OrderBy(
                        value => value)
                    .ToList();

            bool needsFallbackNormal =
                triangles.Any(
                    triangle =>
                        triangle.A.NormalIndex < 0 ||
                        triangle.B.NormalIndex < 0 ||
                        triangle.C.NormalIndex < 0);

            Dictionary<int, int> positionMap =
                new();

            Dictionary<int, int> normalMap =
                new();

            for (int i = 0;
                 i < orderedPositions.Count;
                 i++)
            {
                positionMap[
                    orderedPositions[i]] =
                        i + 1;
            }

            for (int i = 0;
                 i < orderedNormals.Count;
                 i++)
            {
                normalMap[
                    orderedNormals[i]] =
                        i + 1;
            }

            int fallbackNormalIndex =
                orderedNormals.Count +
                1;

            using StreamWriter writer =
                new StreamWriter(
                    path,
                    append: false,
                    new UTF8Encoding(false),
                    bufferSize: 1024 * 1024);

            writer.WriteLine(
                "# ENSHALOOBJCHUNK 1");

            writer.WriteLine(
                $"# Cell {cellX},{cellZ}");

            writer.WriteLine(
                "# Spatially optimized by Ensemble; local origin moved near this chunk for Halo Wars culling.");

            writer.WriteLine(
                $"# LocalOrigin {Format(localOrigin.X)} {Format(localOrigin.Y)} {Format(localOrigin.Z)}");

            foreach (int oldIndex
                     in orderedPositions)
            {
                Vector3 value =
                    positions[oldIndex] -
                    chunkPivot;

                writer.Write("v ");
                writer.Write(Format(value.X));
                writer.Write(' ');
                writer.Write(Format(value.Y));
                writer.Write(' ');
                writer.WriteLine(Format(value.Z));
            }

            foreach (int oldIndex
                     in orderedNormals)
            {
                Vector3 value =
                    normals[oldIndex];

                writer.Write("vn ");
                writer.Write(Format(value.X));
                writer.Write(' ');
                writer.Write(Format(value.Y));
                writer.Write(' ');
                writer.WriteLine(Format(value.Z));
            }

            if (needsFallbackNormal)
            {
                writer.WriteLine(
                    "vn 0 1 0");
            }

            writer.WriteLine(
                $"g halo_bsp_chunk_{cellX:D2}_{cellZ:D2}");

            foreach (Triangle triangle
                     in triangles)
            {
                writer.Write("f ");

                WriteFaceVertex(
                    writer,
                    triangle.A,
                    positionMap,
                    normalMap,
                    fallbackNormalIndex);

                writer.Write(' ');

                WriteFaceVertex(
                    writer,
                    triangle.B,
                    positionMap,
                    normalMap,
                    fallbackNormalIndex);

                writer.Write(' ');

                WriteFaceVertex(
                    writer,
                    triangle.C,
                    positionMap,
                    normalMap,
                    fallbackNormalIndex);

                writer.WriteLine();
            }

            return new Chunk
            {
                ObjPath =
                    path,

                CellX =
                    cellX,

                CellZ =
                    cellZ,

                TriangleCount =
                    triangles.Count,

                VertexCount =
                    orderedPositions.Count,

                LocalOrigin =
                    localOrigin
            };
        }

        private static void AddUsed(
            FaceVertex value,
            HashSet<int> positions,
            HashSet<int> normals)
        {
            positions.Add(
                value.PositionIndex);

            if (value.NormalIndex >= 0)
            {
                normals.Add(
                    value.NormalIndex);
            }
        }

        private static void WriteFaceVertex(
            TextWriter writer,
            FaceVertex value,
            IReadOnlyDictionary<int, int> positionMap,
            IReadOnlyDictionary<int, int> normalMap,
            int fallbackNormalIndex)
        {
            int position =
                positionMap[
                    value.PositionIndex];

            int normal =
                value.NormalIndex >= 0 &&
                normalMap.TryGetValue(
                    value.NormalIndex,
                    out int mappedNormal)
                    ? mappedNormal
                    : fallbackNormalIndex;

            writer.Write(position);
            writer.Write("//");
            writer.Write(normal);
        }

        private static string Format(
            float value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }
    }
}
