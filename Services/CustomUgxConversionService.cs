using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;

namespace Ensemble.Services
{
    /// <summary>
    /// Compiles a static artist mesh into a native Halo Wars DE UGX by using a
    /// known-good stock rigid UGX as the structural/material template.
    ///
    /// The template contributes the material/GRX metadata and vertex
    /// declaration. Ensemble replaces cached section geometry, VB and IB data.
    /// This mirrors the original Ensemble Studios gr2ugx split between cached
    /// geometry (0x700), index buffer (0x701), vertex buffer (0x702) and
    /// material data (0x704), while avoiding fabricated prototype/material
    /// metadata.
    /// </summary>
    internal static class CustomUgxConversionService
    {
        private const uint EcfMagic = 0xDABA7737;
        private const uint UgxFileId = 0xAAC93746;
        private const ulong CachedChunkId = 0x00000700;
        private const ulong IndexChunkId = 0x00000701;
        private const ulong VertexChunkId = 0x00000702;
        private const uint GeometrySignature = 0xC2340004;

        private const int RootSectionsCountOffset = 64;
        private const int RootSectionsPointerOffset = 72;
        private const int SectionSize = 152;
        private const int MaxTrianglesPerSection = 8192;

        internal sealed class Result
        {
            public required byte[] UgxData { get; init; }
            public required string PackOrder { get; init; }
            public int SectionCount { get; init; }
            public int TriangleCount { get; init; }
            public int VertexCount { get; init; }
        }

        private sealed class EcfChunk
        {
            public ulong Id { get; init; }
            public int Offset { get; init; }
            public int Size { get; init; }
            public byte Flags { get; init; }
            public int Compression => Flags & 0x07;
        }

        private sealed class VertexDeclaration
        {
            public required string PackOrder { get; init; }
            public int PositionType { get; init; }
            public int BasisType { get; init; }
            public int BasisScaleType { get; init; }
            public int TangentType { get; init; }
            public int NormalType { get; init; }
            public int[] UvTypes { get; } = new int[8];
            public int IndicesType { get; init; }
            public int WeightsType { get; init; }
            public int DiffuseType { get; init; }
            public int IndexType { get; init; }
            public int Stride { get; init; }
        }

