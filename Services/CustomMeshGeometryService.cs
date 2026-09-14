using Assimp;
using System.IO;
using System.Numerics;

namespace Ensemble.Services
{
    /// <summary>
    /// Imports ordinary artist meshes into a small, engine-neutral triangle
    /// stream used by Ensemble's Halo Wars UGX compiler.
    ///
    /// AssimpNetter is used here only as an interchange reader. The output is
    /// still compiled by Ensemble into Halo Wars' native UGX geometry format.
    /// </summary>
    internal static class CustomMeshGeometryService
    {
        internal readonly struct Vertex
        {
            public Vertex(
                Vector3 position,
                Vector3 normal,
                Vector3 tangent,
                Vector2 texCoord)
            {
                Position = position;
                Normal = normal;
                Tangent = tangent;
                TexCoord = texCoord;
            }

            public Vector3 Position { get; }
            public Vector3 Normal { get; }
            public Vector3 Tangent { get; }
            public Vector2 TexCoord { get; }
        }

        internal sealed class Geometry
        {
            public List<Vertex> TriangleVertices { get; } = new();

            public int TriangleCount => TriangleVertices.Count / 3;

            public int VertexCount => TriangleVertices.Count;

            public Vector3 BoundsMin { get; init; }

            public Vector3 BoundsMax { get; init; }

            public Vector3 BoundsCenter =>
                (BoundsMin + BoundsMax) * 0.5f;

            public float BoundsRadius { get; init; }
        }

        public static Geometry Import(
            string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "The custom mesh file could not be found.",
                    sourcePath);
            }

            string extension =
                Path.GetExtension(sourcePath)
                    .ToLowerInvariant();

            if (extension is not (".obj" or ".fbx"))
            {
                throw new NotSupportedException(
                    "The native UGX compiler currently accepts OBJ and FBX files.\n\n" +
                    $"Selected format: {extension}");
            }

            PostProcessSteps flags =
                PostProcessSteps.Triangulate |
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.CalculateTangentSpace |
                PostProcessSteps.PreTransformVertices |
                PostProcessSteps.FindInvalidData |
                PostProcessSteps.ImproveCacheLocality |
                PostProcessSteps.SortByPrimitiveType |
                PostProcessSteps.ValidateDataStructure |
                PostProcessSteps.MakeLeftHanded |
                PostProcessSteps.FlipWindingOrder;

            using AssimpContext context = new();

            Scene scene =
                context.ImportFile(
                    sourcePath,
                    flags)
                ??
                throw new InvalidDataException(
                    "Assimp could not decode the selected model.");

            if (!scene.HasMeshes ||
                scene.Meshes.Count == 0)
            {
                throw new InvalidDataException(
                    "The selected model contains no mesh geometry.");
            }

            List<Vertex> triangleVertices = new();

            foreach (Assimp.Mesh mesh in scene.Meshes)
            {
                if (mesh.VertexCount <= 0 ||
                    mesh.FaceCount <= 0)
                {
                    continue;
                }

                bool hasNormals =
                    mesh.HasNormals &&
                    mesh.Normals.Count == mesh.VertexCount;

                bool hasTangents =
                    mesh.HasTangentBasis &&
                    mesh.Tangents.Count == mesh.VertexCount;

                bool hasUv0 =
                    mesh.HasTextureCoords(0) &&
                    mesh.TextureCoordinateChannels[0].Count ==
                        mesh.VertexCount;

                foreach (Face face in mesh.Faces)
                {
                    if (face.IndexCount != 3)
                    {
                        continue;
                    }

                    for (int corner = 0;
                         corner < 3;
                         corner++)
                    {
                        int index = face.Indices[corner];

                        if (index < 0 ||
                            index >= mesh.VertexCount)
                        {
                            throw new InvalidDataException(
                                "The imported mesh contains an out-of-range vertex index.");
                        }

                        Vector3 p =
                            mesh.Vertices[index];

                        Vector3 n =
                            hasNormals
                                ? mesh.Normals[index]
                                : Vector3.UnitY;

                        Vector3 t =
                            hasTangents
                                ? mesh.Tangents[index]
                                : Vector3.UnitX;

                        Vector3 uv =
                            hasUv0
                                ? mesh.TextureCoordinateChannels[0][index]
                                : Vector3.Zero;

                        Vector3 normal =
                            SafeNormalise(
                                n,
                                Vector3.UnitY);

                        Vector3 tangent =
                            SafeNormalise(
                                t,
                                BuildFallbackTangent(normal));

                        triangleVertices.Add(
                            new Vertex(
                                p,
                                normal,
                                tangent,
                                new Vector2(uv.X, uv.Y)));
                    }
                }
            }

            if (triangleVertices.Count < 3)
            {
                throw new InvalidDataException(
                    "The selected model contains no triangles that can be compiled to UGX.");
            }

            // Artist files can use arbitrary pivots. Halo Wars scenery is much
            // easier to place when the local origin is bottom-centre, so keep
            // the authored scale while normalising only the pivot.
            Vector3 rawMin =
                new(
                    triangleVertices.Min(v => v.Position.X),
                    triangleVertices.Min(v => v.Position.Y),
                    triangleVertices.Min(v => v.Position.Z));

            Vector3 rawMax =
                new(
                    triangleVertices.Max(v => v.Position.X),
                    triangleVertices.Max(v => v.Position.Y),
                    triangleVertices.Max(v => v.Position.Z));

            Vector3 pivot =
                new(
                    (rawMin.X + rawMax.X) * 0.5f,
                    rawMin.Y,
                    (rawMin.Z + rawMax.Z) * 0.5f);

            List<Vertex> centred =
                new(triangleVertices.Count);

            foreach (Vertex vertex in triangleVertices)
            {
                centred.Add(
                    new Vertex(
                        vertex.Position - pivot,
                        vertex.Normal,
                        vertex.Tangent,
                        vertex.TexCoord));
            }

            Vector3 boundsMin =
                new(
                    centred.Min(v => v.Position.X),
                    centred.Min(v => v.Position.Y),
                    centred.Min(v => v.Position.Z));

            Vector3 boundsMax =
                new(
                    centred.Max(v => v.Position.X),
                    centred.Max(v => v.Position.Y),
                    centred.Max(v => v.Position.Z));

            Vector3 centre =
                (boundsMin + boundsMax) * 0.5f;

            float radius =
                centred.Max(
                    v => Vector3.Distance(
                        v.Position,
                        centre));

            Geometry result =
                new Geometry
                {
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    BoundsRadius = radius
                };

            result.TriangleVertices.AddRange(centred);
            return result;
        }

        private static Vector3 SafeNormalise(
            Vector3 value,
            Vector3 fallback)
        {
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z) ||
                value.LengthSquared() < 0.000001f)
            {
                return fallback;
            }

            return Vector3.Normalize(value);
        }

        private static Vector3 BuildFallbackTangent(
            Vector3 normal)
        {
            Vector3 axis =
                Math.Abs(Vector3.Dot(normal, Vector3.UnitY)) > 0.95f
                    ? Vector3.UnitX
                    : Vector3.UnitY;

            Vector3 tangent =
                Vector3.Cross(axis, normal);

            return tangent.LengthSquared() < 0.000001f
                ? Vector3.UnitX
                : Vector3.Normalize(tangent);
        }
    }
}
