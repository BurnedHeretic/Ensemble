using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Ensemble.Services
{
    /// <summary>
    /// Native Halo Reach MCC cache-map geometry importer.
    ///
    /// v35 deliberately imports only scenario_structure_bsp cluster geometry.
    /// It does not depend on Reclaimer and it does not import Halo FPS gameplay
    /// objects.  The extracted BSP is written to an intermediate OBJ and then
    /// enters Ensemble's existing OBJ -> Halo Wars UGX -> ERA pipeline.
    ///
    /// Initial target: MCC Halo Reach U13-compatible PC cache files.
    /// </summary>
    internal static class HaloMapImportService
    {
        // Halo Reach world coordinates and Halo Wars terrain coordinates are not
        // directly interchangeable.  v35.2 defaults to 1:1 and the UI uses
        // CalculateFitScale() to fit imported BSP geometry to the currently
        // open Halo Wars map.
        public const float DefaultHaloToHaloWarsScale = 1.0f;

        internal sealed class ExtractOptions
        {
            public float WorldScale { get; init; }
                = DefaultHaloToHaloWarsScale;
        }

        internal sealed class ExtractResult
        {
            public string SourceMapPath { get; init; }
                = string.Empty;

            public string ObjPath { get; init; }
                = string.Empty;

            public string CacheImplementation { get; init; }
                = "Ensemble Native MCC Halo Reach";

            public string BuildString { get; init; }
                = string.Empty;

            public List<string> BspTags { get; init; }
                = new();

            public List<string> SkippedBspDiagnostics { get; init; }
                = new();

            public long VertexCount { get; set; }

            public long TriangleCount { get; set; }

            public int MeshPlacementCount { get; set; }

            public float AppliedScale { get; set; }

            public float SourceWidth { get; set; }

            public float SourceDepth { get; set; }
        }

        public static float CalculateFitScale(
            string mapPath,
            float targetWidth,
            float targetDepth,
            float occupancy = 0.82f)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
                throw new FileNotFoundException("The selected Halo cache map could not be found.", mapPath);

            if (!float.IsFinite(targetWidth) || targetWidth <= 0 ||
                !float.IsFinite(targetDepth) || targetDepth <= 0)
            {
                return DefaultHaloToHaloWarsScale;
            }

            occupancy = Math.Clamp(occupancy, 0.10f, 0.98f);

            using ReachMccCache cache = new(mapPath);

            if (!cache.TryGetScenarioHorizontalSize(
                    out float sourceWidth,
                    out float sourceDepth))
            {
                return DefaultHaloToHaloWarsScale;
            }

            float scale =
                Math.Min(
                    targetWidth / Math.Max(0.0001f, sourceWidth),
                    targetDepth / Math.Max(0.0001f, sourceDepth))
                * occupancy;

            return float.IsFinite(scale) && scale > 0
                ? Math.Clamp(scale, 0.001f, 1000.0f)
                : DefaultHaloToHaloWarsScale;
        }


        /// <summary>
        /// Measures the actual horizontal footprint written to an Ensemble-generated
        /// Halo OBJ.  This intentionally uses emitted vertices rather than raw SBSP
        /// metadata bounds because streamed/auxiliary Reach BSPs can be far away from
        /// the render geometry that was successfully imported.
        /// </summary>
        public static bool TryMeasureGeneratedObjHorizontalSize(
            string objPath,
            out float width,
            out float depth)
        {
            width = 0;
            depth = 0;

            if (string.IsNullOrWhiteSpace(objPath) || !File.Exists(objPath))
                return false;

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            bool any = false;

            foreach (string rawLine in File.ReadLines(objPath))
            {
                if (!rawLine.StartsWith("v ", StringComparison.Ordinal))
                    continue;

                string[] parts = rawLine.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 4 ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                    !float.IsFinite(x) ||
                    !float.IsFinite(z))
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
                any = true;
            }

            if (!any)
                return false;

            width = maxX - minX;
            depth = maxZ - minZ;

            return float.IsFinite(width) && width > 0.0001f &&
                   float.IsFinite(depth) && depth > 0.0001f;
        }

        public static float CalculateFitScaleFromGeometry(
            float sourceWidth,
            float sourceDepth,
            float targetWidth,
            float targetDepth,
            float occupancy = 0.90f)
        {
            if (!float.IsFinite(sourceWidth) || sourceWidth <= 0 ||
                !float.IsFinite(sourceDepth) || sourceDepth <= 0 ||
                !float.IsFinite(targetWidth) || targetWidth <= 0 ||
                !float.IsFinite(targetDepth) || targetDepth <= 0)
            {
                return DefaultHaloToHaloWarsScale;
            }

            occupancy = Math.Clamp(occupancy, 0.05f, 3.0f);

            float scale =
                Math.Min(
                    targetWidth / sourceWidth,
                    targetDepth / sourceDepth)
                * occupancy;

            return float.IsFinite(scale) && scale > 0
                ? Math.Clamp(scale, 0.0001f, 10000.0f)
                : DefaultHaloToHaloWarsScale;
        }

        /// <summary>
        /// Uniformly scales vertex positions in an Ensemble-generated Halo OBJ.
        /// Normals and indices remain unchanged.  Scaling here means the chosen size
        /// is baked into the later UGX rather than being an editor-only transform.
        /// </summary>
        public static void ScaleGeneratedObj(
            string objPath,
            float scale)
        {
            if (string.IsNullOrWhiteSpace(objPath) || !File.Exists(objPath))
                throw new FileNotFoundException("The generated Halo OBJ could not be found.", objPath);

            if (!float.IsFinite(scale) || scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(scale), "Halo OBJ scale must be positive and finite.");

            if (Math.Abs(scale - 1.0f) < 0.000001f)
                return;

            string tempPath = objPath + ".scaled.tmp";

            try
            {
                using StreamReader reader = new(objPath);
                using StreamWriter writer = new(tempPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("v ", StringComparison.Ordinal))
                    {
                        writer.WriteLine(line);
                        continue;
                    }

                    string[] parts = line.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 4 ||
                        !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                        !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                        !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        throw new InvalidDataException("The generated Halo OBJ contains an invalid vertex line while applying scale.");
                    }

                    x *= scale;
                    y *= scale;
                    z *= scale;

                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    {
                        throw new InvalidDataException("The requested Halo map scale produced a non-finite vertex coordinate.");
                    }

                    writer.Write("v ");
                    writer.Write(x.ToString("R", CultureInfo.InvariantCulture));
                    writer.Write(' ');
                    writer.Write(y.ToString("R", CultureInfo.InvariantCulture));
                    writer.Write(' ');
                    writer.WriteLine(z.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                throw;
            }

            File.Move(tempPath, objPath, overwrite: true);
        }

        public static ExtractResult ExtractReachMapToObj(
            string mapPath,
            string outputObjPath,
            ExtractOptions? options = null,
            Action<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(mapPath)
                || !File.Exists(mapPath))
            {
                throw new FileNotFoundException(
                    "The selected Halo cache map could not be found.",
                    mapPath);
            }

            if (!Path.GetExtension(mapPath)
                    .Equals(
                        ".map",
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Halo Map Import currently expects a Halo MCC .map cache file.");
            }

            options ??=
                new ExtractOptions();

            if (!float.IsFinite(options.WorldScale)
                || options.WorldScale <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Halo map scale must be a positive finite number.");
            }

            string? outputDirectory =
                Path.GetDirectoryName(outputObjPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            progress?.Invoke(
                "Reading Halo Reach cache header and tag index natively...");

            using ReachMccCache cache =
                new ReachMccCache(
                    mapPath);

            List<ReachTag> bspTags =
                cache.GetScenarioBspTags();

            if (bspTags.Count == 0)
            {
                throw new InvalidDataException(
                    "The selected Halo Reach map contains no scenario_structure_bsp (sbsp) tags referenced by its scenario.");
            }

            ExtractResult result =
                new ExtractResult
                {
                    SourceMapPath =
                        Path.GetFullPath(mapPath),

                    ObjPath =
                        Path.GetFullPath(outputObjPath),

                    BuildString =
                        cache.BuildString,

                    CacheImplementation =
                        "Ensemble Native MCC Halo Reach U13-compatible",

                    AppliedScale =
                        options.WorldScale
                };

            if (cache.TryGetScenarioHorizontalSize(
                    out float sourceWidth,
                    out float sourceDepth))
            {
                result.SourceWidth = sourceWidth;
                result.SourceDepth = sourceDepth;
            }

            using StreamWriter writer =
                new StreamWriter(
                    outputObjPath,
                    append: false,
                    new UTF8Encoding(false),
                    bufferSize: 1024 * 1024);

            writer.WriteLine(
                "# ENSHALOOBJ 1");
            writer.WriteLine(
                "# Ensemble native Halo Reach -> Halo Wars intermediate OBJ");
            writer.WriteLine(
                "# Source: " +
                Path.GetFileName(mapPath));
            writer.WriteLine(
                "# Core scenario_structure_bsp cluster geometry only.");
            writer.WriteLine(
                "# Halo FPS objects, scripts and gameplay data are intentionally not transferred.");

            int globalVertexBase =
                1;

            List<string> skippedBsps =
                result.SkippedBspDiagnostics;

            for (int bspIndex = 0;
                 bspIndex < bspTags.Count;
                 bspIndex++)
            {
                ReachTag bsp =
                    bspTags[bspIndex];

                string bspName =
                    string.IsNullOrWhiteSpace(bsp.Name)
                        ? $"sbsp_{bsp.Id:D4}"
                        : bsp.Name;

                progress?.Invoke(
                    $"Reading Reach BSP {bspIndex + 1}/{bspTags.Count}: {bspName}");

                long beforeVertices =
                    result.VertexCount;

                long beforeTriangles =
                    result.TriangleCount;

                int beforePlacements =
                    result.MeshPlacementCount;

                int beforeVertexBase =
                    globalVertexBase;

                try
                {
                    StringBuilder bspBuffer =
                        new();

                    using StringWriter bspWriter =
                        new StringWriter(
                            bspBuffer,
                            CultureInfo.InvariantCulture);

                    cache.ExportBspClusters(
                        bspWriter,
                        bsp,
                        options.WorldScale,
                        result,
                        ref globalVertexBase);

                    if (result.TriangleCount > beforeTriangles)
                    {
                        writer.Write(
                            bspBuffer.ToString());

                        result.BspTags.Add(
                            bspName);
                    }
                    else
                    {
                        // A Reach scenario can reference helper/transition BSPs
                        // that carry metadata but no renderable cluster geometry.
                        // Do not abort the entire campaign-map import for those.
                        result.VertexCount =
                            beforeVertices;

                        result.TriangleCount =
                            beforeTriangles;

                        result.MeshPlacementCount =
                            beforePlacements;

                        globalVertexBase =
                            beforeVertexBase;

                        skippedBsps.Add(
                            $"{bspName}: no renderable cluster triangles");

                        progress?.Invoke(
                            $"Skipping Reach BSP {bspName}: no renderable cluster triangles.");
                    }
                }
                catch (Exception ex) when (
                    ex is InvalidDataException
                    || ex is NotSupportedException
                    || ex is FileNotFoundException)
                {
                    // Reach campaign scenarios commonly contain several BSPs,
                    // including transition/helper BSPs whose resources differ
                    // from the main playable environment.  Reclaimer itself
                    // treats BSP extraction independently; Ensemble should do
                    // the same instead of losing every successfully decoded BSP
                    // because one auxiliary BSP cannot be decoded.
                    result.VertexCount =
                        beforeVertices;

                    result.TriangleCount =
                        beforeTriangles;

                    result.MeshPlacementCount =
                        beforePlacements;

                    globalVertexBase =
                        beforeVertexBase;

                    skippedBsps.Add(
                        $"{bspName}: {ex.Message.Replace(Environment.NewLine, " ")}");

                    progress?.Invoke(
                        $"Skipping unsupported Reach BSP {bspName}; continuing with the remaining BSPs...");
                }
            }

            writer.Flush();

            if (result.TriangleCount <= 0
                || result.VertexCount <= 0)
            {
                string details =
                    skippedBsps.Count > 0
                        ? "\n\nBSP diagnostics:\n - "
                          + string.Join(
                              "\n - ",
                              skippedBsps.Take(8))
                        : string.Empty;

                throw new InvalidDataException(
                    "The native Reach reader located the map's BSP metadata but no triangle geometry was produced.\n\n" +
                    "This can happen if the selected cache stores the required BSP resource page in a sibling shared/campaign .map that is not present, " +
                    "or if every BSP in the cache uses a Reach resource/vertex layout that the current native reader does not yet cover." +
                    details);
            }

            if (skippedBsps.Count > 0)
            {
                progress?.Invoke(
                    $"Imported {result.BspTags.Count} Reach BSP(s); skipped {skippedBsps.Count} auxiliary/unsupported BSP(s).");
            }

            progress?.Invoke(
                $"Native Reach extraction complete: {result.TriangleCount:N0} triangles.");

            return result;
        }

        private sealed class ReachMccCache : IDisposable
        {
            private const int HeaderSize = 40960;
            private const int ExpectedCacheVersion = 13;
            private const long DefaultPointerMagic = 0x50000000L;
            private const long EarlyMccPointerMagic = 0x10000000L;

            private readonly string _mapPath;
            private readonly FileStream _stream;
            private readonly BinaryReader _reader;

            private readonly long _metadataMagic;
            private readonly uint _section0Magic;
            private readonly long _pointerMagic;
            private readonly uint _dataTableAddress;

            private readonly Dictionary<int, ReachTag> _tagsById =
                new();

            private readonly List<ReachTag> _tags =
                new();

            private readonly List<ResourceEntry> _resourceEntries =
                new();

            private readonly Dictionary<int, List<ResourceEntry>> _resourcesByOwner =
                new();

            private readonly List<string> _sharedCaches =
                new();

            private readonly List<PageInfo> _pages =
                new();

            private readonly List<SegmentInfo> _segments =
                new();

            private long _fixupDataOffset;
            private int _fixupDataSize;
            private int _scenarioTagId = -1;
            private List<int>? _scenarioBspIds;
            private int _scenarioLightmapTagId = -1;

            public string BuildString { get; }

            public string ScenarioName { get; }

            public ReachMccCache(
                string mapPath)
            {
                _mapPath =
                    Path.GetFullPath(mapPath);

                _stream =
                    new FileStream(
                        _mapPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 1024,
                        FileOptions.RandomAccess);

                _reader =
                    new BinaryReader(
                        _stream,
                        Encoding.UTF8,
                        leaveOpen: true);

                ValidateHeaderSignature();

                if (_stream.Length < HeaderSize)
                {
                    throw new InvalidDataException(
                        "The selected .map is too small to contain an MCC Halo Reach cache header.");
                }

                int cacheVersion =
                    ReadInt32At(4);

                if (cacheVersion != ExpectedCacheVersion)
                {
                    throw new NotSupportedException(
                        "The native importer currently targets MCC Halo Reach cache version 13.\n\n" +
                        $"Detected cache version: {cacheVersion}");
                }

                BuildString =
                    ReadFixedString(
                        160,
                        32);

                ScenarioName =
                    ReadFixedString(
                        224,
                        256);

                ulong virtualBaseAddress =
                    ReadUInt64At(736);

                ulong indexPointer =
                    ReadUInt64At(744);

                uint[] sectionOffsets =
                    Enumerable.Range(0, 4)
                        .Select(
                            i =>
                                ReadUInt32At(
                                    1228 + i * 4))
                        .ToArray();

                uint[] sectionAddresses =
                    Enumerable.Range(0, 4)
                        .Select(
                            i =>
                                ReadUInt32At(
                                    1244 + i * 8))
                        .ToArray();

                uint section2Physical =
                    unchecked(
                        sectionAddresses[2]
                        + sectionOffsets[2]);

                _metadataMagic =
                    checked(
                        (long)virtualBaseAddress
                        - section2Physical);

                uint section0Physical =
                    unchecked(
                        sectionAddresses[0]
                        + sectionOffsets[0]);

                _section0Magic =
                    unchecked(
                        sectionAddresses[0]
                        - section0Physical);

                _pointerMagic =
                    BuildString is
                        "Jun 24 2019 00:36:03"
                        or "Jul 30 2019 14:17:16"
                            ? EarlyMccPointerMagic
                            : DefaultPointerMagic;

                _dataTableAddress =
                    ReadUInt32At(1232);

                long tagIndexOffset =
                    TranslateExpandedMetadataPointer(
                        checked((long)indexPointer));

                ParseTagIndex(
                    tagIndexOffset);

                ParseScenarioReferences();
                ParseResourceCatalog();
                ParseResourceLayoutTable();
            }

            public void Dispose()
            {
                _reader.Dispose();
                _stream.Dispose();
            }

            public List<ReachTag> GetScenarioBspTags()
            {
                if (_scenarioBspIds != null
                    && _scenarioBspIds.Count > 0)
                {
                    return _scenarioBspIds
                        .Select(
                            id =>
                                _tagsById.GetValueOrDefault(id))
                        .Where(
                            tag =>
                                tag != null
                                && tag.ClassCode.Equals(
                                    "sbsp",
                                    StringComparison.Ordinal))
                        .Cast<ReachTag>()
                        .ToList();
                }

                return _tags
                    .Where(
                        tag =>
                            tag.ClassCode.Equals(
                                "sbsp",
                                StringComparison.Ordinal))
                    .ToList();
            }

            public bool TryGetScenarioHorizontalSize(
                out float width,
                out float depth)
            {
                width = 0;
                depth = 0;

                List<ReachTag> bsps =
                    GetScenarioBspTags();

                bool any = false;
                float minX = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float minY = float.PositiveInfinity;
                float maxY = float.NegativeInfinity;

                foreach (ReachTag bsp in bsps)
                {
                    float x0 = ReadFloatAt(bsp.MetadataOffset + 240);
                    float x1 = ReadFloatAt(bsp.MetadataOffset + 244);
                    float y0 = ReadFloatAt(bsp.MetadataOffset + 248);
                    float y1 = ReadFloatAt(bsp.MetadataOffset + 252);

                    if (!float.IsFinite(x0) || !float.IsFinite(x1) ||
                        !float.IsFinite(y0) || !float.IsFinite(y1))
                    {
                        continue;
                    }

                    float localMinX = Math.Min(x0, x1);
                    float localMaxX = Math.Max(x0, x1);
                    float localMinY = Math.Min(y0, y1);
                    float localMaxY = Math.Max(y0, y1);

                    if (localMaxX - localMinX <= 0.0001f ||
                        localMaxY - localMinY <= 0.0001f)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, localMinX);
                    maxX = Math.Max(maxX, localMaxX);
                    minY = Math.Min(minY, localMinY);
                    maxY = Math.Max(maxY, localMaxY);
                    any = true;
                }

                if (!any)
                    return false;

                width = maxX - minX;
                depth = maxY - minY;

                return float.IsFinite(width) && width > 0.0001f &&
                       float.IsFinite(depth) && depth > 0.0001f;
            }

            public void ExportBspClusters(
                TextWriter writer,
                ReachTag bsp,
                float worldScale,
                ExtractResult result,
                ref int globalVertexBase)
            {
                ReachBlock clusterBlock =
                    ReadBlock(
                        bsp.MetadataOffset + 312);

                List<int> clusterSections =
                    new();

                for (int i = 0;
                     i < clusterBlock.Count;
                     i++)
                {
                    long clusterOffset =
                        clusterBlock.Offset
                        + i * 140L;

                    short sectionIndex =
                        ReadInt16At(
                            clusterOffset + 64);

                    if (sectionIndex >= 0)
                        clusterSections.Add(sectionIndex);
                }

                List<ReachSection> sections;
                ResourceEntry? preferredResource =
                    null;

                if (TryResolveScenarioLightmapGeometry(
                        bsp.Id,
                        out List<ReachSection>? lightmapSections,
                        out ResourceEntry? lightmapResource))
                {
                    sections =
                        lightmapSections!;

                    preferredResource =
                        lightmapResource;
                }
                else
                {
                    sections =
                        ParseSections(
                            ReadBlock(
                                bsp.MetadataOffset + 1104));
                }

                if (sections.Count == 0)
                {
                    throw new InvalidDataException(
                        $"BSP '{bsp.Name}' contains no readable Reach render sections.");
                }

                List<int> sectionIndices =
                    clusterSections
                        .Where(
                            index =>
                                index >= 0
                                && index < sections.Count)
                        .Distinct()
                        .ToList();

                if (sectionIndices.Count == 0)
                {
                    // Some special-purpose Reach caches omit cluster metadata.
                    // In that case use all render sections rather than silently
                    // producing an empty map.
                    sectionIndices =
                        Enumerable.Range(
                                0,
                                sections.Count)
                            .ToList();
                }

                List<ReachBoundingBox> bounds =
                    ParseBoundingBoxes(
                        ReadBlock(
                            bsp.MetadataOffset + 1116));

                GeometryResourceLayout layout =
                    SelectGeometryResource(
                        bsp,
                        sections,
                        sectionIndices,
                        preferredResource);

                byte[] resourceData =
                    ReadResourceSegment(
                        layout.Entry,
                        layout.RequiredLength);

                foreach (int sectionIndex
                         in sectionIndices)
                {
                    ReachSection section =
                        sections[sectionIndex];

                    ReachBoundingBox? sectionBounds =
                        sectionIndex < bounds.Count
                            ? bounds[sectionIndex]
                            : null;

                    ExportSection(
                        writer,
                        bsp,
                        sectionIndex,
                        section,
                        sectionBounds,
                        layout,
                        resourceData,
                        worldScale,
                        result,
                        ref globalVertexBase);
                }
            }

            private void ParseTagIndex(
                long tagIndexOffset)
            {
                ValidateRange(
                    tagIndexOffset,
                    76,
                    "tag index");

                int classCount =
                    ReadInt32At(
                        tagIndexOffset);

                ulong classPointer =
                    ReadUInt64At(
                        tagIndexOffset + 8);

                int tagCount =
                    ReadInt32At(
                        tagIndexOffset + 16);

                ulong tagPointer =
                    ReadUInt64At(
                        tagIndexOffset + 24);

                if (classCount <= 0
                    || classCount > 4096
                    || tagCount <= 0
                    || tagCount > 500000)
                {
                    throw new InvalidDataException(
                        "The MCC Reach tag index contains implausible class/tag counts.");
                }

                long classOffset =
                    TranslateExpandedMetadataPointer(
                        checked((long)classPointer));

                long tagOffset =
                    TranslateExpandedMetadataPointer(
                        checked((long)tagPointer));

                ValidateRange(
                    classOffset,
                    classCount * 16L,
                    "tag class table");

                ValidateRange(
                    tagOffset,
                    tagCount * 8L,
                    "tag item table");

                List<string> classes =
                    new(classCount);

                for (int i = 0;
                     i < classCount;
                     i++)
                {
                    uint classId =
                        ReadUInt32At(
                            classOffset + i * 16L);

                    classes.Add(
                        FourCcFromClassId(
                            classId));
                }

                string?[] names =
                    ReadTagNames(
                        tagCount);

                for (int i = 0;
                     i < tagCount;
                     i++)
                {
                    long itemOffset =
                        tagOffset
                        + i * 8L;

                    short classIndex =
                        ReadInt16At(
                            itemOffset);

                    if (classIndex < 0
                        || classIndex >= classes.Count)
                    {
                        continue;
                    }

                    uint compressedMetadataPointer =
                        ReadUInt32At(
                            itemOffset + 4);

                    long metadataOffset =
                        TranslateCompressedMetadataPointer(
                            compressedMetadataPointer);

                    if (metadataOffset < 0
                        || metadataOffset >= _stream.Length)
                    {
                        continue;
                    }

                    ReachTag tag =
                        new ReachTag(
                            i,
                            classes[classIndex],
                            names.ElementAtOrDefault(i)
                                ?? string.Empty,
                            metadataOffset);

                    _tags.Add(tag);
                    _tagsById[i] = tag;
                }

                ReachTag? scenario =
                    _tags
                        .Where(
                            tag =>
                                tag.ClassCode.Equals(
                                    "scnr",
                                    StringComparison.Ordinal))
                        .FirstOrDefault(
                            tag =>
                                tag.Name.Equals(
                                    ScenarioName,
                                    StringComparison.OrdinalIgnoreCase))
                    ??
                    _tags
                        .FirstOrDefault(
                            tag =>
                                tag.ClassCode.Equals(
                                    "scnr",
                                    StringComparison.Ordinal));

                if (scenario != null)
                    _scenarioTagId = scenario.Id;
            }

            private string?[] ReadTagNames(
                int tagCount)
            {
                string?[] result =
                    new string?[tagCount];

                try
                {
                    uint tablePointer =
                        ReadUInt32At(36);

                    int tableSize =
                        ReadInt32At(40);

                    uint indexPointer =
                        ReadUInt32At(44);

                    long tableOffset =
                        TranslateSection0Pointer(
                            tablePointer);

                    long indicesOffset =
                        TranslateSection0Pointer(
                            indexPointer);

                    if (tableSize <= 0)
                        return result;

                    ValidateRange(
                        tableOffset,
                        tableSize,
                        "tag name table");

                    ValidateRange(
                        indicesOffset,
                        tagCount * 4L,
                        "tag name index table");

                    byte[] table =
                        ReadBytesAt(
                            tableOffset,
                            tableSize);

                    for (int i = 0;
                         i < tagCount;
                         i++)
                    {
                        int offset =
                            ReadInt32At(
                                indicesOffset + i * 4L);

                        if (offset < 0
                            || offset >= table.Length)
                        {
                            continue;
                        }

                        int end =
                            Array.IndexOf(
                                table,
                                (byte)0,
                                offset);

                        if (end < 0)
                            end = table.Length;

                        result[i] =
                            Encoding.UTF8.GetString(
                                table,
                                offset,
                                end - offset);
                    }
                }
                catch
                {
                    // Tag names are diagnostic/editor labels only.  The cache
                    // remains importable by tag id/class if this table is absent.
                }

                return result;
            }

            private void ParseScenarioReferences()
            {
                if (_scenarioTagId < 0
                    || !_tagsById.TryGetValue(
                        _scenarioTagId,
                        out ReachTag? scenario))
                {
                    return;
                }

                ReachBlock bspBlock =
                    ReadBlock(
                        scenario.MetadataOffset + 80);

                List<int> bspIds =
                    new();

                for (int i = 0;
                     i < bspBlock.Count;
                     i++)
                {
                    long entry =
                        bspBlock.Offset
                        + i * 172L;

                    int id =
                        ReadTagReferenceId(
                            entry);

                    if (id >= 0)
                        bspIds.Add(id);
                }

                _scenarioBspIds =
                    bspIds;

                _scenarioLightmapTagId =
                    ReadTagReferenceId(
                        scenario.MetadataOffset + 1800);
            }

            private void ParseResourceCatalog()
            {
                ReachTag zone =
                    _tags
                        .FirstOrDefault(
                            tag =>
                                tag.ClassCode.Equals(
                                    "zone",
                                    StringComparison.Ordinal))
                    ?? throw new InvalidDataException(
                        "The Reach cache contains no resource gestalt (zone) tag.");

                ReachBlock resourceBlock =
                    ReadBlock(
                        zone.MetadataOffset + 100);

                _fixupDataSize =
                    ReadInt32At(
                        zone.MetadataOffset + 328);

                uint fixupPointer =
                    ReadUInt32At(
                        zone.MetadataOffset + 340);

                _fixupDataOffset =
                    TranslateCompressedMetadataPointer(
                        fixupPointer);

                if (_fixupDataSize <= 0)
                {
                    throw new InvalidDataException(
                        "The Reach resource gestalt contains no fixup data.");
                }

                ValidateRange(
                    _fixupDataOffset,
                    _fixupDataSize,
                    "resource fixup data");

                for (int i = 0;
                     i < resourceBlock.Count;
                     i++)
                {
                    long entryOffset =
                        resourceBlock.Offset
                        + i * 64L;

                    int ownerTagId =
                        ReadTagReferenceId(
                            entryOffset);

                    int fixupOffset =
                        ReadInt32At(
                            entryOffset + 20);

                    int fixupSize =
                        ReadInt32At(
                            entryOffset + 24);

                    short segmentIndex =
                        ReadInt16At(
                            entryOffset + 34);

                    ReachBlock resourceFixupBlock =
                        ReadBlock(
                            entryOffset + 40);

                    List<int> resourceFixups =
                        new(resourceFixupBlock.Count);

                    for (int r = 0;
                         r < resourceFixupBlock.Count;
                         r++)
                    {
                        int encodedOffset =
                            ReadInt32At(
                                resourceFixupBlock.Offset
                                + r * 8L
                                + 4);

                        resourceFixups.Add(
                            encodedOffset
                            & 0x0FFFFFFF);
                    }

                    ResourceEntry entry =
                        new ResourceEntry(
                            i,
                            ownerTagId,
                            fixupOffset,
                            fixupSize,
                            segmentIndex,
                            resourceFixups);

                    _resourceEntries.Add(
                        entry);

                    if (!_resourcesByOwner.TryGetValue(
                            ownerTagId,
                            out List<ResourceEntry>? ownerEntries))
                    {
                        ownerEntries =
                            new List<ResourceEntry>();

                        _resourcesByOwner[ownerTagId] =
                            ownerEntries;
                    }

                    ownerEntries.Add(
                        entry);
                }
            }

            private void ParseResourceLayoutTable()
            {
                ReachTag play =
                    _tags
                        .FirstOrDefault(
                            tag =>
                                tag.ClassCode.Equals(
                                    "play",
                                    StringComparison.Ordinal))
                    ?? throw new InvalidDataException(
                        "The Reach cache contains no resource layout (play) tag.");

                ReachBlock sharedBlock =
                    ReadBlock(
                        play.MetadataOffset + 12);

                for (int i = 0;
                     i < sharedBlock.Count;
                     i++)
                {
                    _sharedCaches.Add(
                        ReadFixedString(
                            sharedBlock.Offset
                            + i * 264L,
                            32));
                }

                ReachBlock pageBlock =
                    ReadBlock(
                        play.MetadataOffset + 24);

                for (int i = 0;
                     i < pageBlock.Count;
                     i++)
                {
                    long pageOffset =
                        pageBlock.Offset
                        + i * 88L;

                    _pages.Add(
                        new PageInfo(
                            ReadInt16At(
                                pageOffset + 4),
                            ReadInt32At(
                                pageOffset + 8),
                            ReadInt32At(
                                pageOffset + 12),
                            ReadInt32At(
                                pageOffset + 16)));
                }

                ReachBlock segmentBlock =
                    ReadBlock(
                        play.MetadataOffset + 60);

                for (int i = 0;
                     i < segmentBlock.Count;
                     i++)
                {
                    long segmentOffset =
                        segmentBlock.Offset
                        + i * 16L;

                    _segments.Add(
                        new SegmentInfo(
                            ReadInt16At(
                                segmentOffset),
                            ReadInt16At(
                                segmentOffset + 2),
                            ReadInt32At(
                                segmentOffset + 4),
                            ReadInt32At(
                                segmentOffset + 8)));
                }
            }

            private bool TryResolveScenarioLightmapGeometry(
                int bspTagId,
                out List<ReachSection>? sections,
                out ResourceEntry? resource)
            {
                sections = null;
                resource = null;

                if (_scenarioLightmapTagId < 0
                    || _scenarioBspIds == null)
                {
                    return false;
                }

                int bspIndex =
                    _scenarioBspIds.IndexOf(
                        bspTagId);

                if (bspIndex < 0
                    || !_tagsById.TryGetValue(
                        _scenarioLightmapTagId,
                        out ReachTag? lightmap))
                {
                    return false;
                }

                ReachBlock lightmapRefs =
                    ReadBlock(
                        lightmap.MetadataOffset + 4);

                if (bspIndex >= lightmapRefs.Count)
                    return false;

                long lightmapRefOffset =
                    lightmapRefs.Offset
                    + bspIndex * 32L;

                int dataTagId =
                    ReadTagReferenceId(
                        lightmapRefOffset);

                if (dataTagId < 0
                    || !_tagsById.TryGetValue(
                        dataTagId,
                        out ReachTag? dataTag))
                {
                    return false;
                }

                ReachBlock sectionBlock =
                    ReadBlock(
                        dataTag.MetadataOffset + 124);

                List<ReachSection> parsedSections =
                    ParseSections(
                        sectionBlock);

                if (parsedSections.Count == 0)
                    return false;

                int resourceIdentifier =
                    ReadInt32At(
                        dataTag.MetadataOffset + 268);

                int resourceIndex =
                    resourceIdentifier
                    & ushort.MaxValue;

                if (resourceIndex < 0
                    || resourceIndex >= _resourceEntries.Count)
                {
                    return false;
                }

                sections =
                    parsedSections;

                resource =
                    _resourceEntries[resourceIndex];

                return true;
            }

            private List<ReachSection> ParseSections(
                ReachBlock block)
            {
                List<ReachSection> result =
                    new(block.Count);

                for (int i = 0;
                     i < block.Count;
                     i++)
                {
                    long sectionOffset =
                        block.Offset
                        + i * 92L;

                    ReachBlock submeshBlock =
                        ReadBlock(
                            sectionOffset);

                    List<ReachSubmesh> submeshes =
                        new(submeshBlock.Count);

                    for (int s = 0;
                         s < submeshBlock.Count;
                         s++)
                    {
                        long submeshOffset =
                            submeshBlock.Offset
                            + s * 24L;

                        submeshes.Add(
                            new ReachSubmesh(
                                ReadInt32At(
                                    submeshOffset + 4),
                                ReadInt32At(
                                    submeshOffset + 8)));
                    }

                    result.Add(
                        new ReachSection(
                            ReadInt16At(
                                sectionOffset + 24),
                            ReadInt16At(
                                sectionOffset + 40),
                            ReadUInt16At(
                                sectionOffset + 44),
                            ReadByteAt(
                                sectionOffset + 47),
                            ReadByteAt(
                                sectionOffset + 50),
                            submeshes));
                }

                return result;
            }

            private List<ReachBoundingBox> ParseBoundingBoxes(
                ReachBlock block)
            {
                List<ReachBoundingBox> result =
                    new(block.Count);

                for (int i = 0;
                     i < block.Count;
                     i++)
                {
                    long offset =
                        block.Offset
                        + i * 52L;

                    result.Add(
                        new ReachBoundingBox(
                            ReadFloatAt(offset + 4),
                            ReadFloatAt(offset + 8),
                            ReadFloatAt(offset + 12),
                            ReadFloatAt(offset + 16),
                            ReadFloatAt(offset + 20),
                            ReadFloatAt(offset + 24)));
                }

                return result;
            }

            private GeometryResourceLayout SelectGeometryResource(
                ReachTag bsp,
                IReadOnlyList<ReachSection> sections,
                IReadOnlyList<int> sectionIndices,
                ResourceEntry? preferred)
            {
                HashSet<int> attempted =
                    new();

                GeometryResourceLayout? bestDirect =
                    null;

                void ConsiderDirect(
                    ResourceEntry candidate)
                {
                    if (!attempted.Add(
                            candidate.Index))
                    {
                        return;
                    }

                    GeometryResourceLayout? parsed =
                        TryParseGeometryResource(
                            candidate,
                            sections,
                            sectionIndices);

                    if (parsed == null)
                        return;

                    if (bestDirect == null
                        || parsed.Score > bestDirect.Score)
                    {
                        bestDirect =
                            parsed;
                    }
                }

                // The scenario lightmap resource identifier is the strongest
                // source of truth and matches the path used by Halo Reach's
                // normal render pipeline.
                if (preferred != null)
                {
                    ConsiderDirect(
                        preferred);
                }

                // Some special BSPs own their resource directly instead of
                // routing it through scenario_lightmap_bsp_data.
                if (_resourcesByOwner.TryGetValue(
                        bsp.Id,
                        out List<ResourceEntry>? owned))
                {
                    foreach (ResourceEntry candidate
                             in owned)
                    {
                        ConsiderDirect(
                            candidate);
                    }
                }

                if (bestDirect != null)
                    return bestDirect;

                // U13 campaign maps are not completely uniform.  A handful of
                // transition/streaming BSPs reference geometry through resource
                // owners that are neither the sbsp tag nor the lightmap tag
                // exposed by the scenario block.  The resource gestalt already
                // contains every resource entry, so perform a metadata-only
                // compatibility scan before declaring the BSP unsupported.
                // No page data is decompressed during this scan.
                GeometryResourceLayout? bestFallback =
                    null;

                int supportedRequested =
                    sectionIndices.Count(
                        sectionIndex =>
                            sectionIndex >= 0
                            && sectionIndex < sections.Count
                            && IsSupportedVertexFormat(
                                sections[sectionIndex].VertexFormat));

                foreach (ResourceEntry candidate
                         in _resourceEntries)
                {
                    if (!attempted.Add(
                            candidate.Index))
                    {
                        continue;
                    }

                    GeometryResourceLayout? parsed =
                        TryParseGeometryResource(
                            candidate,
                            sections,
                            sectionIndices);

                    if (parsed == null)
                        continue;

                    if (bestFallback == null
                        || parsed.Score > bestFallback.Score)
                    {
                        bestFallback =
                            parsed;
                    }

                    // Score is coveredSections * 1,000,000 plus a vertex
                    // tiebreaker below one million.  Keep scanning the small
                    // metadata catalog so the most complete compatible layout
                    // wins instead of depending on resource-entry order.
                }

                if (bestFallback != null)
                    return bestFallback;

                string formats =
                    string.Join(
                        ", ",
                        sectionIndices
                            .Where(
                                index =>
                                    index >= 0
                                    && index < sections.Count)
                            .Select(
                                index =>
                                    $"0x{sections[index].VertexFormat:X2}")
                            .Distinct());

                throw new InvalidDataException(
                    $"Ensemble found BSP '{bsp.Name}' but could not locate a compatible Reach vertex/index resource for its cluster sections.\n\n" +
                    $"Requested cluster sections: {sectionIndices.Count}; supported vertex-format sections: {supportedRequested}; formats: {formats}.\n" +
                    $"Resource entries checked: {attempted.Count}.\n\n" +
                    "Ensemble will skip this BSP when other playable BSPs exist in the same scenario. If every BSP is skipped, keep the source map beside its MCC shared/campaign resource maps and report the BSP diagnostics shown by Ensemble.");
            }

            private GeometryResourceLayout? TryParseGeometryResource(
                ResourceEntry entry,
                IReadOnlyList<ReachSection> sections,
                IReadOnlyList<int> sectionIndices)
            {
                if (entry.SegmentIndex < 0
                    || entry.SegmentIndex >= _segments.Count
                    || entry.FixupOffset < 0
                    || entry.FixupSize < 24
                    || entry.FixupOffset >
                        _fixupDataSize - entry.FixupSize)
                {
                    return null;
                }

                long tailOffset =
                    _fixupDataOffset
                    + entry.FixupOffset
                    + entry.FixupSize
                    - 24L;

                int vertexBufferCount =
                    ReadInt32At(
                        tailOffset);

                int indexBufferCount =
                    ReadInt32At(
                        tailOffset + 12);

                if (vertexBufferCount <= 0
                    || vertexBufferCount > 4096
                    || indexBufferCount < 0
                    || indexBufferCount > 4096)
                {
                    return null;
                }

                long infoBase =
                    _fixupDataOffset
                    + entry.FixupOffset;

                long indexInfoBase =
                    infoBase
                    + vertexBufferCount * 28L
                    + vertexBufferCount * 12L;

                if (indexInfoBase
                        + indexBufferCount * 28L
                    > tailOffset)
                {
                    return null;
                }

                List<VertexBufferInfo> vertexBuffers =
                    new(vertexBufferCount);

                for (int i = 0;
                     i < vertexBufferCount;
                     i++)
                {
                    long offset =
                        infoBase
                        + i * 28L;

                    vertexBuffers.Add(
                        new VertexBufferInfo(
                            ReadInt32At(offset),
                            ReadInt32At(offset + 8)));
                }

                List<IndexBufferInfo> indexBuffers =
                    new(indexBufferCount);

                for (int i = 0;
                     i < indexBufferCount;
                     i++)
                {
                    long offset =
                        indexInfoBase
                        + i * 28L;

                    indexBuffers.Add(
                        new IndexBufferInfo(
                            ReadInt32At(offset),
                            ReadInt32At(offset + 8)));
                }

                int requiredLength =
                    0;

                int coveredSections =
                    0;

                long totalVertices =
                    0;

                foreach (int sectionIndex
                         in sectionIndices)
                {
                    if (sectionIndex < 0
                        || sectionIndex >= sections.Count)
                    {
                        continue;
                    }

                    ReachSection section =
                        sections[sectionIndex];

                    if (!IsSupportedVertexFormat(
                            section.VertexFormat))
                    {
                        continue;
                    }

                    if (section.VertexBufferIndex < 0
                        || section.VertexBufferIndex >= vertexBuffers.Count
                        || section.VertexBufferIndex >= entry.ResourceFixups.Count)
                    {
                        continue;
                    }

                    VertexBufferInfo vInfo =
                        vertexBuffers[
                            section.VertexBufferIndex];

                    if (vInfo.VertexCount <= 0
                        || vInfo.DataLength <= 0)
                    {
                        continue;
                    }

                    int vertexOffset =
                        entry.ResourceFixups[
                            section.VertexBufferIndex];

                    if (!TryAccumulateRequiredLength(
                            vertexOffset,
                            vInfo.DataLength,
                            ref requiredLength))
                    {
                        return null;
                    }

                    if (!section.IsUnindexed)
                    {
                        if (section.IndexBufferIndex < 0
                            || section.IndexBufferIndex >= indexBuffers.Count)
                        {
                            continue;
                        }

                        int fixupIndex =
                            vertexBuffers.Count * 2
                            + section.IndexBufferIndex;

                        if (fixupIndex < 0
                            || fixupIndex >= entry.ResourceFixups.Count)
                        {
                            continue;
                        }

                        IndexBufferInfo iInfo =
                            indexBuffers[
                                section.IndexBufferIndex];

                        if (iInfo.DataLength <= 0)
                            continue;

                        int indexOffset =
                            entry.ResourceFixups[
                                fixupIndex];

                        if (!TryAccumulateRequiredLength(
                                indexOffset,
                                iInfo.DataLength,
                                ref requiredLength))
                        {
                            return null;
                        }
                    }

                    coveredSections++;
                    totalVertices +=
                        vInfo.VertexCount;
                }

                if (coveredSections <= 0
                    || requiredLength <= 0)
                {
                    return null;
                }

                int score =
                    checked(
                        coveredSections * 1_000_000
                        + (int)Math.Min(
                            999_999,
                            totalVertices));

                return new GeometryResourceLayout(
                    entry,
                    vertexBuffers,
                    indexBuffers,
                    requiredLength,
                    score);
            }

            private byte[] ReadResourceSegment(
                ResourceEntry entry,
                int requiredLength)
            {
                if (entry.SegmentIndex < 0
                    || entry.SegmentIndex >= _segments.Count)
                {
                    throw new InvalidDataException(
                        "Reach BSP resource points to an invalid segment.");
                }

                SegmentInfo segment =
                    _segments[
                        entry.SegmentIndex];

                bool useSecondary =
                    segment.SecondaryPageIndex >= 0;

                int pageIndex =
                    useSecondary
                        ? segment.SecondaryPageIndex
                        : segment.PrimaryPageIndex;

                int segmentOffset =
                    useSecondary
                        ? segment.SecondaryPageOffset
                        : segment.PrimaryPageOffset;

                if (pageIndex < 0
                    || pageIndex >= _pages.Count
                    || segmentOffset < 0)
                {
                    throw new InvalidDataException(
                        "Reach BSP resource segment has no readable page.");
                }

                PageInfo page =
                    _pages[pageIndex];

                if (useSecondary
                    && (page.DataOffset < 0
                        || page.CompressedSize <= 0))
                {
                    pageIndex =
                        segment.PrimaryPageIndex;

                    segmentOffset =
                        segment.PrimaryPageOffset;

                    if (pageIndex < 0
                        || pageIndex >= _pages.Count
                        || segmentOffset < 0)
                    {
                        throw new InvalidDataException(
                            "Reach BSP resource fallback page is invalid.");
                    }

                    page =
                        _pages[pageIndex];
                }

                if (page.DataOffset < 0
                    || page.CompressedSize <= 0
                    || page.DecompressedSize <= 0)
                {
                    throw new InvalidDataException(
                        "Reach BSP resource page contains no data.");
                }

                if (segmentOffset >
                        page.DecompressedSize
                        - requiredLength)
                {
                    throw new InvalidDataException(
                        "Reach BSP resource data extends beyond its decompressed page.");
                }

                string targetFile =
                    ResolveResourceFile(
                        page.CacheIndex);

                using FileStream resourceStream =
                    new FileStream(
                        targetFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 1024,
                        FileOptions.SequentialScan);

                if (resourceStream.Length < 1236)
                {
                    throw new InvalidDataException(
                        $"Resource cache '{Path.GetFileName(targetFile)}' is too small to contain an MCC Reach data table.");
                }

                resourceStream.Position =
                    1232;

                Span<byte> dataTableBytes =
                    stackalloc byte[4];

                ReadExactly(
                    resourceStream,
                    dataTableBytes);

                uint targetDataTableAddress =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        dataTableBytes);

                long encodedOffset =
                    checked(
                        (long)targetDataTableAddress
                        + page.DataOffset);

                if (encodedOffset < 0
                    || encodedOffset >= resourceStream.Length)
                {
                    throw new InvalidDataException(
                        $"Reach resource page points outside '{Path.GetFileName(targetFile)}'.");
                }

                resourceStream.Position =
                    encodedOffset;

                if (page.CompressedSize ==
                    page.DecompressedSize)
                {
                    resourceStream.Position =
                        checked(
                            encodedOffset
                            + segmentOffset);

                    byte[] direct =
                        new byte[requiredLength];

                    ReadExactly(
                        resourceStream,
                        direct);

                    return direct;
                }

                using DeflateStream deflate =
                    new DeflateStream(
                        resourceStream,
                        CompressionMode.Decompress,
                        leaveOpen: false);

                SkipExactly(
                    deflate,
                    segmentOffset);

                byte[] result =
                    new byte[requiredLength];

                ReadExactly(
                    deflate,
                    result);

                return result;
            }

            private string ResolveResourceFile(
                short cacheIndex)
            {
                if (cacheIndex < 0)
                    return _mapPath;

                if (cacheIndex >= _sharedCaches.Count)
                {
                    throw new InvalidDataException(
                        $"Reach resource page refers to missing shared-cache index {cacheIndex}.");
                }

                string raw =
                    _sharedCaches[cacheIndex];

                string normalised =
                    raw.Replace(
                            '\\',
                            '/')
                        .Trim();

                string fileName =
                    normalised
                        .Split(
                            '/',
                            StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault()
                    ?? normalised;

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new InvalidDataException(
                        $"Reach shared-cache index {cacheIndex} has no file name.");
                }

                string directory =
                    Path.GetDirectoryName(_mapPath)
                    ?? string.Empty;

                string candidate =
                    Path.Combine(
                        directory,
                        fileName);

                if (!File.Exists(candidate))
                {
                    throw new FileNotFoundException(
                        "This Halo Reach BSP uses a sibling MCC resource map.\n\n" +
                        $"Required file: {fileName}\n" +
                        $"Expected beside: {Path.GetFileName(_mapPath)}\n\n" +
                        "Copy/use the source .map from its normal MCC Reach maps directory so Ensemble can resolve shared resources.",
                        candidate);
                }

                return candidate;
            }

            private void ExportSection(
                TextWriter writer,
                ReachTag bsp,
                int sectionIndex,
                ReachSection section,
                ReachBoundingBox? bounds,
                GeometryResourceLayout layout,
                byte[] resourceData,
                float worldScale,
                ExtractResult result,
                ref int globalVertexBase)
            {
                if (section.VertexBufferIndex < 0
                    || section.VertexBufferIndex >= layout.VertexBuffers.Count)
                {
                    return;
                }

                VertexBufferInfo vInfo =
                    layout.VertexBuffers[
                        section.VertexBufferIndex];

                if (vInfo.VertexCount <= 0
                    || vInfo.DataLength <= 0)
                {
                    return;
                }

                int vertexFixupIndex =
                    section.VertexBufferIndex;

                if (vertexFixupIndex < 0
                    || vertexFixupIndex >= layout.Entry.ResourceFixups.Count)
                {
                    return;
                }

                int vertexDataOffset =
                    layout.Entry.ResourceFixups[
                        vertexFixupIndex];

                int minimumStride =
                    GetMinimumVertexStride(
                        section.VertexFormat);

                if (minimumStride <= 0)
                    return;

                int stride =
                    vInfo.DataLength /
                    Math.Max(
                        1,
                        vInfo.VertexCount);

                if (stride < minimumStride
                    || (long)vertexDataOffset
                        + (long)stride * vInfo.VertexCount
                        > resourceData.Length)
                {
                    throw new InvalidDataException(
                        $"Reach BSP section {sectionIndex} has an invalid vertex buffer layout.");
                }

                string groupName =
                    SanitizeObjName(
                        (string.IsNullOrWhiteSpace(bsp.Name)
                            ? $"sbsp_{bsp.Id:D4}"
                            : bsp.Name)
                        +
                        $"_cluster_{sectionIndex:D3}");

                writer.WriteLine(
                    "g " +
                    groupName);

                Vector3[] positions =
                    new Vector3[vInfo.VertexCount];

                Vector3[] normals =
                    new Vector3[vInfo.VertexCount];

                for (int i = 0;
                     i < vInfo.VertexCount;
                     i++)
                {
                    int offset =
                        checked(
                            vertexDataOffset
                            + i * stride);

                    DecodeVertex(
                        resourceData,
                        offset,
                        section.VertexFormat,
                        out Vector3 position,
                        out Vector3 normal);

                    if (bounds != null
                        && bounds.HasPositionBounds)
                    {
                        position =
                            bounds.ExpandPosition(
                                position);
                    }

                    position =
                        new Vector3(
                            position.X,
                            position.Z,
                            -position.Y)
                        * worldScale;

                    normal =
                        new Vector3(
                            normal.X,
                            normal.Z,
                            -normal.Y);

                    if (!float.IsFinite(normal.X)
                        || !float.IsFinite(normal.Y)
                        || !float.IsFinite(normal.Z)
                        || normal.LengthSquared() < 0.000001f)
                    {
                        normal =
                            Vector3.UnitY;
                    }
                    else
                    {
                        normal =
                            Vector3.Normalize(
                                normal);
                    }

                    positions[i] =
                        position;

                    normals[i] =
                        normal;

                    writer.Write("v ");
                    WriteFloat(writer, position.X);
                    writer.Write(' ');
                    WriteFloat(writer, position.Y);
                    writer.Write(' ');
                    WriteFloat(writer, position.Z);
                    writer.WriteLine();
                }

                for (int i = 0;
                     i < normals.Length;
                     i++)
                {
                    Vector3 normal =
                        normals[i];

                    writer.Write("vn ");
                    WriteFloat(writer, normal.X);
                    writer.Write(' ');
                    WriteFloat(writer, normal.Y);
                    writer.Write(' ');
                    WriteFloat(writer, normal.Z);
                    writer.WriteLine();
                }

                List<int> indices;
                int indexFormat;

                if (section.IsUnindexed)
                {
                    indices =
                        Enumerable.Range(
                                0,
                                vInfo.VertexCount)
                            .ToList();

                    indexFormat =
                        section.IndexFormat;
                }
                else
                {
                    if (section.IndexBufferIndex < 0
                        || section.IndexBufferIndex >= layout.IndexBuffers.Count)
                    {
                        return;
                    }

                    IndexBufferInfo iInfo =
                        layout.IndexBuffers[
                            section.IndexBufferIndex];

                    int fixupIndex =
                        layout.VertexBuffers.Count * 2
                        + section.IndexBufferIndex;

                    if (fixupIndex < 0
                        || fixupIndex >= layout.Entry.ResourceFixups.Count)
                    {
                        return;
                    }

                    int indexDataOffset =
                        layout.Entry.ResourceFixups[
                            fixupIndex];

                    int indexSize =
                        vInfo.VertexCount > ushort.MaxValue
                            ? 4
                            : 2;

                    if (iInfo.DataLength <= 0
                        || indexDataOffset < 0
                        || indexDataOffset >
                            resourceData.Length - iInfo.DataLength)
                    {
                        throw new InvalidDataException(
                            $"Reach BSP section {sectionIndex} has an invalid index buffer layout.");
                    }

                    int indexCount =
                        iInfo.DataLength /
                        indexSize;

                    indices =
                        new List<int>(
                            indexCount);

                    for (int i = 0;
                         i < indexCount;
                         i++)
                    {
                        int offset =
                            indexDataOffset
                            + i * indexSize;

                        if (indexSize == 2)
                        {
                            indices.Add(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    resourceData.AsSpan(
                                        offset,
                                        2)));
                        }
                        else
                        {
                            uint value =
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    resourceData.AsSpan(
                                        offset,
                                        4));

                            indices.Add(
                                value == uint.MaxValue
                                    ? -1
                                    : checked((int)value));
                        }
                    }

                    indexFormat =
                        iInfo.IndexFormat;
                }

                List<ReachSubmesh> ranges =
                    section.Submeshes.Count > 0
                        ? section.Submeshes
                        : new List<ReachSubmesh>
                        {
                            new ReachSubmesh(
                                0,
                                indices.Count)
                        };

                int trianglesWritten =
                    0;

                foreach (ReachSubmesh submesh
                         in ranges)
                {
                    if (submesh.IndexStart < 0
                        || submesh.IndexLength <= 0
                        || submesh.IndexStart >
                            indices.Count - submesh.IndexLength)
                    {
                        continue;
                    }

                    IReadOnlyList<int> slice =
                        indices
                            .Skip(
                                submesh.IndexStart)
                            .Take(
                                submesh.IndexLength)
                            .ToArray();

                    IEnumerable<(int A, int B, int C)> triangles =
                        indexFormat is 0 or 5
                            ? Unstrip(
                                slice,
                                vInfo.VertexCount > ushort.MaxValue
                                    ? -1
                                    : ushort.MaxValue)
                            : TriangleList(
                                slice);

                    foreach ((int a, int b, int c)
                             in triangles)
                    {
                        if (a < 0
                            || b < 0
                            || c < 0
                            || a >= vInfo.VertexCount
                            || b >= vInfo.VertexCount
                            || c >= vInfo.VertexCount
                            || a == b
                            || a == c
                            || b == c)
                        {
                            continue;
                        }

                        int ia =
                            globalVertexBase + a;

                        int ib =
                            globalVertexBase + b;

                        int ic =
                            globalVertexBase + c;

                        writer.Write("f ");
                        writer.Write(ia);
                        writer.Write("//");
                        writer.Write(ia);
                        writer.Write(' ');
                        writer.Write(ib);
                        writer.Write("//");
                        writer.Write(ib);
                        writer.Write(' ');
                        writer.Write(ic);
                        writer.Write("//");
                        writer.Write(ic);
                        writer.WriteLine();

                        trianglesWritten++;
                    }
                }

                result.VertexCount +=
                    vInfo.VertexCount;

                result.TriangleCount +=
                    trianglesWritten;

                result.MeshPlacementCount++;

                globalVertexBase +=
                    vInfo.VertexCount;
            }

            private static void DecodeVertex(
                byte[] data,
                int offset,
                byte format,
                out Vector3 position,
                out Vector3 normal)
            {
                if (format is 0x00 or 0x01 or 0x02)
                {
                    position =
                        new Vector3(
                            ReadSingleLittleEndian(
                                data,
                                offset),
                            ReadSingleLittleEndian(
                                data,
                                offset + 4),
                            ReadSingleLittleEndian(
                                data,
                                offset + 8));

                    normal =
                        new Vector3(
                            ReadSnorm16(
                                data,
                                offset + 20),
                            ReadSnorm16(
                                data,
                                offset + 22),
                            ReadSnorm16(
                                data,
                                offset + 24));

                    return;
                }

                if (format == 0x0F)
                {
                    position =
                        new Vector3(
                            ReadSingleLittleEndian(
                                data,
                                offset),
                            ReadSingleLittleEndian(
                                data,
                                offset + 4),
                            ReadSingleLittleEndian(
                                data,
                                offset + 8));

                    normal =
                        new Vector3(
                            ReadSingleLittleEndian(
                                data,
                                offset + 20),
                            ReadSingleLittleEndian(
                                data,
                                offset + 24),
                            ReadSingleLittleEndian(
                                data,
                                offset + 28));

                    return;
                }

                throw new NotSupportedException(
                    $"Unsupported MCC Reach vertex format 0x{format:X2}.");
            }

            private static int GetMinimumVertexStride(
                byte format)
            {
                return format switch
                {
                    0x00 => 36,
                    0x01 => 36,
                    0x02 => 44,
                    0x0F => 32,
                    _ => -1
                };
            }

            private static bool IsSupportedVertexFormat(
                byte format)
            {
                return GetMinimumVertexStride(format) > 0;
            }

            private ReachBlock ReadBlock(
                long offset)
            {
                int count =
                    ReadInt32At(
                        offset);

                uint pointer =
                    ReadUInt32At(
                        offset + 4);

                if (count <= 0
                    || pointer == 0)
                {
                    return new ReachBlock(
                        0,
                        -1);
                }

                if (count > 10_000_000)
                {
                    throw new InvalidDataException(
                        $"Reach tag block at 0x{offset:X} has an implausible element count ({count:N0}).");
                }

                long translated =
                    TranslateCompressedMetadataPointer(
                        pointer);

                if (translated < 0
                    || translated >= _stream.Length)
                {
                    throw new InvalidDataException(
                        $"Reach tag block at 0x{offset:X} points outside the cache.");
                }

                return new ReachBlock(
                    count,
                    translated);
            }

            private long TranslateCompressedMetadataPointer(
                uint pointer)
            {
                long expanded =
                    checked(
                        ((long)pointer << 2)
                        + _pointerMagic);

                return TranslateExpandedMetadataPointer(
                    expanded);
            }

            private long TranslateExpandedMetadataPointer(
                long pointer)
            {
                return pointer
                    - _metadataMagic;
            }

            private long TranslateSection0Pointer(
                uint pointer)
            {
                uint offset =
                    unchecked(
                        pointer
                        - _section0Magic);

                return offset;
            }

            private int ReadTagReferenceId(
                long offset)
            {
                int raw =
                    ReadInt32At(
                        offset + 12);

                return unchecked((short)(raw & ushort.MaxValue));
            }

            private void ValidateHeaderSignature()
            {
                byte[] signature =
                    ReadBytesAt(
                        0,
                        4);

                string text =
                    Encoding.ASCII.GetString(
                        signature);

                if (!text.Equals(
                        "head",
                        StringComparison.Ordinal)
                    && !text.Equals(
                        "daeh",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The selected .map does not contain a recognised Halo cache header.\n\n" +
                        $"Header bytes: {Convert.ToHexString(signature)}");
                }
            }

            private void ValidateRange(
                long offset,
                long length,
                string description)
            {
                if (offset < 0
                    || length < 0
                    || offset >
                        _stream.Length - length)
                {
                    throw new InvalidDataException(
                        $"The Reach {description} points outside the cache (0x{offset:X}, {length:N0} bytes).");
                }
            }

            private byte ReadByteAt(
                long offset)
            {
                ValidateRange(
                    offset,
                    1,
                    "byte field");

                _stream.Position =
                    offset;

                int value =
                    _stream.ReadByte();

                if (value < 0)
                    throw new EndOfStreamException();

                return (byte)value;
            }

            private short ReadInt16At(
                long offset)
            {
                Span<byte> bytes =
                    stackalloc byte[2];

                ReadAt(
                    offset,
                    bytes);

                return BinaryPrimitives.ReadInt16LittleEndian(
                    bytes);
            }

            private ushort ReadUInt16At(
                long offset)
            {
                Span<byte> bytes =
                    stackalloc byte[2];

                ReadAt(
                    offset,
                    bytes);

                return BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes);
            }

            private int ReadInt32At(
                long offset)
            {
                Span<byte> bytes =
                    stackalloc byte[4];

                ReadAt(
                    offset,
                    bytes);

                return BinaryPrimitives.ReadInt32LittleEndian(
                    bytes);
            }

            private uint ReadUInt32At(
                long offset)
            {
                Span<byte> bytes =
                    stackalloc byte[4];

                ReadAt(
                    offset,
                    bytes);

                return BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes);
            }

            private ulong ReadUInt64At(
                long offset)
            {
                Span<byte> bytes =
                    stackalloc byte[8];

                ReadAt(
                    offset,
                    bytes);

                return BinaryPrimitives.ReadUInt64LittleEndian(
                    bytes);
            }

            private float ReadFloatAt(
                long offset)
            {
                return BitConverter.Int32BitsToSingle(
                    ReadInt32At(
                        offset));
            }

            private string ReadFixedString(
                long offset,
                int length)
            {
                byte[] bytes =
                    ReadBytesAt(
                        offset,
                        length);

                int end =
                    Array.IndexOf(
                        bytes,
                        (byte)0);

                if (end < 0)
                    end = bytes.Length;

                return Encoding.UTF8.GetString(
                        bytes,
                        0,
                        end)
                    .Trim();
            }

            private byte[] ReadBytesAt(
                long offset,
                int length)
            {
                ValidateRange(
                    offset,
                    length,
                    "data field");

                byte[] result =
                    new byte[length];

                _stream.Position =
                    offset;

                ReadExactly(
                    _stream,
                    result);

                return result;
            }

            private void ReadAt(
                long offset,
                Span<byte> destination)
            {
                ValidateRange(
                    offset,
                    destination.Length,
                    "data field");

                _stream.Position =
                    offset;

                ReadExactly(
                    _stream,
                    destination);
            }
        }

        private sealed record ReachTag(
            int Id,
            string ClassCode,
            string Name,
            long MetadataOffset);

        private readonly record struct ReachBlock(
            int Count,
            long Offset);

        private sealed record ResourceEntry(
            int Index,
            int OwnerTagId,
            int FixupOffset,
            int FixupSize,
            short SegmentIndex,
            List<int> ResourceFixups);

        private readonly record struct PageInfo(
            short CacheIndex,
            int DataOffset,
            int CompressedSize,
            int DecompressedSize);

        private readonly record struct SegmentInfo(
            short PrimaryPageIndex,
            short SecondaryPageIndex,
            int PrimaryPageOffset,
            int SecondaryPageOffset);

        private sealed record ReachSection(
            short VertexBufferIndex,
            short IndexBufferIndex,
            ushort Flags,
            byte VertexFormat,
            byte IndexFormat,
            List<ReachSubmesh> Submeshes)
        {
            public bool IsUnindexed =>
                IndexBufferIndex < 0
                || (Flags & (1 << 4)) != 0;
        }

        private readonly record struct ReachSubmesh(
            int IndexStart,
            int IndexLength);

        private sealed record ReachBoundingBox(
            float XMin,
            float XMax,
            float YMin,
            float YMax,
            float ZMin,
            float ZMax)
        {
            public bool HasPositionBounds =>
                XMin != XMax
                || YMin != YMax
                || ZMin != ZMax;

            public Vector3 ExpandPosition(
                Vector3 value)
            {
                return new Vector3(
                    XMin
                    + value.X * (XMax - XMin),
                    YMin
                    + value.Y * (YMax - YMin),
                    ZMin
                    + value.Z * (ZMax - ZMin));
            }
        }

        private readonly record struct VertexBufferInfo(
            int VertexCount,
            int DataLength);

        private readonly record struct IndexBufferInfo(
            int IndexFormat,
            int DataLength);

        private sealed record GeometryResourceLayout(
            ResourceEntry Entry,
            List<VertexBufferInfo> VertexBuffers,
            List<IndexBufferInfo> IndexBuffers,
            int RequiredLength,
            int Score);

        private static bool TryAccumulateRequiredLength(
            int offset,
            int length,
            ref int requiredLength)
        {
            if (offset < 0
                || length <= 0)
            {
                return false;
            }

            long end =
                (long)offset
                + length;

            if (end <= 0
                || end > int.MaxValue)
            {
                return false;
            }

            requiredLength =
                Math.Max(
                    requiredLength,
                    (int)end);

            return true;
        }

        private static IEnumerable<(int A, int B, int C)> TriangleList(
            IReadOnlyList<int> indices)
        {
            for (int i = 0;
                 i + 2 < indices.Count;
                 i += 3)
            {
                yield return
                    (
                        indices[i],
                        indices[i + 1],
                        indices[i + 2]
                    );
            }
        }

        private static IEnumerable<(int A, int B, int C)> Unstrip(
            IReadOnlyList<int> indices,
            int restartIndex)
        {
            int position =
                0;

            int i0 =
                0;

            int i1 =
                0;

            foreach (int index
                     in indices)
            {
                if (index == restartIndex)
                {
                    position = 0;
                    i0 = 0;
                    i1 = 0;
                    continue;
                }

                if (position == 0)
                {
                    i0 = index;
                    position = 1;
                    continue;
                }

                if (position == 1)
                {
                    i1 = index;
                    position = 2;
                    continue;
                }

                int i2 =
                    index;

                int emittedPosition =
                    position + 1;

                if (i0 != i1
                    && i0 != i2
                    && i1 != i2)
                {
                    if (emittedPosition % 2 == 1)
                    {
                        yield return
                            (
                                i0,
                                i1,
                                i2
                            );
                    }
                    else
                    {
                        yield return
                            (
                                i0,
                                i2,
                                i1
                            );
                    }
                }

                i0 = i1;
                i1 = i2;
                position++;
            }
        }

        private static float ReadSingleLittleEndian(
            byte[] data,
            int offset)
        {
            int bits =
                BinaryPrimitives.ReadInt32LittleEndian(
                    data.AsSpan(
                        offset,
                        4));

            return BitConverter.Int32BitsToSingle(
                bits);
        }

        private static float ReadSnorm16(
            byte[] data,
            int offset)
        {
            short value =
                BinaryPrimitives.ReadInt16LittleEndian(
                    data.AsSpan(
                        offset,
                        2));

            if (value == short.MinValue)
                return -1.0f;

            return Math.Clamp(
                value / 32767.0f,
                -1.0f,
                1.0f);
        }

        private static string FourCcFromClassId(
            uint classId)
        {
            Span<byte> bytes =
                stackalloc byte[4];

            bytes[0] =
                (byte)(classId >> 24);
            bytes[1] =
                (byte)(classId >> 16);
            bytes[2] =
                (byte)(classId >> 8);
            bytes[3] =
                (byte)classId;

            return Encoding.ASCII.GetString(
                bytes);
        }

        private static string SanitizeObjName(
            string value)
        {
            StringBuilder builder =
                new StringBuilder(
                    value.Length);

            foreach (char c
                     in value)
            {
                builder.Append(
                    char.IsLetterOrDigit(c)
                    || c is '_' or '-'
                        ? c
                        : '_');
            }

            return builder.Length == 0
                ? "reach_bsp"
                : builder.ToString();
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

        private static void SkipExactly(
            Stream stream,
            int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            byte[] buffer =
                new byte[64 * 1024];

            int remaining =
                count;

            while (remaining > 0)
            {
                int read =
                    stream.Read(
                        buffer,
                        0,
                        Math.Min(
                            buffer.Length,
                            remaining));

                if (read <= 0)
                    throw new EndOfStreamException();

                remaining -=
                    read;
            }
        }

        private static void ReadExactly(
            Stream stream,
            byte[] destination)
        {
            ReadExactly(
                stream,
                destination.AsSpan());
        }

        private static void ReadExactly(
            Stream stream,
            Span<byte> destination)
        {
            int total =
                0;

            while (total < destination.Length)
            {
                int read =
                    stream.Read(
                        destination[total..]);

                if (read <= 0)
                    throw new EndOfStreamException();

                total +=
                    read;
            }
        }
    }
}