        public static Result Convert(
            byte[] templateUgx,
            CustomMeshGeometryService.Geometry geometry)
        {
            ArgumentNullException.ThrowIfNull(templateUgx);
            ArgumentNullException.ThrowIfNull(geometry);

            if (geometry.TriangleCount <= 0)
                throw new InvalidDataException("Custom mesh contains no triangles.");

            Dictionary<ulong, EcfChunk> chunks =
                ReadTopLevelChunks(templateUgx);

            EcfChunk cachedChunk = RequireChunk(chunks, CachedChunkId, "cached geometry");
            EcfChunk vertexChunk = RequireChunk(chunks, VertexChunkId, "vertex buffer");
            EcfChunk indexChunk = RequireChunk(chunks, IndexChunkId, "index buffer");

            // EcfFileService.ReplaceChunk preserves the template chunk flags.
            // Only use stock templates whose geometry chunks are stored, which
            // is also how the original gr2ugx writer emitted them.
            foreach (EcfChunk chunk in new[] { cachedChunk, vertexChunk, indexChunk })
            {
                if (chunk.Compression != 0)
                {
                    throw new InvalidDataException(
                        "The candidate stock UGX uses internally compressed geometry chunks. " +
                        "Ensemble will try another rigid template.");
                }
            }

            byte[] cached = Slice(templateUgx, cachedChunk);

            bool bigEndian = DetermineCachedEndianness(cached);

            int templateSectionCount =
                checked((int)ReadUInt32(cached, RootSectionsCountOffset, bigEndian));

            ulong templateSectionPointer =
                ReadUInt64(cached, RootSectionsPointerOffset, bigEndian);

            if (templateSectionCount <= 0 ||
                templateSectionPointer > int.MaxValue ||
                templateSectionPointer + SectionSize > (ulong)cached.Length)
            {
                throw new InvalidDataException(
                    "The candidate UGX does not contain a supported DE 64-bit section table.");
            }

            int templateSectionOffset = checked((int)templateSectionPointer);

            // Rigid Halo Wars scenery commonly still carries a one-bone
            // skeleton. The original Ensemble Studios ugxGeom/gr2ugx code
            // explicitly writes the model bone table even when rigidOnly is
            // true, so a non-zero BCachedData::mBones count must NOT be used
            // to classify a model as skinned. v30 did exactly that and rejected
            // practically every useful stock scenery template.
            //
            // What matters for this geometry-only compiler is whether the
            // header or source section is rigid. We preserve the template bone
            // arrays and reuse its rigid-bone index; the generated vertex stream
            // itself contains no skinning data.
            bool headerRigidOnly =
                cached.Length > 57 &&
                cached[57] != 0;

            bool sectionRigidOnly =
                cached[templateSectionOffset + 144] != 0;

            if (!headerRigidOnly && !sectionRigidOnly)
            {
                throw new InvalidDataException(
                    "The candidate UGX is genuinely skinned and cannot be used as a rigid custom-mesh template.");
            }

            int rigidBoneIndex =
                sectionRigidOnly
                    ? ReadInt32(
                        cached,
                        templateSectionOffset + 12,
                        bigEndian)
                    : ReadInt32(
                        cached,
                        4,
                        bigEndian);

            VertexDeclaration declaration =
                ReadDeclaration(
                    cached,
                    templateSectionOffset,
                    bigEndian);

            ValidateDeclaration(declaration);

            int sectionCount =
                checked(
                    (geometry.TriangleCount +
                     MaxTrianglesPerSection - 1) /
                    MaxTrianglesPerSection);

            if (sectionCount <= 0 || sectionCount > 4096)
                throw new InvalidDataException("Custom mesh requires too many UGX sections.");

            using MemoryStream vertexStream = new();
            using MemoryStream indexStream = new();

            List<(int VertexOffset, int VertexBytes, int VertexCount, int IndexOffset, int TriangleCount)>
                sectionGeometry = new(sectionCount);

            int triangleCursor = 0;
            int globalIndexCount = 0;

            while (triangleCursor < geometry.TriangleCount)
            {
                int triangles =
                    Math.Min(
                        MaxTrianglesPerSection,
                        geometry.TriangleCount - triangleCursor);

                int vertexCount = checked(triangles * 3);

                if (vertexCount > ushort.MaxValue)
                    throw new InvalidDataException("UGX section exceeded the 16-bit index limit.");

                int vertexOffset = checked((int)vertexStream.Position);

                for (int i = 0; i < vertexCount; i++)
                {
                    int sourceIndex =
                        checked(triangleCursor * 3 + i);

                    byte[] encoded =
                        EncodeVertex(
                            geometry.TriangleVertices[sourceIndex],
                            declaration,
                            bigEndian);

                    vertexStream.Write(encoded, 0, encoded.Length);
                }

                int vertexBytes =
                    checked(vertexCount * declaration.Stride);

                int indexOffset = globalIndexCount;

                for (int i = 0; i < vertexCount; i++)
                {
                    Span<byte> indexBytes = stackalloc byte[2];
                    WriteUInt16(indexBytes, checked((ushort)i), bigEndian);
                    indexStream.Write(indexBytes);
                }

                globalIndexCount = checked(globalIndexCount + vertexCount);

                sectionGeometry.Add(
                    (vertexOffset,
                     vertexBytes,
                     vertexCount,
                     indexOffset,
                     triangles));

                triangleCursor += triangles;
            }

            byte[] newCached =
                BuildCachedGeometry(
                    cached,
                    templateSectionOffset,
                    declaration,
                    geometry,
                    sectionGeometry,
                    rigidBoneIndex,
                    bigEndian);

            byte[] result = templateUgx;

            result = EcfFileService.ReplaceChunk(result, CachedChunkId, newCached);
            result = EcfFileService.ReplaceChunk(result, VertexChunkId, vertexStream.ToArray());
            result = EcfFileService.ReplaceChunk(result, IndexChunkId, indexStream.ToArray());

            ValidateGeneratedUgx(
                result,
                geometry.TriangleCount,
                sectionCount);

            return new Result
            {
                UgxData = result,
                PackOrder = declaration.PackOrder,
                SectionCount = sectionCount,
                TriangleCount = geometry.TriangleCount,
                VertexCount = geometry.VertexCount
            };
        }

