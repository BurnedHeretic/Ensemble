// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ensemble.HaloMapBridge is the separate MCC cache-reading helper.
// It links against Reclaimer (GPL-3.0) and is intentionally kept out of the
// main Ensemble process. See THIRD-PARTY-Reclaimer.txt.

using Reclaimer.Blam.Common;
using Reclaimer.Geometry;
using Reclaimer.Utilities;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Ensemble.HaloMapBridge
{
    internal static class Program
    {
        private const string ClusterRegionName =
            "<Clusters>";

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                WriteIndented =
                    true
            };

        private sealed class BridgeResult
        {
            public bool Success { get; set; }

            public string SourceMapPath { get; set; } =
                string.Empty;

            public string SourceGame { get; set; } =
                string.Empty;

            public string Engine { get; set; } =
                string.Empty;

            public string CacheType { get; set; } =
                string.Empty;

            public string BuildString { get; set; } =
                string.Empty;

            public bool IsMcc { get; set; }

            public string ProviderName { get; set; } =
                "Reclaimer v2.1.1624";

            public string ObjPath { get; set; } =
                string.Empty;

            public List<string> BspTags { get; set; } =
                new();

            public List<string> SkippedBspDiagnostics { get; set; } =
                new();

            public long VertexCount { get; set; }

            public long TriangleCount { get; set; }

            public int MeshPlacementCount { get; set; }

            public string? Error { get; set; }
        }

        private sealed record Options(
            string Command,
            string MapPath,
            string? ObjPath,
            string? JsonPath);

        private static int Main(
            string[] args)
        {
            Options? options =
                null;

            BridgeResult? result =
                null;

            try
            {
                options =
                    ParseOptions(
                        args);

                result =
                    options.Command.Equals(
                        "probe",
                        StringComparison.OrdinalIgnoreCase)
                        ? Probe(
                            options.MapPath)
                        : Extract(
                            options.MapPath,
                            options.ObjPath
                            ??
                            throw new InvalidOperationException(
                                "Extract requires --obj."));

                result.Success =
                    true;

                WriteResult(
                    result,
                    options.JsonPath);

                return 0;
            }
            catch (Exception ex)
            {
                result ??=
                    new BridgeResult
                    {
                        Success =
                            false,

                        SourceMapPath =
                            options?.MapPath
                            ??
                            string.Empty,

                        ObjPath =
                            options?.ObjPath
                            ??
                            string.Empty
                    };

                result.Success =
                    false;

                result.Error =
                    ex.ToString();

                try
                {
                    WriteResult(
                        result,
                        options?.JsonPath);
                }
                catch
                {
                    // Preserve the original failure.
                }

                Console.Error.WriteLine(
                    ex);

                return 1;
            }
        }

        private static Options ParseOptions(
            string[] args)
        {
            if (args.Length ==
                0)
            {
                throw new ArgumentException(
                    "Usage: Ensemble.HaloMapBridge probe|extract --map <file.map> [--obj <output.obj>] [--json <result.json>]");
            }

            string command =
                args[0];

            if (!command.Equals(
                    "probe",
                    StringComparison.OrdinalIgnoreCase)
                &&
                !command.Equals(
                    "extract",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Unknown command '{command}'. Expected probe or extract.");
            }

            string? map =
                null;

            string? obj =
                null;

            string? json =
                null;

            for (int i = 1;
                 i < args.Length;
                 i++)
            {
                string arg =
                    args[i];

                if (arg.Equals(
                        "--map",
                        StringComparison.OrdinalIgnoreCase))
                {
                    map =
                        RequireValue(
                            args,
                            ref i,
                            arg);

                    continue;
                }

                if (arg.Equals(
                        "--obj",
                        StringComparison.OrdinalIgnoreCase))
                {
                    obj =
                        RequireValue(
                            args,
                            ref i,
                            arg);

                    continue;
                }

                if (arg.Equals(
                        "--json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    json =
                        RequireValue(
                            args,
                            ref i,
                            arg);

                    continue;
                }

                throw new ArgumentException(
                    $"Unknown argument '{arg}'.");
            }

            if (string.IsNullOrWhiteSpace(
                    map))
            {
                throw new ArgumentException(
                    "--map is required.");
            }

            map =
                Path.GetFullPath(
                    map);

            if (!File.Exists(
                    map))
            {
                throw new FileNotFoundException(
                    "The selected Halo MCC .map file could not be found.",
                    map);
            }

            if (!Path.GetExtension(
                    map)
                    .Equals(
                        ".map",
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The MCC map bridge expects a .map cache file.");
            }

            if (command.Equals(
                    "extract",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.IsNullOrWhiteSpace(
                    obj))
            {
                throw new ArgumentException(
                    "extract requires --obj <output.obj>.");
            }

            if (!string.IsNullOrWhiteSpace(
                    obj))
            {
                obj =
                    Path.GetFullPath(
                        obj);
            }

            if (!string.IsNullOrWhiteSpace(
                    json))
            {
                json =
                    Path.GetFullPath(
                        json);
            }

            return new Options(
                command,
                map,
                obj,
                json);
        }

        private static string RequireValue(
            string[] args,
            ref int index,
            string argument)
        {
            if (index + 1 >=
                args.Length)
            {
                throw new ArgumentException(
                    $"{argument} requires a value.");
            }

            index++;

            return args[index];
        }

        private static BridgeResult Probe(
            string mapPath)
        {
            ICacheFile cache =
                CacheFactory.ReadCacheFile(
                    mapPath);

            try
            {
                RequireMcc(
                    cache);

                BridgeResult result =
                    CreateBaseResult(
                        cache,
                        mapPath,
                        string.Empty);

                foreach (IIndexItem bsp
                         in cache.TagIndex
                             .Where(
                                 item =>
                                     item.ClassCode
                                         .Equals(
                                             "sbsp",
                                             StringComparison.OrdinalIgnoreCase)))
                {
                    result.BspTags.Add(
                        bsp.TagName);
                }

                if (result.BspTags.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        "The selected MCC cache contains no scenario_structure_bsp (sbsp) tags.");
                }

                return result;
            }
            finally
            {
                (cache as IDisposable)?
                    .Dispose();
            }
        }

        private static BridgeResult Extract(
            string mapPath,
            string outputObjPath)
        {
            string? directory =
                Path.GetDirectoryName(
                    outputObjPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            ICacheFile cache =
                CacheFactory.ReadCacheFile(
                    mapPath);

            try
            {
                RequireMcc(
                    cache);

                BridgeResult result =
                    CreateBaseResult(
                        cache,
                        mapPath,
                        outputObjPath);

                List<IIndexItem> bspItems =
                    cache.TagIndex
                        .Where(
                            item =>
                                item.ClassCode
                                    .Equals(
                                        "sbsp",
                                        StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (bspItems.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        "The selected MCC cache contains no scenario_structure_bsp (sbsp) tags.");
                }

                using StreamWriter writer =
                    new StreamWriter(
                        outputObjPath,
                        append:
                            false,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier:
                                false),
                        bufferSize:
                            1024 *
                            1024);

                writer.WriteLine(
                    "# ENSHALOOBJ 2");

                writer.WriteLine(
                    "# Ensemble MCC all-games Halo .map -> Halo Wars intermediate OBJ");

                writer.WriteLine(
                    "# Backend: Reclaimer v2.1.1624");

                writer.WriteLine(
                    "# Source game: " +
                    result.SourceGame);

                writer.WriteLine(
                    "# Cache type: " +
                    result.CacheType);

                writer.WriteLine(
                    "# Source: " +
                    Path.GetFileName(
                        mapPath));

                writer.WriteLine(
                    "# Core scenario_structure_bsp cluster geometry only.");

                writer.WriteLine(
                    "# Geometry instances, FPS objects, scripts, encounters, weapons and vehicles are intentionally excluded.");

                int globalVertexBase =
                    1;

                for (int bspIndex = 0;
                     bspIndex < bspItems.Count;
                     bspIndex++)
                {
                    IIndexItem bsp =
                        bspItems[bspIndex];

                    string bspName =
                        string.IsNullOrWhiteSpace(
                            bsp.TagName)
                            ? $"sbsp_{bsp.Id:X8}"
                            : bsp.TagName;

                    Console.Error.WriteLine(
                        $"[{bspIndex + 1}/{bspItems.Count}] {bspName}");

                    long beforeTriangles =
                        result.TriangleCount;

                    long beforeVertices =
                        result.VertexCount;

                    int beforePlacements =
                        result.MeshPlacementCount;

                    int beforeBase =
                        globalVertexBase;

                    string bspTempPath =
                        outputObjPath +
                        $".bsp-{bspIndex:D3}.tmp";

                    try
                    {
                        using (
                            StreamWriter bspWriter =
                                new StreamWriter(
                                    bspTempPath,
                                    append:
                                        false,
                                    new UTF8Encoding(
                                        encoderShouldEmitUTF8Identifier:
                                            false),
                                    bufferSize:
                                        1024 *
                                        1024))
                        {
                            ExportBsp(
                                bsp,
                                bspWriter,
                                result,
                                ref globalVertexBase);
                        }

                        if (result.TriangleCount >
                            beforeTriangles)
                        {
                            using FileStream bspStream =
                                new FileStream(
                                    bspTempPath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read,
                                    bufferSize:
                                        1024 *
                                        1024,
                                    FileOptions.SequentialScan);

                            using StreamReader bspReader =
                                new StreamReader(
                                    bspStream,
                                    Encoding.UTF8,
                                    detectEncodingFromByteOrderMarks:
                                        false,
                                    bufferSize:
                                        1024 *
                                        1024);

                            bspReader.CopyTo(
                                writer);

                            result.BspTags.Add(
                                bspName);
                        }
                        else
                        {
                            result.TriangleCount =
                                beforeTriangles;

                            result.VertexCount =
                                beforeVertices;

                            result.MeshPlacementCount =
                                beforePlacements;

                            globalVertexBase =
                                beforeBase;

                            result.SkippedBspDiagnostics.Add(
                                $"{bspName}: no renderable cluster triangles");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.TriangleCount =
                            beforeTriangles;

                        result.VertexCount =
                            beforeVertices;

                        result.MeshPlacementCount =
                            beforePlacements;

                        globalVertexBase =
                            beforeBase;

                        result.SkippedBspDiagnostics.Add(
                            $"{bspName}: {FlattenMessage(ex)}");

                        Console.Error.WriteLine(
                            $"Skipped {bspName}: {ex.Message}");
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(
                                    bspTempPath))
                            {
                                File.Delete(
                                    bspTempPath);
                            }
                        }
                        catch
                        {
                            // Temporary per-BSP output is disposable.
                        }
                    }
                }

                writer.Flush();

                if (result.TriangleCount <=
                        0 ||
                    result.VertexCount <=
                        0)
                {
                    string diagnostics =
                        result.SkippedBspDiagnostics.Count >
                            0
                            ? "\n\nBSP diagnostics:\n - " +
                              string.Join(
                                  "\n - ",
                                  result.SkippedBspDiagnostics
                                      .Take(
                                          12))
                            : string.Empty;

                    throw new InvalidDataException(
                        $"Reclaimer identified {result.SourceGame} / {result.CacheType}, but no BSP triangles could be exported." +
                        diagnostics);
                }

                return result;
            }
            finally
            {
                (cache as IDisposable)?
                    .Dispose();
            }
        }

        private static void ExportBsp(
            IIndexItem bsp,
            TextWriter writer,
            BridgeResult result,
            ref int globalVertexBase)
        {
            if (!ContentFactory.TryGetGeometryContent(
                    bsp,
                    out IContentProvider<Scene>? provider)
                ||
                provider ==
                    null)
            {
                throw new NotSupportedException(
                    "Reclaimer could not create a geometry provider for this BSP.");
            }

            Scene scene =
                provider.GetContent()
                ??
                throw new InvalidDataException(
                    "Reclaimer returned no Scene for this BSP.");

            int emittedPermutations =
                0;

            foreach (Model model
                     in scene.EnumerateModels())
            {
                List<ModelRegion> clusterRegions =
                    model.Regions
                        .Where(
                            region =>
                                string.Equals(
                                    region.Name,
                                    ClusterRegionName,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (clusterRegions.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        "The BSP model has no <Clusters> region.");
                }

                foreach (ModelRegion region
                         in clusterRegions)
                {
                    foreach (ModelPermutation permutation
                             in region.Permutations
                                 .Where(
                                     value =>
                                         value.Export &&
                                         !value.IsInstanced))
                    {
                        Matrix4x4 transform =
                            permutation.GetFinalTransform();

                        foreach (int meshIndex
                                 in permutation.MeshIndices)
                        {
                            Mesh? mesh =
                                model.Meshes
                                    .ElementAtOrDefault(
                                        meshIndex);

                            if (mesh ==
                                    null ||
                                mesh.VertexBuffer ==
                                    null ||
                                mesh.IndexBuffer ==
                                    null ||
                                mesh.VertexCount <=
                                    0 ||
                                mesh.Segments.Count ==
                                    0)
                            {
                                continue;
                            }

                            int triangles =
                                ExportMesh(
                                    writer,
                                    mesh,
                                    transform,
                                    SanitizeObjName(
                                        $"{bsp.TagName}_{permutation.Name}_{meshIndex:D3}"),
                                    ref globalVertexBase,
                                    out int vertices);

                            if (triangles <=
                                0)
                            {
                                continue;
                            }

                            result.TriangleCount +=
                                triangles;

                            result.VertexCount +=
                                vertices;

                            result.MeshPlacementCount++;

                            emittedPermutations++;
                        }
                    }
                }
            }

            if (emittedPermutations ==
                0)
            {
                throw new InvalidDataException(
                    "The BSP contains no exportable core cluster meshes.");
            }
        }

        private static int ExportMesh(
            TextWriter writer,
            Mesh mesh,
            Matrix4x4 permutationTransform,
            string groupName,
            ref int globalVertexBase,
            out int verticesWritten)
        {
            verticesWritten =
                0;

            Vector3[]? positions =
                mesh.GetPositions()?
                    .ToArray();

            if (positions ==
                    null ||
                positions.Length ==
                    0)
            {
                return 0;
            }

            Matrix4x4 expansion =
                mesh.PositionBounds
                    .CreateExpansionMatrix();

            for (int i = 0;
                 i < positions.Length;
                 i++)
            {
                Vector3 p =
                    Vector3.Transform(
                        positions[i],
                        expansion);

                p =
                    Vector3.Transform(
                        p,
                        permutationTransform);

                positions[i] =
                    SwizzlePosition(
                        p);
            }

            List<(int A, int B, int C)> faces =
                new();

            foreach (MeshSegment segment
                     in mesh.Segments)
            {
                int[] indices =
                    mesh.GetTriangleIndicies(
                            segment)
                        .ToArray();

                for (int i = 0;
                     i + 2 < indices.Length;
                     i += 3)
                {
                    int a =
                        indices[i];

                    int b =
                        indices[i + 1];

                    int c =
                        indices[i + 2];

                    if (a < 0 ||
                        b < 0 ||
                        c < 0 ||
                        a >= positions.Length ||
                        b >= positions.Length ||
                        c >= positions.Length ||
                        a == b ||
                        a == c ||
                        b == c)
                    {
                        continue;
                    }

                    Vector3 cross =
                        Vector3.Cross(
                            positions[b] -
                            positions[a],
                            positions[c] -
                            positions[a]);

                    if (!float.IsFinite(
                            cross.X) ||
                        !float.IsFinite(
                            cross.Y) ||
                        !float.IsFinite(
                            cross.Z) ||
                        cross.LengthSquared() <
                            0.00000001f)
                    {
                        continue;
                    }

                    faces.Add(
                        (
                            a,
                            b,
                            c
                        ));
                }
            }

            if (faces.Count ==
                0)
            {
                return 0;
            }

            Vector3[] normals =
                BuildNormals(
                    mesh,
                    positions,
                    faces,
                    permutationTransform);

            writer.WriteLine(
                "g " +
                groupName);

            foreach (Vector3 position
                     in positions)
            {
                writer.Write(
                    "v ");

                WriteFloat(
                    writer,
                    position.X);

                writer.Write(
                    ' ');

                WriteFloat(
                    writer,
                    position.Y);

                writer.Write(
                    ' ');

                WriteFloat(
                    writer,
                    position.Z);

                writer.WriteLine();
            }

            foreach (Vector3 normal
                     in normals)
            {
                writer.Write(
                    "vn ");

                WriteFloat(
                    writer,
                    normal.X);

                writer.Write(
                    ' ');

                WriteFloat(
                    writer,
                    normal.Y);

                writer.Write(
                    ' ');

                WriteFloat(
                    writer,
                    normal.Z);

                writer.WriteLine();
            }

            foreach ((int a, int b, int c)
                     in faces)
            {
                int ia =
                    checked(
                        globalVertexBase +
                        a);

                int ib =
                    checked(
                        globalVertexBase +
                        b);

                int ic =
                    checked(
                        globalVertexBase +
                        c);

                writer.Write(
                    "f ");

                writer.Write(
                    ia);

                writer.Write(
                    "//");

                writer.Write(
                    ia);

                writer.Write(
                    ' ');

                writer.Write(
                    ib);

                writer.Write(
                    "//");

                writer.Write(
                    ib);

                writer.Write(
                    ' ');

                writer.Write(
                    ic);

                writer.Write(
                    "//");

                writer.WriteLine(
                    ic);
            }

            globalVertexBase =
                checked(
                    globalVertexBase +
                    positions.Length);

            verticesWritten =
                positions.Length;

            return faces.Count;
        }

        private static Vector3[] BuildNormals(
            Mesh mesh,
            IReadOnlyList<Vector3> transformedPositions,
            IReadOnlyList<(int A, int B, int C)> faces,
            Matrix4x4 permutationTransform)
        {
            Vector3[]? source =
                mesh.GetNormals()?
                    .ToArray();

            if (source !=
                    null &&
                source.Length ==
                    transformedPositions.Count)
            {
                Vector3[] result =
                    new Vector3[
                        source.Length];

                for (int i = 0;
                     i < source.Length;
                     i++)
                {
                    Vector3 normal =
                        Vector3.TransformNormal(
                            source[i],
                            permutationTransform);

                    normal =
                        SwizzleNormal(
                            normal);

                    result[i] =
                        SafeNormalize(
                            normal,
                            Vector3.UnitY);
                }

                return result;
            }

            Vector3[] accumulated =
                new Vector3[
                    transformedPositions.Count];

            foreach ((int a, int b, int c)
                     in faces)
            {
                Vector3 normal =
                    Vector3.Cross(
                        transformedPositions[b] -
                        transformedPositions[a],
                        transformedPositions[c] -
                        transformedPositions[a]);

                normal =
                    SafeNormalize(
                        normal,
                        Vector3.UnitY);

                accumulated[a] +=
                    normal;

                accumulated[b] +=
                    normal;

                accumulated[c] +=
                    normal;
            }

            for (int i = 0;
                 i < accumulated.Length;
                 i++)
            {
                accumulated[i] =
                    SafeNormalize(
                        accumulated[i],
                        Vector3.UnitY);
            }

            return accumulated;
        }

        private static Vector3 SwizzlePosition(
            Vector3 source)
        {
            // Keep the exact convention used by Ensemble's existing native
            // Reach reader: Blam Z-up -> Ensemble/Halo Wars Y-up.
            return new Vector3(
                source.X,
                source.Z,
                -source.Y);
        }

        private static Vector3 SwizzleNormal(
            Vector3 source)
        {
            return new Vector3(
                source.X,
                source.Z,
                -source.Y);
        }

        private static Vector3 SafeNormalize(
            Vector3 value,
            Vector3 fallback)
        {
            if (!float.IsFinite(
                    value.X) ||
                !float.IsFinite(
                    value.Y) ||
                !float.IsFinite(
                    value.Z) ||
                value.LengthSquared() <
                    0.000001f)
            {
                return fallback;
            }

            return Vector3.Normalize(
                value);
        }

        private static void RequireMcc(
            ICacheFile cache)
        {
            if (!cache.Metadata.IsMcc)
            {
                throw new NotSupportedException(
                    $"The selected cache is {cache.Metadata.Engine} / {cache.CacheType}, but this workflow is for Master Chief Collection .map files.");
            }

            if (cache.Metadata.Engine ==
                BlamEngine.Unknown)
            {
                throw new NotSupportedException(
                    "Reclaimer could not identify the MCC game/engine for this .map.");
            }
        }

        private static BridgeResult CreateBaseResult(
            ICacheFile cache,
            string mapPath,
            string objPath)
        {
            return new BridgeResult
            {
                SourceMapPath =
                    Path.GetFullPath(
                        mapPath),

                ObjPath =
                    string.IsNullOrWhiteSpace(
                        objPath)
                        ? string.Empty
                        : Path.GetFullPath(
                            objPath),

                SourceGame =
                    GetGameName(
                        cache.Metadata.Engine),

                Engine =
                    cache.Metadata.Engine.ToString(),

                CacheType =
                    cache.CacheType.ToString(),

                BuildString =
                    cache.BuildString
                    ??
                    string.Empty,

                IsMcc =
                    cache.Metadata.IsMcc
            };
        }

        private static string GetGameName(
            BlamEngine engine)
        {
            return engine switch
            {
                BlamEngine.Halo1 =>
                    "Halo: Combat Evolved Anniversary",

                BlamEngine.Halo2 =>
                    "Halo 2 Anniversary - Campaign/Classic BSP",

                BlamEngine.Halo3 =>
                    "Halo 3",

                BlamEngine.Halo3ODST =>
                    "Halo 3: ODST",

                BlamEngine.HaloReach =>
                    "Halo: Reach",

                BlamEngine.Halo4 =>
                    "Halo 4",

                BlamEngine.Halo2X =>
                    "Halo 2: Anniversary Multiplayer",

                _ =>
                    engine.ToString()
            };
        }

        private static void WriteResult(
            BridgeResult result,
            string? jsonPath)
        {
            string json =
                JsonSerializer.Serialize(
                    result,
                    JsonOptions);

            if (!string.IsNullOrWhiteSpace(
                    jsonPath))
            {
                string? directory =
                    Path.GetDirectoryName(
                        jsonPath);

                if (!string.IsNullOrWhiteSpace(
                        directory))
                {
                    Directory.CreateDirectory(
                        directory);
                }

                File.WriteAllText(
                    jsonPath,
                    json,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier:
                            false));
            }
            else
            {
                Console.Out.WriteLine(
                    json);
            }
        }

        private static string FlattenMessage(
            Exception ex)
        {
            string message =
                ex.Message.Replace(
                    "\r",
                    " ")
                .Replace(
                    "\n",
                    " ");

            return message.Length >
                600
                    ? message[
                        ..600]
                    : message;
        }

        private static string SanitizeObjName(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "bsp";
            }

            StringBuilder result =
                new StringBuilder(
                    value.Length);

            foreach (char c
                     in value)
            {
                if (char.IsLetterOrDigit(
                        c) ||
                    c is
                        '_' or
                        '-' or
                        '.')
                {
                    result.Append(
                        c);
                }
                else
                {
                    result.Append(
                        '_');
                }
            }

            return result.ToString();
        }

        private static void WriteFloat(
            TextWriter writer,
            float value)
        {
            writer.Write(
                value.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
        }
    }
}