        private static byte[] BuildCachedGeometry(
            byte[] original,
            int templateSectionOffset,
            VertexDeclaration declaration,
            CustomMeshGeometryService.Geometry geometry,
            IReadOnlyList<(int VertexOffset, int VertexBytes, int VertexCount, int IndexOffset, int TriangleCount)> sections,
            int rigidBoneIndex,
            bool bigEndian)
        {
            int newSectionsOffset = Align(original.Length, 16);
            int finalLength = checked(newSectionsOffset + sections.Count * SectionSize);

            byte[] result = new byte[finalLength];
            Buffer.BlockCopy(original, 0, result, 0, original.Length);

            WriteUInt32(
                result,
                RootSectionsCountOffset,
                checked((uint)sections.Count),
                bigEndian);

            WriteUInt64(
                result,
                RootSectionsPointerOffset,
                checked((ulong)newSectionsOffset),
                bigEndian);

            // Header BHeader<packed> fields, validated against the original
            // Ensemble Studios ugxGeom source.
            Vector3 centre = geometry.BoundsCenter;

            WriteSingle(result, 8, centre.X, bigEndian);
            WriteSingle(result, 12, centre.Y, bigEndian);
            WriteSingle(result, 16, centre.Z, bigEndian);
            WriteSingle(result, 20, geometry.BoundsRadius, bigEndian);

            WriteSingle(result, 24, geometry.BoundsMin.X, bigEndian);
            WriteSingle(result, 28, geometry.BoundsMin.Y, bigEndian);
            WriteSingle(result, 32, geometry.BoundsMin.Z, bigEndian);

            WriteSingle(result, 36, geometry.BoundsMax.X, bigEndian);
            WriteSingle(result, 40, geometry.BoundsMax.Y, bigEndian);
            WriteSingle(result, 44, geometry.BoundsMax.Z, bigEndian);

            WriteInt16(result, 48, 1, bigEndian); // max instances
            WriteInt16(result, 50, 0, bigEndian); // instance multiplier

            // Keep the template's global-bones flag and bone arrays. A rigid
            // model may legitimately contain a skeleton; rigidOnly tells the
            // renderer to use one bone rather than skin each vertex.
            WriteInt32(result, 4, rigidBoneIndex, bigEndian);
            result[54] = 1; // all sections rigid
            // result[55] = template global-bones flag (preserved)
            result[56] = 0; // all sections skinned
            result[57] = 1; // rigid only

            for (int i = 0; i < sections.Count; i++)
            {
                int target = checked(newSectionsOffset + i * SectionSize);

                Buffer.BlockCopy(
                    original,
                    templateSectionOffset,
                    result,
                    target,
                    SectionSize);

                var part = sections[i];

                // Preserve material/accessory/rigid-bone metadata from the
                // stock slot, replacing only geometry stream bookkeeping.
                WriteInt32(result, target + 12, rigidBoneIndex, bigEndian);
                WriteInt32(result, target + 16, part.IndexOffset, bigEndian);
                WriteInt32(result, target + 20, part.TriangleCount, bigEndian);
                WriteInt32(result, target + 24, part.VertexOffset, bigEndian);
                WriteInt32(result, target + 28, part.VertexBytes, bigEndian);
                WriteInt32(result, target + 32, declaration.Stride, bigEndian);
                WriteInt32(result, target + 36, part.VertexCount, bigEndian);
                result[target + 144] = 1;
            }

            return result;
        }

        private static VertexDeclaration ReadDeclaration(
            byte[] cached,
            int sectionOffset,
            bool bigEndian)
        {
            ulong packOrderPointer =
                ReadUInt64(cached, sectionOffset + 56, bigEndian);

            VertexDeclaration result =
                new VertexDeclaration
                {
                    PackOrder = ReadPackedAsciiString(cached, packOrderPointer),
                    PositionType = ReadInt32(cached, sectionOffset + 72, bigEndian),
                    BasisType = ReadInt32(cached, sectionOffset + 76, bigEndian),
                    BasisScaleType = ReadInt32(cached, sectionOffset + 80, bigEndian),
                    TangentType = ReadInt32(cached, sectionOffset + 84, bigEndian),
                    NormalType = ReadInt32(cached, sectionOffset + 88, bigEndian),
                    IndicesType = ReadInt32(cached, sectionOffset + 124, bigEndian),
                    WeightsType = ReadInt32(cached, sectionOffset + 128, bigEndian),
                    DiffuseType = ReadInt32(cached, sectionOffset + 132, bigEndian),
                    IndexType = ReadInt32(cached, sectionOffset + 136, bigEndian),
                    Stride = ReadInt32(cached, sectionOffset + 32, bigEndian)
                };

            for (int i = 0; i < 8; i++)
            {
                result.UvTypes[i] =
                    ReadInt32(cached, sectionOffset + 92 + i * 4, bigEndian);
            }

            return result;
        }

        private static void ValidateDeclaration(
            VertexDeclaration declaration)
        {
            if (string.IsNullOrWhiteSpace(declaration.PackOrder))
                throw new InvalidDataException("Rigid UGX template has no vertex pack order.");

            if (declaration.Stride <= 0 || declaration.Stride > 512)
                throw new InvalidDataException("Rigid UGX template has an invalid vertex stride.");

            int calculated = 0;

            string order = declaration.PackOrder;

            for (int i = 0; i < order.Length; i++)
            {
                char token = char.ToUpperInvariant(order[i]);

                switch (token)
                {
                    case 'P':
                        calculated += ElementSize(declaration.PositionType);
                        break;

                    case 'N':
                        calculated += ElementSize(declaration.NormalType);
                        break;

                    case 'A':
                        i = SkipIndexSuffix(order, i);
                        calculated += ElementSize(declaration.TangentType);
                        break;

                    case 'T':
                    {
                        int uv = ReadIndexSuffix(order, ref i);
                        uv = Math.Clamp(uv, 0, 7);
                        calculated += ElementSize(declaration.UvTypes[uv]);
                        break;
                    }

                    case 'B':
                        i = SkipIndexSuffix(order, i);
                        calculated += ElementSize(declaration.BasisType) * 2;
                        break;

                    case 'X':
                        i = SkipIndexSuffix(order, i);
                        calculated += ElementSize(declaration.BasisScaleType);
                        break;

                    case 'D':
                        calculated += ElementSize(declaration.DiffuseType);
                        break;

                    case 'I':
                        calculated += ElementSize(declaration.IndexType);
                        break;

                    case 'S':
                        throw new InvalidDataException(
                            $"UGX template pack order '{order}' contains per-vertex skinning data and cannot be used for a rigid custom model.");

                    default:
                        throw new InvalidDataException(
                            $"Unsupported UGX pack-order token '{token}'.");
                }
            }

            if (calculated > declaration.Stride)
            {
                throw new InvalidDataException(
                    $"UGX vertex declaration consumes {calculated} bytes but declares a {declaration.Stride}-byte stride.");
            }
        }

        private static byte[] EncodeVertex(
            CustomMeshGeometryService.Vertex vertex,
            VertexDeclaration declaration,
            bool bigEndian)
        {
            byte[] output = new byte[declaration.Stride];
            int cursor = 0;
            string order = declaration.PackOrder;

            for (int i = 0; i < order.Length; i++)
            {
                char token = char.ToUpperInvariant(order[i]);
                Vector4 value;
                int type;

                switch (token)
                {
                    case 'B':
                    {
                        i = SkipIndexSuffix(order, i);

                        Vector3 normal = SafeNormalise(
                            vertex.Normal,
                            Vector3.UnitY);

                        Vector3 tangent = SafeNormalise(
                            vertex.Tangent -
                            normal * Vector3.Dot(vertex.Tangent, normal),
                            BuildFallbackTangent(normal));

                        Vector3 binormal = SafeNormalise(
                            Vector3.Cross(normal, tangent),
                            Vector3.UnitZ);

                        int basisSize = ElementSize(declaration.BasisType);

                        if (cursor + basisSize * 2 > output.Length)
                            throw new InvalidDataException("UGX basis data exceeded its declared stride.");

                        WriteElement(
                            output.AsSpan(cursor, basisSize),
                            declaration.BasisType,
                            new Vector4(tangent, 1.0f),
                            bigEndian);

                        cursor += basisSize;

                        WriteElement(
                            output.AsSpan(cursor, basisSize),
                            declaration.BasisType,
                            new Vector4(binormal, 1.0f),
                            bigEndian);

                        cursor += basisSize;
                        continue;
                    }

                    case 'X':
                    {
                        i = SkipIndexSuffix(order, i);
                        int basisScaleSize = ElementSize(declaration.BasisScaleType);

                        if (cursor + basisScaleSize > output.Length)
                            throw new InvalidDataException("UGX basis-scale data exceeded its declared stride.");

                        WriteElement(
                            output.AsSpan(cursor, basisScaleSize),
                            declaration.BasisScaleType,
                            new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                            bigEndian);

                        cursor += basisScaleSize;
                        continue;
                    }

                    case 'P':
                        value = new Vector4(vertex.Position, 1.0f);
                        type = declaration.PositionType;
                        break;

                    case 'N':
                        value = new Vector4(vertex.Normal, 0.0f);
                        type = declaration.NormalType;
                        break;

                    case 'A':
                        i = SkipIndexSuffix(order, i);
                        value = new Vector4(vertex.Tangent, 1.0f);
                        type = declaration.TangentType;
                        break;

                    case 'T':
                    {
                        int uv = ReadIndexSuffix(order, ref i);
                        uv = Math.Clamp(uv, 0, 7);
                        value = new Vector4(vertex.TexCoord, 0.0f, 1.0f);
                        type = declaration.UvTypes[uv];
                        break;
                    }

                    case 'D':
                        value = Vector4.One;
                        type = declaration.DiffuseType;
                        break;

                    case 'I':
                        value = Vector4.Zero;
                        type = declaration.IndexType;
                        break;

                    default:
                        throw new InvalidDataException(
                            $"Unsupported UGX pack-order token '{token}'.");
                }

                int size = ElementSize(type);

                if (cursor + size > output.Length)
                    throw new InvalidDataException("UGX vertex pack order exceeded its declared stride.");

                WriteElement(
                    output.AsSpan(cursor, size),
                    type,
                    value,
                    bigEndian);

                cursor += size;
            }

            return output;
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

            Vector3 tangent = Vector3.Cross(axis, normal);

            return tangent.LengthSquared() < 0.000001f
                ? Vector3.UnitX
                : Vector3.Normalize(tangent);
        }

        private static void WriteElement(
            Span<byte> destination,
            int type,
            Vector4 value,
            bool bigEndian)
        {
            switch (type)
            {
                case 0:
                    return;

                case 1:
                    WriteSingle(destination, 0, value.X, bigEndian);
                    return;

                case 2:
                    WriteSingle(destination, 0, value.X, bigEndian);
                    WriteSingle(destination, 4, value.Y, bigEndian);
                    return;

                case 3:
                    WriteSingle(destination, 0, value.X, bigEndian);
                    WriteSingle(destination, 4, value.Y, bigEndian);
                    WriteSingle(destination, 8, value.Z, bigEndian);
                    return;

                case 4:
                    WriteSingle(destination, 0, value.X, bigEndian);
                    WriteSingle(destination, 4, value.Y, bigEndian);
                    WriteSingle(destination, 8, value.Z, bigEndian);
                    WriteSingle(destination, 12, value.W, bigEndian);
                    return;

                case 5:
                {
                    byte r = ToByte01(value.X);
                    byte g = ToByte01(value.Y);
                    byte b = ToByte01(value.Z);
                    byte a = ToByte01(value.W);
                    uint packed =
                        ((uint)a << 24) |
                        ((uint)r << 16) |
                        ((uint)g << 8) |
                        b;
                    WriteUInt32(destination, 0, packed, bigEndian);
                    return;
                }

                case 6:
                {
                    uint packed =
                        ((uint)ToByteRaw(value.W) << 24) |
                        ((uint)ToByteRaw(value.Z) << 16) |
                        ((uint)ToByteRaw(value.Y) << 8) |
                        ToByteRaw(value.X);
                    WriteUInt32(destination, 0, packed, bigEndian);
                    return;
                }

                case 7:
                    WriteInt16(destination, 0, ToShortRaw(value.X), bigEndian);
                    WriteInt16(destination, 2, ToShortRaw(value.Y), bigEndian);
                    return;

                case 8:
                    WriteInt16(destination, 0, ToShortRaw(value.X), bigEndian);
                    WriteInt16(destination, 2, ToShortRaw(value.Y), bigEndian);
                    WriteInt16(destination, 4, ToShortRaw(value.Z), bigEndian);
                    WriteInt16(destination, 6, ToShortRaw(value.W), bigEndian);
                    return;

                case 9:
                {
                    uint packed =
                        ((uint)ToByte01(value.W) << 24) |
                        ((uint)ToByte01(value.Z) << 16) |
                        ((uint)ToByte01(value.Y) << 8) |
                        ToByte01(value.X);
                    WriteUInt32(destination, 0, packed, bigEndian);
                    return;
                }

                case 10:
                    WriteInt16(destination, 0, ToSignedNormal16(value.X), bigEndian);
                    WriteInt16(destination, 2, ToSignedNormal16(value.Y), bigEndian);
                    return;

                case 11:
                    WriteInt16(destination, 0, ToSignedNormal16(value.X), bigEndian);
                    WriteInt16(destination, 2, ToSignedNormal16(value.Y), bigEndian);
                    WriteInt16(destination, 4, ToSignedNormal16(value.Z), bigEndian);
                    WriteInt16(destination, 6, ToSignedNormal16(value.W), bigEndian);
                    return;

                case 12:
                    WriteUInt16(destination, 0, ToUnsignedNormal16(value.X), bigEndian);
                    WriteUInt16(destination, 2, ToUnsignedNormal16(value.Y), bigEndian);
                    return;

                case 13:
                    WriteUInt16(destination, 0, ToUnsignedNormal16(value.X), bigEndian);
                    WriteUInt16(destination, 2, ToUnsignedNormal16(value.Y), bigEndian);
                    WriteUInt16(destination, 4, ToUnsignedNormal16(value.Z), bigEndian);
                    WriteUInt16(destination, 6, ToUnsignedNormal16(value.W), bigEndian);
                    return;

                case 14:
                {
                    uint x = (uint)Math.Clamp((int)MathF.Round(value.X), 0, 1023);
                    uint y = (uint)Math.Clamp((int)MathF.Round(value.Y), 0, 1023);
                    uint z = (uint)Math.Clamp((int)MathF.Round(value.Z), 0, 1023);
                    WriteUInt32(destination, 0, (z << 20) | (y << 10) | x, bigEndian);
                    return;
                }

                case 15:
                {
                    uint x = (uint)(Math.Clamp((int)MathF.Round(value.X * 511.0f), -512, 511) & 0x3ff);
                    uint y = (uint)(Math.Clamp((int)MathF.Round(value.Y * 511.0f), -512, 511) & 0x3ff);
                    uint z = (uint)(Math.Clamp((int)MathF.Round(value.Z * 511.0f), -512, 511) & 0x3ff);
                    WriteUInt32(destination, 0, (z << 20) | (y << 10) | x, bigEndian);
                    return;
                }

                case 16:
                    WriteHalf(destination, 0, value.X, bigEndian);
                    WriteHalf(destination, 2, value.Y, bigEndian);
                    return;

                case 17:
                    WriteHalf(destination, 0, value.X, bigEndian);
                    WriteHalf(destination, 2, value.Y, bigEndian);
                    WriteHalf(destination, 4, value.Z, bigEndian);
                    WriteHalf(destination, 6, value.W, bigEndian);
                    return;

                case 18:
                    WriteHalf(destination, 0, value.X, bigEndian);
                    return;

                case 19:
                {
                    uint x = (uint)Math.Clamp((int)MathF.Round(value.X * 1023.0f), 0, 1023);
                    uint y = (uint)Math.Clamp((int)MathF.Round(value.Y * 1023.0f), 0, 1023);
                    uint z = (uint)Math.Clamp((int)MathF.Round(value.Z * 1023.0f), 0, 1023);
                    WriteUInt32(destination, 0, (z << 20) | (y << 10) | x, bigEndian);
                    return;
                }

                case 20:
                {
                    uint x = (uint)Math.Clamp((int)MathF.Round(value.X * 511.0f) + 512, 0, 1023);
                    uint y = (uint)Math.Clamp((int)MathF.Round(value.Y * 1023.0f) + 1024, 0, 2047);
                    uint z = (uint)Math.Clamp((int)MathF.Round(value.Z * 1023.0f) + 1024, 0, 2047);
                    WriteUInt32(destination, 0, (z << 21) | (y << 10) | x, bigEndian);
                    return;
                }

                default:
                    throw new InvalidDataException($"Unknown UGX vertex element type {type}.");
            }
        }

        private static void ValidateGeneratedUgx(
            byte[] data,
            int expectedTriangles,
            int expectedSections)
        {
            Dictionary<ulong, EcfChunk> chunks = ReadTopLevelChunks(data);
            EcfChunk cachedChunk = RequireChunk(chunks, CachedChunkId, "cached geometry");
            EcfChunk vertexChunk = RequireChunk(chunks, VertexChunkId, "vertex buffer");
            EcfChunk indexChunk = RequireChunk(chunks, IndexChunkId, "index buffer");

            byte[] cached = Slice(data, cachedChunk);
            bool bigEndian = DetermineCachedEndianness(cached);

            int sectionCount =
                checked((int)ReadUInt32(cached, RootSectionsCountOffset, bigEndian));
            ulong sectionPointer =
                ReadUInt64(cached, RootSectionsPointerOffset, bigEndian);

            if (sectionCount != expectedSections ||
                sectionPointer > int.MaxValue ||
                sectionPointer + (ulong)sectionCount * SectionSize > (ulong)cached.Length)
            {
                throw new InvalidDataException("Generated UGX failed section-table verification.");
            }

            int totalTriangles = 0;

            for (int i = 0; i < sectionCount; i++)
            {
                int section = checked((int)sectionPointer + i * SectionSize);
                int tris = ReadInt32(cached, section + 20, bigEndian);
                int verts = ReadInt32(cached, section + 36, bigEndian);
                int stride = ReadInt32(cached, section + 32, bigEndian);
                int vbOffset = ReadInt32(cached, section + 24, bigEndian);
                int vbBytes = ReadInt32(cached, section + 28, bigEndian);
                int ibOffset = ReadInt32(cached, section + 16, bigEndian);

                if (tris <= 0 || verts <= 0 || stride <= 0 ||
                    vbOffset < 0 || vbBytes < 0 ||
                    vbOffset + vbBytes > vertexChunk.Size ||
                    ibOffset < 0 ||
                    checked((ibOffset + tris * 3) * 2) > indexChunk.Size)
                {
                    throw new InvalidDataException("Generated UGX contains an invalid geometry section.");
                }

                totalTriangles += tris;
            }

            if (totalTriangles != expectedTriangles)
            {
                throw new InvalidDataException(
                    $"Generated UGX triangle verification failed ({totalTriangles} != {expectedTriangles}).");
            }
        }

        private static Dictionary<ulong, EcfChunk> ReadTopLevelChunks(
            byte[] data)
        {
            if (data.Length < 32 ||
                ReadUInt32(data, 0, true) != EcfMagic)
            {
                throw new InvalidDataException("Template file is not a valid Halo Wars ECF/UGX.");
            }

            int headerSize = checked((int)ReadUInt32(data, 4, true));
            int declaredSize = checked((int)ReadUInt32(data, 12, true));
            int chunkCount = ReadUInt16(data, 16, true);
            uint fileId = ReadUInt32(data, 20, true);
            int extra = ReadUInt16(data, 24, true);

            if (fileId != UgxFileId)
                throw new InvalidDataException("Template ECF is not a Halo Wars UGX model.");

            if (declaredSize != 0 && declaredSize != data.Length)
                throw new InvalidDataException("Template UGX file-size header is invalid.");

            int chunkHeaderSize = checked(24 + extra);
            Dictionary<ulong, EcfChunk> result = new();

            for (int i = 0; i < chunkCount; i++)
            {
                int p = checked(headerSize + i * chunkHeaderSize);
                EnsureRange(data, p, 24);

                ulong id = ReadUInt64(data, p, true);
                int offset = checked((int)ReadUInt32(data, p + 8, true));
                int size = checked((int)ReadUInt32(data, p + 12, true));
                byte flags = data[p + 20];

                EnsureRange(data, offset, size);

                result[id] =
                    new EcfChunk
                    {
                        Id = id,
                        Offset = offset,
                        Size = size,
                        Flags = flags
                    };
            }

            return result;
        }

        private static EcfChunk RequireChunk(
            IReadOnlyDictionary<ulong, EcfChunk> chunks,
            ulong id,
            string name)
        {
            if (!chunks.TryGetValue(id, out EcfChunk? chunk))
                throw new InvalidDataException($"UGX template is missing its {name} chunk.");

            return chunk;
        }

        private static byte[] Slice(
            byte[] source,
            EcfChunk chunk)
        {
            return source.AsSpan(chunk.Offset, chunk.Size).ToArray();
        }

        private static bool DetermineCachedEndianness(
            byte[] cached)
        {
            if (cached.Length < 80)
                throw new InvalidDataException("UGX cached geometry is too small.");

            if (ReadUInt32(cached, 0, false) == GeometrySignature)
                return false;

            if (ReadUInt32(cached, 0, true) == GeometrySignature)
                return true;

            throw new InvalidDataException("UGX cached geometry signature is invalid.");
        }

        private static string ReadPackedAsciiString(
            byte[] data,
            ulong pointer)
        {
            if (pointer == 0 ||
                pointer == uint.MaxValue ||
                pointer == ulong.MaxValue)
            {
                return string.Empty;
            }

            if (pointer >= (ulong)data.Length)
                throw new InvalidDataException("UGX packed string pointer is outside cached geometry.");

            int start = checked((int)pointer);
            int end = start;

            while (end < data.Length && data[end] != 0)
                end++;

            if (end >= data.Length)
                throw new InvalidDataException("UGX packed string is unterminated.");

            return Encoding.ASCII.GetString(data, start, end - start);
        }

        private static int ElementSize(int type)
        {
            return type switch
            {
                0 => 0,
                1 => 4,
                2 => 8,
                3 => 12,
                4 => 16,
                5 => 4,
                6 => 4,
                7 => 4,
                8 => 8,
                9 => 4,
                10 => 4,
                11 => 8,
                12 => 4,
                13 => 8,
                14 => 4,
                15 => 4,
                16 => 4,
                17 => 8,
                18 => 2,
                19 => 4,
                20 => 4,
                _ => throw new InvalidDataException(
                    $"Unknown UGX vertex element type {type}.")
            };
        }

        private static int SkipIndexSuffix(string order, int index)
        {
            if (index + 1 < order.Length && char.IsDigit(order[index + 1]))
                return index + 1;

            return index;
        }

        private static int ReadIndexSuffix(string order, ref int index)
        {
            if (index + 1 < order.Length && char.IsDigit(order[index + 1]))
            {
                index++;
                return order[index] - '0';
            }

            return 0;
        }

        private static int Align(int value, int alignment)
        {
            int mask = alignment - 1;
            return checked((value + mask) & ~mask);
        }

        private static byte ToByte01(float value) =>
            checked((byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0, 1) * 255.0f), 0, 255));

        private static byte ToByteRaw(float value) =>
            checked((byte)Math.Clamp((int)MathF.Round(value), 0, 255));

        private static short ToShortRaw(float value) =>
            checked((short)Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue));

        private static short ToSignedNormal16(float value) =>
            checked((short)Math.Clamp((int)MathF.Round(Math.Clamp(value, -1, 1) * 32767.0f), short.MinValue, short.MaxValue));

        private static ushort ToUnsignedNormal16(float value) =>
            checked((ushort)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0, 1) * 65535.0f), 0, ushort.MaxValue));

        private static void WriteHalf(Span<byte> data, int offset, float value, bool bigEndian)
        {
            ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
            WriteUInt16(data, offset, bits, bigEndian);
        }

        private static void EnsureRange(byte[] data, int offset, int size)
        {
            if (offset < 0 || size < 0 || (long)offset + size > data.Length)
                throw new InvalidDataException("UGX data range is outside the file.");
        }

        private static ushort ReadUInt16(byte[] data, int offset, bool bigEndian)
        {
            return bigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        }

        private static uint ReadUInt32(byte[] data, int offset, bool bigEndian)
        {
            return bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        }

        private static int ReadInt32(byte[] data, int offset, bool bigEndian) =>
            unchecked((int)ReadUInt32(data, offset, bigEndian));

        private static ulong ReadUInt64(byte[] data, int offset, bool bigEndian)
        {
            return bigEndian
                ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8))
                : BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
        }

        private static void WriteUInt16(Span<byte> data, ushort value, bool bigEndian)
        {
            WriteUInt16(data, 0, value, bigEndian);
        }

        private static void WriteUInt16(Span<byte> data, int offset, ushort value, bool bigEndian)
        {
            if (bigEndian)
                BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);
            else
                BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), value);
        }

        private static void WriteInt16(byte[] data, int offset, short value, bool bigEndian) =>
            WriteInt16(data.AsSpan(), offset, value, bigEndian);

        private static void WriteInt16(Span<byte> data, int offset, short value, bool bigEndian)
        {
            if (bigEndian)
                BinaryPrimitives.WriteInt16BigEndian(data.Slice(offset, 2), value);
            else
                BinaryPrimitives.WriteInt16LittleEndian(data.Slice(offset, 2), value);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value, bool bigEndian) =>
            WriteUInt32(data.AsSpan(), offset, value, bigEndian);

        private static void WriteUInt32(Span<byte> data, int offset, uint value, bool bigEndian)
        {
            if (bigEndian)
                BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), value);
        }

        private static void WriteInt32(byte[] data, int offset, int value, bool bigEndian) =>
            WriteUInt32(data, offset, unchecked((uint)value), bigEndian);

        private static void WriteUInt64(byte[] data, int offset, ulong value, bool bigEndian)
        {
            if (bigEndian)
                BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset, 8), value);
            else
                BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
        }

        private static void WriteSingle(byte[] data, int offset, float value, bool bigEndian) =>
            WriteSingle(data.AsSpan(), offset, value, bigEndian);

        private static void WriteSingle(Span<byte> data, int offset, float value, bool bigEndian)
        {
            int bits = BitConverter.SingleToInt32Bits(value);

            if (bigEndian)
                BinaryPrimitives.WriteInt32BigEndian(data.Slice(offset, 4), bits);
            else
                BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), bits);
        }
    }
}
