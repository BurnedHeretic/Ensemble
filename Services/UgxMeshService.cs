using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Ensemble.Services
{
    /// <summary>
    /// Native Halo Wars UGX mesh reader for the Ensemble 3D viewport.
    ///
    /// The format information used here comes from Ensemble Studios' original
    /// ugxGeom / gr2ugx source plus validation against Halo Wars DE stock UGX
    /// files.  GR2 is the authoring format; gr2ugx compiled it into the UGX
    /// geometry consumed by the game.  This service renders that compiled
    /// geometry directly from the currently-open ERA.
    /// </summary>
    internal static class UgxMeshService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const uint UgxFileId =
            0xAAC93746;

        private const ulong CachedDataChunkId =
            0x00000700;

        private const ulong IndexBufferChunkId =
            0x00000701;

        private const ulong VertexBufferChunkId =
            0x00000702;

        private const ulong MaterialChunkId =
            0x00000704;

        private const uint GeometrySignature =
            0xC2340004;

        private const int Packed64RootSectionsOffset =
            64;

        private const int Packed64SectionSize =
            152;

        private static readonly Dictionary<string, UgxMeshAsset?>
            AssetCache =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, BitmapSource?>
            TextureCache =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, List<UgxIndexEntry>>
            ArchiveIndexCache =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, UgxMeshAsset?>
            ResolutionCache =
                new(
                    StringComparer.OrdinalIgnoreCase);

        public sealed class UgxMeshAsset
        {
            public string SourceArchivePath
            {
                get;
                init;
            } =
                string.Empty;

            public string SourceFileName
            {
                get;
                init;
            } =
                string.Empty;

            public List<UgxMeshPart> Parts
            {
                get;
                init;
            } =
                new();
        }

        public sealed class UgxMeshPart
        {
            public int MaterialIndex
            {
                get;
                init;
            }

            public MeshGeometry3D Geometry
            {
                get;
                init;
            } =
                new MeshGeometry3D();

            public ImageSource? DiffuseTexture
            {
                get;
                init;
            }
        }

        public static UgxMeshAsset? TryLoadForObject(
            EraArchiveInfo archive,
            string type,
            string editorName)
        {
            ArgumentNullException.ThrowIfNull(
                archive);

            IReadOnlyList<EraArchiveInfo> archives =
                HaloWarsAssetArchiveService
                    .GetSearchArchives(
                        archive);

            string resolutionKey =
                string.Join(
                    "|",
                    archives.Select(
                        item =>
                            item.FilePath))
                +
                "||" +
                type +
                "||" +
                editorName;

            if (ResolutionCache.TryGetValue(
                    resolutionKey,
                    out UgxMeshAsset? resolved))
            {
                return resolved;
            }

            List<ResolvedUgxCandidate> matches =
                FindBestUgxCandidates(
                    archives,
                    type,
                    editorName);

            foreach (ResolvedUgxCandidate match
                     in matches)
            {
                string cacheKey =
                    match.Archive.FilePath +
                    "|" +
                    match.Chunk.Index +
                    "|" +
                    match.Chunk.Adler32.ToString(
                        "X8");

                if (AssetCache.TryGetValue(
                        cacheKey,
                        out UgxMeshAsset? cached))
                {
                    if (cached !=
                        null)
                    {
                        ResolutionCache[
                            resolutionKey] =
                                cached;

                        return cached;
                    }

                    continue;
                }

                try
                {
                    byte[] ugxData =
                        EraExtractionService.ExtractChunk(
                            match.Archive,
                            match.Chunk);

                    UgxMeshAsset asset =
                        ParseUgx(
                            match.Archive,
                            match.Chunk.FileName,
                            ugxData);

                    if (asset.Parts.Count ==
                        0)
                    {
                        AssetCache[
                            cacheKey] =
                                null;

                        continue;
                    }

                    AssetCache[
                        cacheKey] =
                            asset;

                    ResolutionCache[
                        resolutionKey] =
                            asset;

                    return asset;
                }
                catch
                {
                    // A fuzzy match or optional/unsupported UGX must not
                    // prevent the resolver from trying the next candidate.
                    AssetCache[
                        cacheKey] =
                            null;
                }
            }

            ResolutionCache[
                resolutionKey] =
                    null;

            return null;
        }

        public static void ClearCaches()
        {
            AssetCache.Clear();
            TextureCache.Clear();
            ArchiveIndexCache.Clear();
            ResolutionCache.Clear();
        }

        private static List<ResolvedUgxCandidate> FindBestUgxCandidates(
            IReadOnlyList<EraArchiveInfo> archives,
            string type,
            string editorName)
        {
            List<AssetSearchCandidate> candidates =
                BuildSearchCandidates(
                    type,
                    editorName);

            List<ResolvedUgxCandidate> matches =
                new();

            for (int archivePriority = 0;
                 archivePriority <
                 archives.Count;
                 archivePriority++)
            {
                EraArchiveInfo archive =
                    archives[
                        archivePriority];

                foreach (UgxIndexEntry entry
                         in GetArchiveIndex(
                             archive))
                {
                    int score =
                        0;

                    foreach (AssetSearchCandidate candidate
                             in candidates)
                    {
                        score =
                            Math.Max(
                                score,
                                ScoreCandidate(
                                    entry.Key,
                                    entry.Base,
                                    entry.Tokens,
                                    candidate));
                    }

                    if (score >=
                        360)
                    {
                        matches.Add(
                            new ResolvedUgxCandidate
                            {
                                Archive =
                                    archive,

                                Chunk =
                                    entry.Chunk,

                                Score =
                                    score,

                                ArchivePriority =
                                    archivePriority
                            });
                    }
                }
            }

            return matches
                .OrderByDescending(
                    item =>
                        item.Score)
                .ThenBy(
                    item =>
                        item.ArchivePriority)
                .Take(
                    32)
                .ToList();
        }

        private static IReadOnlyList<UgxIndexEntry> GetArchiveIndex(
            EraArchiveInfo archive)
        {
            string key =
                Path.GetFullPath(
                    archive.FilePath);

            if (ArchiveIndexCache.TryGetValue(
                    key,
                    out List<UgxIndexEntry>? cached))
            {
                return cached;
            }

            List<UgxIndexEntry> result =
                new();

            foreach (EraChunkInfo chunk
                     in archive.Chunks)
            {
                if (chunk.Index <=
                        0 ||
                    string.IsNullOrWhiteSpace(
                        chunk.FileName) ||
                    !chunk.FileName.EndsWith(
                        ".ugx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetKey =
                    NormaliseAssetKey(
                        chunk.FileName);

                result.Add(
                    new UgxIndexEntry
                    {
                        Chunk =
                            chunk,

                        Key =
                            assetKey,

                        Base =
                            GetBaseName(
                                assetKey),

                        Tokens =
                            TokeniseAssetName(
                                assetKey)
                    });
            }

            ArchiveIndexCache[
                key] =
                    result;

            return result;
        }

        private static List<AssetSearchCandidate> BuildSearchCandidates(
            string type,
            string editorName)
        {
            List<AssetSearchCandidate> result =
                new();

            AddSearchCandidate(
                result,
                type,
                1000);

            AddSearchCandidate(
                result,
                editorName,
                650);

            string combined =
                (
                    type +
                    " " +
                    editorName
                )
                .ToLowerInvariant();

            // =====================================================
            // SCENARIO / GAMEPLAY OBJECT VISUAL ALIASES
            // =====================================================
            //
            // Scenario XMB object names often describe a gameplay hook,
            // rather than naming the UGX asset directly.  These mappings
            // mirror stock Halo Wars dependency names from the original
            // Ensemble Studios build pipeline.

            if (combined.Contains(
                    "game_base_socket",
                    StringComparison.Ordinal)
                ||
                (
                    combined.Contains(
                        "base",
                        StringComparison.Ordinal)
                    &&
                    combined.Contains(
                        "socket",
                        StringComparison.Ordinal)
                ))
            {
                AddSearchCandidate(
                    result,
                    "game\\base\\socket_01\\socket_02",
                    980);

                AddSearchCandidate(
                    result,
                    "socket_02",
                    960);

                AddSearchCandidate(
                    result,
                    "baseobstruction_01",
                    580);
            }

            if (combined.Contains(
                    "reactor",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "hook\\bonus\\reactor_01\\reactor_01",
                    980);

                AddSearchCandidate(
                    result,
                    "reactor_01",
                    950);
            }

            if (combined.Contains(
                    "sniperplatform",
                    StringComparison.Ordinal)
                ||
                combined.Contains(
                    "sniper_platform",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "hook\\building\\sniperplatform_01\\sniperplatform_01",
                    980);

                AddSearchCandidate(
                    result,
                    "sniperplatform_01",
                    950);
            }

            if (combined.Contains(
                    "teleporter",
                    StringComparison.Ordinal))
            {
                bool preferTwoWay =
                    combined.Contains(
                        "2way",
                        StringComparison.Ordinal)
                    ||
                    combined.Contains(
                        "twoway",
                        StringComparison.Ordinal)
                    ||
                    combined.Contains(
                        "two_way",
                        StringComparison.Ordinal);

                bool preferOneWay =
                    combined.Contains(
                        "1way",
                        StringComparison.Ordinal)
                    ||
                    combined.Contains(
                        "oneway",
                        StringComparison.Ordinal)
                    ||
                    combined.Contains(
                        "one_way",
                        StringComparison.Ordinal)
                    ||
                    combined.Contains(
                        "receiver",
                        StringComparison.Ordinal);

                if (preferTwoWay)
                {
                    AddSearchCandidate(
                        result,
                        "hook\\building\\teleporter2way_01\\teleporter2way_01",
                        990);
                }

                if (preferOneWay)
                {
                    AddSearchCandidate(
                        result,
                        "hook\\building\\teleporter1way_01\\teleporter1way_01",
                        990);
                }

                AddSearchCandidate(
                    result,
                    "teleporter1way_01",
                    preferOneWay
                        ? 970
                        : 760);

                AddSearchCandidate(
                    result,
                    "teleporter2way_01",
                    preferTwoWay
                        ? 970
                        : 740);
            }

            if (combined.Contains(
                    "supply",
                    StringComparison.Ordinal))
            {
                // Neutral skirmish-map supply hook.
                AddSearchCandidate(
                    result,
                    "hook\\spawner\\supply3hole_01\\supply3hole_01",
                    980);

                AddSearchCandidate(
                    result,
                    "supply3hole_01",
                    950);

                // Also support faction building names on maps that place
                // those directly.
                AddSearchCandidate(
                    result,
                    "supplypad_01",
                    700);

                AddSearchCandidate(
                    result,
                    "supplypad_02",
                    680);

                AddSearchCandidate(
                    result,
                    "supplydepot_01",
                    680);
            }

            if (combined.Contains(
                    "rebelmarker",
                    StringComparison.Ordinal)
                ||
                combined.Contains(
                    "rebel_marker",
                    StringComparison.Ordinal))
            {
                if (combined.Contains(
                        "sniper",
                        StringComparison.Ordinal))
                {
                    AddSearchCandidate(
                        result,
                        "system\\rebelmarker\\sniper_01\\sniper_01",
                        990);

                    AddSearchCandidate(
                        result,
                        "sniper_01",
                        960);
                }
                else
                {
                    AddSearchCandidate(
                        result,
                        "system\\rebelmarker\\infantry_01\\infantry_01",
                        920);

                    AddSearchCandidate(
                        result,
                        "infantry_01",
                        880);
                }
            }

            if (combined.Contains(
                    "crate",
                    StringComparison.Ordinal))
            {
                foreach (string crateName
                         in new[]
                         {
                             "crate_01",
                             "crate_02",
                             "crate_03",
                             "crate_04",
                             "crate_05",
                             "crate12_01",
                             "crate3a_01",
                             "crate3b_01",
                             "crate3c_01",
                             "crate5_01"
                         })
                {
                    if (combined.Contains(
                            crateName.Replace(
                                "_",
                                string.Empty),
                            StringComparison.Ordinal)
                        ||
                        combined.Contains(
                            crateName,
                            StringComparison.Ordinal))
                    {
                        AddSearchCandidate(
                            result,
                            crateName,
                            990);
                    }
                    else
                    {
                        AddSearchCandidate(
                            result,
                            crateName,
                            610);
                    }
                }
            }

            if (combined.Contains(
                    "unitstart",
                    StringComparison.Ordinal)
                ||
                combined.Contains(
                    "unit_start",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "system\\unitstart_01\\unitstart_01",
                    960);

                AddSearchCandidate(
                    result,
                    "unitstart_01",
                    940);
            }

            if (combined.Contains(
                    "baseobstruction",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "system\\marker\\baseobstruction_01\\baseobstruction_01",
                    970);
            }

            if (combined.Contains(
                    "globalrally",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "system\\marker\\globalrally_01\\globalrally_01",
                    970);
            }
            else if (
                combined.Contains(
                    "rally",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "system\\marker\\rally_01\\rally_02",
                    900);
            }

            if (combined.Contains(
                    "highlight",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "highlight_01",
                    840);

                AddSearchCandidate(
                    result,
                    "highlight_02",
                    820);

                AddSearchCandidate(
                    result,
                    "highlight_03",
                    800);
            }

            if (combined.Contains(
                    "parkinglotcov",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "parkinglotcov_01",
                    980);
            }

            if (combined.Contains(
                    "parkinglotunsc",
                    StringComparison.Ordinal))
            {
                AddSearchCandidate(
                    result,
                    "parkinglotunsc_01",
                    980);
            }

            // =====================================================
            // FLATTENED / PREFIXED PROTOTYPE NAMES
            // =====================================================
            //
            // Many types look like:
            //     env_arcadia_pinetree_08
            //     sys_rebelMarker_sniper_01
            //     game_base_socket_01
            //
            // Add progressively stripped forms for archive matching.

            foreach (string source
                     in new[]
                     {
                         type,
                         editorName
                     })
            {
                string normalised =
                    NormaliseLooseName(
                        source);

                if (string.IsNullOrWhiteSpace(
                        normalised))
                {
                    continue;
                }

                AddSearchCandidate(
                    result,
                    normalised,
                    560);

                foreach (string prefix
                         in new[]
                         {
                             "env_",
                             "sys_",
                             "game_",
                             "hook_",
                             "obj_",
                             "for_",
                             "unsc_",
                             "cov_"
                         })
                {
                    if (normalised.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        normalised.Length >
                            prefix.Length)
                    {
                        AddSearchCandidate(
                            result,
                            normalised[
                                prefix.Length..],
                            540);
                    }
                }
            }

            return result;
        }

        private static void AddSearchCandidate(
            List<AssetSearchCandidate> result,
            string value,
            int weight)
        {
            string normalised =
                NormaliseAssetKey(
                    value);

            if (string.IsNullOrWhiteSpace(
                    normalised))
            {
                return;
            }

            string baseName =
                GetBaseName(
                    normalised);

            AssetSearchCandidate? existing =
                result.FirstOrDefault(
                    candidate =>
                        candidate.Key.Equals(
                            normalised,
                            StringComparison.OrdinalIgnoreCase));

            if (existing !=
                null)
            {
                existing.Weight =
                    Math.Max(
                        existing.Weight,
                        weight);

                return;
            }

            result.Add(
                new AssetSearchCandidate
                {
                    Key =
                        normalised,

                    Base =
                        baseName,

                    Tokens =
                        TokeniseAssetName(
                            normalised),

                    Weight =
                        weight
                });
        }

        private static int ScoreCandidate(
            string assetKey,
            string assetBase,
            HashSet<string> assetTokens,
            AssetSearchCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(
                    candidate.Key))
            {
                return 0;
            }

            int weight =
                candidate.Weight;

            if (assetKey.Equals(
                    candidate.Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return weight;
            }

            if (assetKey.EndsWith(
                    "\\" +
                    candidate.Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return weight -
                    15;
            }

            if (candidate.Key.EndsWith(
                    "\\" +
                    assetKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return weight -
                    30;
            }

            if (!string.IsNullOrWhiteSpace(
                    candidate.Base)
                &&
                assetBase.Equals(
                    candidate.Base,
                    StringComparison.OrdinalIgnoreCase))
            {
                return weight -
                    45;
            }

            string flattenedAsset =
                assetKey.Replace(
                    "\\",
                    "_");

            string flattenedCandidate =
                candidate.Key.Replace(
                    "\\",
                    "_");

            if (flattenedAsset.EndsWith(
                    flattenedCandidate,
                    StringComparison.OrdinalIgnoreCase)
                ||
                flattenedCandidate.EndsWith(
                    flattenedAsset,
                    StringComparison.OrdinalIgnoreCase))
            {
                return weight -
                    70;
            }

            if (!string.IsNullOrWhiteSpace(
                    assetBase)
                &&
                assetBase.Length >=
                    4
                &&
                (
                    candidate.Key.EndsWith(
                        assetBase,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    candidate.Key.Contains(
                        assetBase,
                        StringComparison.OrdinalIgnoreCase)
                ))
            {
                return weight -
                    90;
            }

            int tokenMatches =
                candidate.Tokens.Count(
                    token =>
                        assetTokens.Contains(
                            token));

            if (tokenMatches >
                0)
            {
                int totalTokens =
                    Math.Max(
                        1,
                        Math.Max(
                            candidate.Tokens.Count,
                            assetTokens.Count));

                double ratio =
                    tokenMatches /
                    (double)totalTokens;

                int tokenScore =
                    (int)(
                        weight *
                        ratio *
                        0.70);

                // A matching numbered basename such as pinetree + 08 is
                // especially strong.
                if (candidate.Tokens.Any(
                        token =>
                            token.Length <=
                                3
                            &&
                            token.All(
                                char.IsDigit)
                            &&
                            assetTokens.Contains(
                                token)))
                {
                    tokenScore +=
                        90;
                }

                return tokenScore;
            }

            return 0;
        }

        private static HashSet<string> TokeniseAssetName(
            string value)
        {
            string loose =
                NormaliseLooseName(
                    value);

            return loose
                .Split(
                    new[]
                    {
                        '_',
                        '\\',
                        '/',
                        '-',
                        '.',
                        ' '
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(
                    token =>
                        token.Length >=
                            2)
                .Select(
                    token =>
                        token.ToLowerInvariant())
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string NormaliseLooseName(
            string value)
        {
            return (
                value ??
                string.Empty)
                .Trim()
                .Trim('"')
                .Replace(
                    '/',
                    '\\')
                .TrimStart(
                    '\\')
                .ToLowerInvariant();
        }

        private static string NormaliseAssetKey(
            string value)
        {
            string result =
                NormaliseLooseName(
                    value);

            if (result.StartsWith(
                    "art\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                result =
                    result[
                        4..];
            }

            if (result.EndsWith(
                    ".ugx",
                    StringComparison.OrdinalIgnoreCase)
                ||
                result.EndsWith(
                    ".gr2",
                    StringComparison.OrdinalIgnoreCase))
            {
                result =
                    result[..^4];
            }

            return result;
        }

        private static string GetBaseName(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return string.Empty;
            }

            int slash =
                value.LastIndexOf(
                    '\\');

            return slash >=
                0
                ? value[
                    (
                        slash +
                        1
                    )..]
                : value;
        }

        private sealed class ResolvedUgxCandidate
        {
            public EraArchiveInfo Archive
            {
                get;
                init;
            } =
                null!;

            public EraChunkInfo Chunk
            {
                get;
                init;
            } =
                null!;

            public int Score
            {
                get;
                init;
            }

            public int ArchivePriority
            {
                get;
                init;
            }
        }

        private sealed class UgxIndexEntry
        {
            public EraChunkInfo Chunk
            {
                get;
                init;
            } =
                null!;

            public string Key
            {
                get;
                init;
            } =
                string.Empty;

            public string Base
            {
                get;
                init;
            } =
                string.Empty;

            public HashSet<string> Tokens
            {
                get;
                init;
            } =
                new(
                    StringComparer.OrdinalIgnoreCase);
        }

        private sealed class AssetSearchCandidate
        {
            public string Key
            {
                get;
                init;
            } =
                string.Empty;

            public string Base
            {
                get;
                init;
            } =
                string.Empty;

            public HashSet<string> Tokens
            {
                get;
                init;
            } =
                new(
                    StringComparer.OrdinalIgnoreCase);

            public int Weight
            {
                get;
                set;
            }
        }

        private static UgxMeshAsset ParseUgx(
            EraArchiveInfo archive,
            string sourceFileName,
            byte[] data)
        {
            Dictionary<ulong, byte[]> chunks =
                ReadEcfChunks(
                    data);

            if (!chunks.TryGetValue(
                    CachedDataChunkId,
                    out byte[]? cached) ||
                !chunks.TryGetValue(
                    VertexBufferChunkId,
                    out byte[]? vertexBuffer) ||
                !chunks.TryGetValue(
                    IndexBufferChunkId,
                    out byte[]? indexBuffer))
            {
                throw new InvalidDataException(
                    "UGX is missing cached/VB/IB geometry chunks.");
            }

            bool bigEndian;

            if (ReadUInt32(
                    cached,
                    0,
                    false) ==
                GeometrySignature)
            {
                bigEndian =
                    false;
            }
            else if (
                ReadUInt32(
                    cached,
                    0,
                    true) ==
                GeometrySignature)
            {
                bigEndian =
                    true;
            }
            else
            {
                throw new InvalidDataException(
                    "UGX cached geometry signature is invalid.");
            }

            int sectionCount =
                checked(
                    (int)ReadUInt32(
                        cached,
                        Packed64RootSectionsOffset,
                        bigEndian));

            ulong sectionPointer =
                ReadUInt64(
                    cached,
                    Packed64RootSectionsOffset +
                    8,
                    bigEndian);

            if (sectionCount <=
                    0 ||
                sectionCount >
                    4096 ||
                sectionPointer >
                    (ulong)int.MaxValue ||
                sectionPointer +
                    (ulong)sectionCount *
                    Packed64SectionSize >
                    (ulong)cached.Length)
            {
                throw new InvalidDataException(
                    "UGX cached geometry does not use the supported DE 64-bit packed layout.");
            }

            List<string> diffusePaths =
                chunks.TryGetValue(
                    MaterialChunkId,
                    out byte[]? materialChunk)
                    ? ExtractDiffuseTexturePaths(
                        materialChunk)
                    : new List<string>();

            Dictionary<int, ImageSource?> diffuseTextures =
                new();

            UgxMeshAsset asset =
                new UgxMeshAsset
                {
                    SourceArchivePath =
                        archive.FilePath,

                    SourceFileName =
                        sourceFileName
                };

            int sectionsOffset =
                checked(
                    (int)sectionPointer);

            for (int sectionIndex = 0;
                 sectionIndex <
                 sectionCount;
                 sectionIndex++)
            {
                int sectionOffset =
                    checked(
                        sectionsOffset +
                        sectionIndex *
                        Packed64SectionSize);

                ParsedSection section =
                    ReadSection(
                        cached,
                        sectionOffset,
                        bigEndian);

                if (section.NumVerts <=
                        0 ||
                    section.NumTris <=
                        0 ||
                    section.VertexStride <=
                        0)
                {
                    continue;
                }

                try
                {
                    MeshGeometry3D geometry =
                        BuildSectionGeometry(
                            cached,
                            vertexBuffer,
                            indexBuffer,
                            section,
                            bigEndian);

                    if (geometry.Positions.Count ==
                            0 ||
                        geometry.TriangleIndices.Count ==
                            0)
                    {
                        continue;
                    }

                    if (!diffuseTextures.TryGetValue(
                            section.MaterialIndex,
                            out ImageSource? diffuse))
                    {
                        try
                        {
                            diffuse =
                                TryLoadDiffuseTexture(
                                    archive,
                                    diffusePaths,
                                    section.MaterialIndex);
                        }
                        catch
                        {
                            // Texture decode failure must never prevent the
                            // geometry itself from rendering.
                            diffuse =
                                null;
                        }

                        diffuseTextures[
                            section.MaterialIndex] =
                                diffuse;
                    }

                    asset.Parts.Add(
                        new UgxMeshPart
                        {
                            MaterialIndex =
                                section.MaterialIndex,

                            Geometry =
                                geometry,

                            DiffuseTexture =
                                diffuse
                        });
                }
                catch
                {
                    // Complex UGX files can contain optional sections using
                    // vertex declarations we have not decoded yet. Render
                    // every supported section instead of rejecting the whole
                    // model because one section is unfamiliar.
                    continue;
                }
            }

            if (asset.Parts.Count ==
                0)
            {
                throw new InvalidDataException(
                    "UGX contained no renderable mesh sections.");
            }

            return asset;
        }

        private static Dictionary<ulong, byte[]> ReadEcfChunks(
            byte[] data)
        {
            if (data.Length <
                32)
            {
                throw new InvalidDataException(
                    "UGX is too small.");
            }

            if (ReadUInt32(
                    data,
                    0,
                    true) !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    "UGX ECF magic is invalid.");
            }

            int headerSize =
                checked(
                    (int)ReadUInt32(
                        data,
                        4,
                        true));

            int fileSize =
                checked(
                    (int)ReadUInt32(
                        data,
                        12,
                        true));

            int chunkCount =
                ReadUInt16(
                    data,
                    16,
                    true);

            uint fileId =
                ReadUInt32(
                    data,
                    20,
                    true);

            int extra =
                ReadUInt16(
                    data,
                    24,
                    true);

            if (fileId !=
                UgxFileId)
            {
                throw new InvalidDataException(
                    "ECF is not a Halo Wars UGX model.");
            }

            if (fileSize !=
                    0 &&
                fileSize !=
                    checked((uint)data.Length))
            {
                throw new InvalidDataException(
                    "UGX file-size header is invalid.");
            }

            int chunkHeaderSize =
                checked(
                    24 +
                    extra);

            Dictionary<ulong, byte[]> result =
                new();

            for (int i = 0;
                 i <
                 chunkCount;
                 i++)
            {
                int p =
                    checked(
                        headerSize +
                        i *
                        chunkHeaderSize);

                EnsureRange(
                    data,
                    p,
                    24);

                ulong id =
                    ReadUInt64(
                        data,
                        p,
                        true);

                int offset =
                    checked(
                        (int)ReadUInt32(
                            data,
                            p +
                            8,
                            true));

                int size =
                    checked(
                        (int)ReadUInt32(
                            data,
                            p +
                            12,
                            true));

                byte flags =
                    data[
                        p +
                        20];

                int compression =
                    flags &
                    0x07;

                EnsureRange(
                    data,
                    offset,
                    size);

                byte[] payload =
                    data.AsSpan(
                            offset,
                            size)
                        .ToArray();

                if (compression ==
                    0)
                {
                    result[
                        id] =
                            payload;

                    continue;
                }

                if (compression ==
                        2
                    &&
                    extra >=
                        32)
                {
                    uint decompressedSize =
                        ReadUInt32(
                            data,
                            p +
                            32,
                            true);

                    result[
                        id] =
                            EraCompressionService
                                .DecompressDeflateStream(
                                    payload,
                                    decompressedSize);

                    continue;
                }

                // Unknown internal UGX compression. Leave this individual
                // chunk unavailable rather than rejecting the entire file.
            }

            return result;
        }

        private static ParsedSection ReadSection(
            byte[] cached,
            int offset,
            bool bigEndian)
        {
            EnsureRange(
                cached,
                offset,
                Packed64SectionSize);

            ParsedSection result =
                new ParsedSection
                {
                    MaterialIndex =
                        ReadInt32(
                            cached,
                            offset +
                            0,
                            bigEndian),

                    RigidBoneIndex =
                        ReadInt32(
                            cached,
                            offset +
                            12,
                            bigEndian),

                    IndexOffset =
                        ReadInt32(
                            cached,
                            offset +
                            16,
                            bigEndian),

                    NumTris =
                        ReadInt32(
                            cached,
                            offset +
                            20,
                            bigEndian),

                    VertexOffset =
                        ReadInt32(
                            cached,
                            offset +
                            24,
                            bigEndian),

                    VertexBytes =
                        ReadInt32(
                            cached,
                            offset +
                            28,
                            bigEndian),

                    VertexStride =
                        ReadInt32(
                            cached,
                            offset +
                            32,
                            bigEndian),

                    NumVerts =
                        ReadInt32(
                            cached,
                            offset +
                            36,
                            bigEndian),

                    PositionType =
                        ReadInt32(
                            cached,
                            offset +
                            72,
                            bigEndian),

                    BasisType =
                        ReadInt32(
                            cached,
                            offset +
                            76,
                            bigEndian),

                    BasisScaleType =
                        ReadInt32(
                            cached,
                            offset +
                            80,
                            bigEndian),

                    TangentType =
                        ReadInt32(
                            cached,
                            offset +
                            84,
                            bigEndian),

                    NormalType =
                        ReadInt32(
                            cached,
                            offset +
                            88,
                            bigEndian),

                    IndicesType =
                        ReadInt32(
                            cached,
                            offset +
                            124,
                            bigEndian),

                    WeightsType =
                        ReadInt32(
                            cached,
                            offset +
                            128,
                            bigEndian),

                    DiffuseType =
                        ReadInt32(
                            cached,
                            offset +
                            132,
                            bigEndian),

                    IndexType =
                        ReadInt32(
                            cached,
                            offset +
                            136,
                            bigEndian)
                };

            for (int i = 0;
                 i <
                 8;
                 i++)
            {
                result.UvTypes[
                    i] =
                        ReadInt32(
                            cached,
                            offset +
                            92 +
                            i *
                            4,
                            bigEndian);
            }

            ulong packOrderPointer =
                ReadUInt64(
                    cached,
                    offset +
                    56,
                    bigEndian);

            result.PackOrder =
                ReadPackedAsciiString(
                    cached,
                    packOrderPointer);

            if (string.IsNullOrWhiteSpace(
                    result.PackOrder))
            {
                result.PackOrder =
                    "PNA0T0";
            }

            return result;
        }

        private static MeshGeometry3D BuildSectionGeometry(
            byte[] cached,
            byte[] vertexBuffer,
            byte[] indexBuffer,
            ParsedSection section,
            bool bigEndian)
        {
            if (section.VertexOffset <
                    0 ||
                section.VertexBytes <
                    0 ||
                section.VertexStride <=
                    0 ||
                section.NumVerts <
                    0 ||
                section.VertexOffset +
                    section.VertexBytes >
                    vertexBuffer.Length ||
                section.VertexStride *
                    section.NumVerts >
                    section.VertexBytes)
            {
                throw new InvalidDataException(
                    "UGX vertex section is out of range.");
            }

            int indexByteOffset =
                checked(
                    section.IndexOffset *
                    2);

            int indexCount =
                checked(
                    section.NumTris *
                    3);

            if (indexByteOffset <
                    0 ||
                indexCount <
                    0 ||
                indexByteOffset +
                    indexCount *
                    2 >
                    indexBuffer.Length)
            {
                throw new InvalidDataException(
                    "UGX index section is out of range.");
            }

            MeshGeometry3D geometry =
                new MeshGeometry3D();

            bool hasNormals =
                false;

            bool hasUvs =
                false;

            for (int vertex = 0;
                 vertex <
                 section.NumVerts;
                 vertex++)
            {
                int vertexOffset =
                    checked(
                        section.VertexOffset +
                        vertex *
                        section.VertexStride);

                DecodedVertex decoded =
                    DecodeVertex(
                        vertexBuffer,
                        vertexOffset,
                        section,
                        bigEndian);

                geometry.Positions.Add(
                    new Point3D(
                        decoded.Position.X,
                        decoded.Position.Y,
                        decoded.Position.Z));

                if (decoded.HasNormal)
                {
                    Vector3 normal =
                        decoded.Normal;

                    if (normal.LengthSquared() >
                        0.000001f)
                    {
                        normal =
                            Vector3.Normalize(
                                normal);
                    }

                    geometry.Normals.Add(
                        new Vector3D(
                            normal.X,
                            normal.Y,
                            normal.Z));

                    hasNormals =
                        true;
                }
                else
                {
                    geometry.Normals.Add(
                        new Vector3D(
                            0,
                            1,
                            0));
                }

                if (decoded.HasTexCoord)
                {
                    geometry.TextureCoordinates.Add(
                        new Point(
                            decoded.TexCoord.X,
                            decoded.TexCoord.Y));

                    hasUvs =
                        true;
                }
                else
                {
                    geometry.TextureCoordinates.Add(
                        new Point(
                            0,
                            0));
                }
            }

            for (int i = 0;
                 i <
                 indexCount;
                 i++)
            {
                int index =
                    ReadUInt16(
                        indexBuffer,
                        indexByteOffset +
                        i *
                        2,
                        bigEndian);

                if (index <
                        0 ||
                    index >=
                        section.NumVerts)
                {
                    throw new InvalidDataException(
                        "UGX triangle index is outside its section vertex range.");
                }

                geometry.TriangleIndices.Add(
                    index);
            }

            if (!hasNormals)
            {
                RecalculateNormals(
                    geometry);
            }

            if (!hasUvs)
            {
                geometry.TextureCoordinates.Clear();
            }

            if (geometry.CanFreeze)
            {
                geometry.Freeze();
            }

            return geometry;
        }

        private static DecodedVertex DecodeVertex(
            byte[] buffer,
            int vertexOffset,
            ParsedSection section,
            bool bigEndian)
        {
            DecodedVertex result =
                new DecodedVertex();

            int cursor =
                vertexOffset;

            string order =
                section.PackOrder;

            for (int i = 0;
                 i <
                 order.Length;
                 i++)
            {
                char token =
                    char.ToUpperInvariant(
                        order[
                            i]);

                switch (token)
                {
                    case 'P':
                        {
                            Vector4 value =
                                ReadElement(
                                    buffer,
                                    cursor,
                                    section.PositionType,
                                    bigEndian);

                            result.Position =
                                new Vector3(
                                    value.X,
                                    value.Y,
                                    value.Z);

                            cursor +=
                                ElementSize(
                                    section.PositionType);

                            break;
                        }

                    case 'B':
                        {
                            i =
                                SkipIndexSuffix(
                                    order,
                                    i);

                            int size =
                                ElementSize(
                                    section.BasisType);

                            cursor +=
                                size *
                                2;

                            break;
                        }

                    case 'X':
                        {
                            i =
                                SkipIndexSuffix(
                                    order,
                                    i);

                            cursor +=
                                ElementSize(
                                    section.BasisScaleType);

                            break;
                        }

                    case 'A':
                        {
                            i =
                                SkipIndexSuffix(
                                    order,
                                    i);

                            cursor +=
                                ElementSize(
                                    section.TangentType);

                            break;
                        }

                    case 'N':
                        {
                            Vector4 value =
                                ReadElement(
                                    buffer,
                                    cursor,
                                    section.NormalType,
                                    bigEndian);

                            result.Normal =
                                new Vector3(
                                    value.X,
                                    value.Y,
                                    value.Z);

                            result.HasNormal =
                                true;

                            cursor +=
                                ElementSize(
                                    section.NormalType);

                            break;
                        }

                    case 'T':
                        {
                            int uvIndex =
                                0;

                            if (i +
                                    1 <
                                order.Length &&
                                char.IsDigit(
                                    order[
                                        i +
                                        1]))
                            {
                                uvIndex =
                                    order[
                                        i +
                                        1] -
                                    '0';

                                i++;
                            }

                            uvIndex =
                                Math.Clamp(
                                    uvIndex,
                                    0,
                                    7);

                            int uvType =
                                section.UvTypes[
                                    uvIndex];

                            Vector4 value =
                                ReadElement(
                                    buffer,
                                    cursor,
                                    uvType,
                                    bigEndian);

                            if (!result.HasTexCoord)
                            {
                                result.TexCoord =
                                    new Vector2(
                                        value.X,
                                        value.Y);

                                result.HasTexCoord =
                                    true;
                            }

                            cursor +=
                                ElementSize(
                                    uvType);

                            break;
                        }

                    case 'S':
                        cursor +=
                            ElementSize(
                                section.IndicesType) +
                            ElementSize(
                                section.WeightsType);

                        break;

                    case 'D':
                        cursor +=
                            ElementSize(
                                section.DiffuseType);

                        break;

                    case 'I':
                        cursor +=
                            ElementSize(
                                section.IndexType);

                        break;

                    default:
                        throw new InvalidDataException(
                            $"Unsupported UGX vertex pack-order token '{token}'.");
                }
            }

            if (cursor -
                    vertexOffset >
                section.VertexStride)
            {
                throw new InvalidDataException(
                    "UGX vertex pack order exceeds its declared stride.");
            }

            return result;
        }

        private static int SkipIndexSuffix(
            string order,
            int index)
        {
            if (index +
                    1 <
                order.Length &&
                char.IsDigit(
                    order[
                        index +
                        1]))
            {
                return index +
                    1;
            }

            return index;
        }

        private static int ElementSize(
            int type)
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

        private static Vector4 ReadElement(
            byte[] data,
            int offset,
            int type,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                ElementSize(
                    type));

            switch (type)
            {
                case 0:
                    return Vector4.Zero;

                case 1:
                    return new Vector4(
                        ReadSingle(
                            data,
                            offset,
                            bigEndian),
                        0,
                        0,
                        1);

                case 2:
                    return new Vector4(
                        ReadSingle(
                            data,
                            offset,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            4,
                            bigEndian),
                        0,
                        1);

                case 3:
                    return new Vector4(
                        ReadSingle(
                            data,
                            offset,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            4,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            8,
                            bigEndian),
                        1);

                case 4:
                    return new Vector4(
                        ReadSingle(
                            data,
                            offset,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            4,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            8,
                            bigEndian),
                        ReadSingle(
                            data,
                            offset +
                            12,
                            bigEndian));

                case 5:
                    // D3DCOLOR is stored as BGRA bytes in memory.
                    return new Vector4(
                        data[
                            offset +
                            2] /
                        255.0f,
                        data[
                            offset +
                            1] /
                        255.0f,
                        data[
                            offset +
                            0] /
                        255.0f,
                        data[
                            offset +
                            3] /
                        255.0f);

                case 6:
                    return new Vector4(
                        data[
                            offset +
                            0],
                        data[
                            offset +
                            1],
                        data[
                            offset +
                            2],
                        data[
                            offset +
                            3]);

                case 7:
                    return new Vector4(
                        ReadInt16(
                            data,
                            offset,
                            bigEndian),
                        ReadInt16(
                            data,
                            offset +
                            2,
                            bigEndian),
                        0,
                        1);

                case 8:
                    return new Vector4(
                        ReadInt16(
                            data,
                            offset,
                            bigEndian),
                        ReadInt16(
                            data,
                            offset +
                            2,
                            bigEndian),
                        ReadInt16(
                            data,
                            offset +
                            4,
                            bigEndian),
                        ReadInt16(
                            data,
                            offset +
                            6,
                            bigEndian));

                case 9:
                    return new Vector4(
                        data[
                            offset +
                            0] /
                        255.0f,
                        data[
                            offset +
                            1] /
                        255.0f,
                        data[
                            offset +
                            2] /
                        255.0f,
                        data[
                            offset +
                            3] /
                        255.0f);

                case 10:
                    return new Vector4(
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset,
                                bigEndian)),
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset +
                                2,
                                bigEndian)),
                        0,
                        1);

                case 11:
                    return new Vector4(
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset,
                                bigEndian)),
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset +
                                2,
                                bigEndian)),
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset +
                                4,
                                bigEndian)),
                        NormaliseSigned16(
                            ReadInt16(
                                data,
                                offset +
                                6,
                                bigEndian)));

                case 12:
                    return new Vector4(
                        ReadUInt16(
                            data,
                            offset,
                            bigEndian) /
                        65535.0f,
                        ReadUInt16(
                            data,
                            offset +
                            2,
                            bigEndian) /
                        65535.0f,
                        0,
                        1);

                case 13:
                    return new Vector4(
                        ReadUInt16(
                            data,
                            offset,
                            bigEndian) /
                        65535.0f,
                        ReadUInt16(
                            data,
                            offset +
                            2,
                            bigEndian) /
                        65535.0f,
                        ReadUInt16(
                            data,
                            offset +
                            4,
                            bigEndian) /
                        65535.0f,
                        ReadUInt16(
                            data,
                            offset +
                            6,
                            bigEndian) /
                        65535.0f);

                case 14:
                    return DecodePacked101010(
                        ReadUInt32(
                            data,
                            offset,
                            bigEndian),
                        signed:
                            false,
                        normalised:
                            false);

                case 15:
                    return DecodePacked101010(
                        ReadUInt32(
                            data,
                            offset,
                            bigEndian),
                        signed:
                            true,
                        normalised:
                            true);

                case 16:
                    return new Vector4(
                        ReadHalf(
                            data,
                            offset,
                            bigEndian),
                        ReadHalf(
                            data,
                            offset +
                            2,
                            bigEndian),
                        0,
                        1);

                case 17:
                    return new Vector4(
                        ReadHalf(
                            data,
                            offset,
                            bigEndian),
                        ReadHalf(
                            data,
                            offset +
                            2,
                            bigEndian),
                        ReadHalf(
                            data,
                            offset +
                            4,
                            bigEndian),
                        ReadHalf(
                            data,
                            offset +
                            6,
                            bigEndian));

                case 18:
                    return new Vector4(
                        ReadHalf(
                            data,
                            offset,
                            bigEndian),
                        0,
                        0,
                        1);

                case 19:
                    return DecodePacked101010(
                        ReadUInt32(
                            data,
                            offset,
                            bigEndian),
                        signed:
                            false,
                        normalised:
                            true);

                case 20:
                    return DecodePacked101010(
                        ReadUInt32(
                            data,
                            offset,
                            bigEndian),
                        signed:
                            true,
                        normalised:
                            true);

                default:
                    throw new InvalidDataException(
                        $"Unsupported UGX vertex element type {type}.");
            }
        }

        private static Vector4 DecodePacked101010(
            uint value,
            bool signed,
            bool normalised)
        {
            int x =
                (int)(
                    value &
                    0x3FF);

            int y =
                (int)(
                    (
                        value >>
                        10
                    ) &
                    0x3FF);

            int z =
                (int)(
                    (
                        value >>
                        20
                    ) &
                    0x3FF);

            if (signed)
            {
                x =
                    SignExtend10(
                        x);

                y =
                    SignExtend10(
                        y);

                z =
                    SignExtend10(
                        z);
            }

            if (normalised)
            {
                float divisor =
                    signed
                        ? 511.0f
                        : 1023.0f;

                return new Vector4(
                    Math.Clamp(
                        x /
                        divisor,
                        -1.0f,
                        1.0f),
                    Math.Clamp(
                        y /
                        divisor,
                        -1.0f,
                        1.0f),
                    Math.Clamp(
                        z /
                        divisor,
                        -1.0f,
                        1.0f),
                    1);
            }

            return new Vector4(
                x,
                y,
                z,
                1);
        }

        private static int SignExtend10(
            int value)
        {
            return (
                value &
                0x200
            ) !=
            0
                ? value |
                  unchecked(
                      (int)0xFFFFFC00)
                : value;
        }

        private static float NormaliseSigned16(
            short value)
        {
            return Math.Max(
                -1.0f,
                value /
                32767.0f);
        }

        private static float ReadHalf(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            ushort bits =
                ReadUInt16(
                    data,
                    offset,
                    bigEndian);

            return (float)
                BitConverter.UInt16BitsToHalf(
                    bits);
        }

        private static void RecalculateNormals(
            MeshGeometry3D geometry)
        {
            Vector3[] normals =
                new Vector3[
                    geometry.Positions.Count];

            for (int i = 0;
                 i +
                     2 <
                 geometry.TriangleIndices.Count;
                 i +=
                 3)
            {
                int ia =
                    geometry.TriangleIndices[
                        i +
                        0];

                int ib =
                    geometry.TriangleIndices[
                        i +
                        1];

                int ic =
                    geometry.TriangleIndices[
                        i +
                        2];

                Point3D a =
                    geometry.Positions[
                        ia];

                Point3D b =
                    geometry.Positions[
                        ib];

                Point3D c =
                    geometry.Positions[
                        ic];

                Vector3 va =
                    new Vector3(
                        (float)a.X,
                        (float)a.Y,
                        (float)a.Z);

                Vector3 vb =
                    new Vector3(
                        (float)b.X,
                        (float)b.Y,
                        (float)b.Z);

                Vector3 vc =
                    new Vector3(
                        (float)c.X,
                        (float)c.Y,
                        (float)c.Z);

                Vector3 normal =
                    Vector3.Cross(
                        vb -
                        va,
                        vc -
                        va);

                normals[
                    ia] +=
                        normal;

                normals[
                    ib] +=
                        normal;

                normals[
                    ic] +=
                        normal;
            }

            geometry.Normals.Clear();

            foreach (Vector3 normal
                     in normals)
            {
                Vector3 value =
                    normal.LengthSquared() >
                        0.000001f
                        ? Vector3.Normalize(
                            normal)
                        : Vector3.UnitY;

                geometry.Normals.Add(
                    new Vector3D(
                        value.X,
                        value.Y,
                        value.Z));
            }
        }

        private static ImageSource? TryLoadDiffuseTexture(
            EraArchiveInfo archive,
            IReadOnlyList<string> diffusePaths,
            int materialIndex)
        {
            if (diffusePaths.Count ==
                0)
            {
                return null;
            }

            int textureIndex =
                Math.Clamp(
                    materialIndex,
                    0,
                    diffusePaths.Count -
                    1);

            string path =
                diffusePaths[
                    textureIndex];

            foreach (EraArchiveInfo textureArchive
                     in HaloWarsAssetArchiveService
                         .GetSearchArchives(
                             archive))
            {
                EraChunkInfo? chunk =
                    FindTextureChunk(
                        textureArchive,
                        path);

                if (chunk ==
                    null)
                {
                    continue;
                }

                string cacheKey =
                    textureArchive.FilePath +
                    "|tex|" +
                    chunk.Index +
                    "|" +
                    chunk.Adler32.ToString(
                        "X8");

                if (TextureCache.TryGetValue(
                        cacheKey,
                        out BitmapSource? cached))
                {
                    if (cached !=
                        null)
                    {
                        return cached;
                    }

                    continue;
                }

                try
                {
                    BitmapSource? decoded =
                        DdsTextureDecoderService.TryDecode(
                            EraExtractionService.ExtractChunk(
                                textureArchive,
                                chunk));

                    TextureCache[
                        cacheKey] =
                            decoded;

                    if (decoded !=
                        null)
                    {
                        return decoded;
                    }
                }
                catch
                {
                    TextureCache[
                        cacheKey] =
                            null;
                }
            }

            return null;
        }

        private static EraChunkInfo? FindTextureChunk(
            EraArchiveInfo archive,
            string materialPath)
        {
            string normalised =
                materialPath
                    .Replace(
                        '/',
                        '\\')
                    .Trim()
                    .Trim('"')
                    .TrimStart(
                        '\\');

            if (!normalised.StartsWith(
                    "art\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                normalised =
                    "art\\" +
                    normalised;
            }

            if (!normalised.EndsWith(
                    ".ddx",
                    StringComparison.OrdinalIgnoreCase))
            {
                normalised +=
                    ".ddx";
            }

            foreach (EraChunkInfo chunk
                     in archive.Chunks)
            {
                if (chunk.Index >
                        0 &&
                    chunk.FileName.Equals(
                        normalised,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return chunk;
                }
            }

            string baseName =
                Path.GetFileName(
                    normalised);

            return archive.Chunks
                .Where(
                    chunk =>
                        chunk.Index >
                            0 &&
                        chunk.FileName.EndsWith(
                            "\\" +
                            baseName,
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    chunk =>
                        chunk.FileName.Length)
                .FirstOrDefault();
        }

        private static List<string> ExtractDiffuseTexturePaths(
            byte[] materialChunk)
        {
            List<string> result =
                new();

            int start =
                -1;

            for (int i = 0;
                 i <=
                 materialChunk.Length;
                 i++)
            {
                bool printable =
                    i <
                        materialChunk.Length &&
                    materialChunk[
                        i] >=
                        32 &&
                    materialChunk[
                        i] <=
                        126;

                if (printable)
                {
                    if (start <
                        0)
                    {
                        start =
                            i;
                    }

                    continue;
                }

                if (start >=
                        0 &&
                    i -
                        start >=
                        4)
                {
                    string text =
                        Encoding.ASCII.GetString(
                            materialChunk,
                            start,
                            i -
                            start);

                    string lower =
                        text.ToLowerInvariant();

                    if ((lower.EndsWith(
                             "_df",
                             StringComparison.Ordinal) ||
                         lower.EndsWith(
                             "_df.ddx",
                             StringComparison.Ordinal)) &&
                        (text.Contains(
                             '\\') ||
                         text.Contains(
                             '/')))
                    {
                        if (!result.Contains(
                                text,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(
                                text);
                        }
                    }
                }

                start =
                    -1;
            }

            return result;
        }

        private static string ReadPackedAsciiString(
            byte[] data,
            ulong pointer)
        {
            if (pointer ==
                    ulong.MaxValue ||
                pointer ==
                    0x00000000FFFFFFFFUL ||
                pointer >=
                    (ulong)data.Length)
            {
                return string.Empty;
            }

            int start =
                checked(
                    (int)pointer);

            int end =
                start;

            while (end <
                       data.Length &&
                   data[
                       end] !=
                       0)
            {
                end++;
            }

            if (end ==
                data.Length)
            {
                throw new InvalidDataException(
                    "UGX packed string is not null terminated.");
            }

            return Encoding.ASCII.GetString(
                data,
                start,
                end -
                start);
        }

        private static uint ReadUInt32(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                4);

            return bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4))
                : BinaryPrimitives.ReadUInt32LittleEndian(
                    data.AsSpan(
                        offset,
                        4));
        }

        private static int ReadInt32(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            return unchecked(
                (int)ReadUInt32(
                    data,
                    offset,
                    bigEndian));
        }

        private static ushort ReadUInt16(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                2);

            return bigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(
                    data.AsSpan(
                        offset,
                        2))
                : BinaryPrimitives.ReadUInt16LittleEndian(
                    data.AsSpan(
                        offset,
                        2));
        }

        private static short ReadInt16(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            return unchecked(
                (short)ReadUInt16(
                    data,
                    offset,
                    bigEndian));
        }

        private static ulong ReadUInt64(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                8);

            return bigEndian
                ? BinaryPrimitives.ReadUInt64BigEndian(
                    data.AsSpan(
                        offset,
                        8))
                : BinaryPrimitives.ReadUInt64LittleEndian(
                    data.AsSpan(
                        offset,
                        8));
        }

        private static float ReadSingle(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            uint bits =
                ReadUInt32(
                    data,
                    offset,
                    bigEndian);

            return BitConverter.Int32BitsToSingle(
                unchecked(
                    (int)bits));
        }

        private static void EnsureRange(
            byte[] data,
            int offset,
            int size)
        {
            if (offset <
                    0 ||
                size <
                    0 ||
                (long)offset +
                    size >
                    data.Length)
            {
                throw new InvalidDataException(
                    "UGX structure points outside its data buffer.");
            }
        }

        private sealed class ParsedSection
        {
            public int MaterialIndex
            {
                get;
                set;
            }

            public int RigidBoneIndex
            {
                get;
                set;
            }

            public int IndexOffset
            {
                get;
                set;
            }

            public int NumTris
            {
                get;
                set;
            }

            public int VertexOffset
            {
                get;
                set;
            }

            public int VertexBytes
            {
                get;
                set;
            }

            public int VertexStride
            {
                get;
                set;
            }

            public int NumVerts
            {
                get;
                set;
            }

            public string PackOrder
            {
                get;
                set;
            } =
                string.Empty;

            public int PositionType
            {
                get;
                set;
            }

            public int BasisType
            {
                get;
                set;
            }

            public int BasisScaleType
            {
                get;
                set;
            }

            public int TangentType
            {
                get;
                set;
            }

            public int NormalType
            {
                get;
                set;
            }

            public int[] UvTypes
            {
                get;
            } =
                new int[8];

            public int IndicesType
            {
                get;
                set;
            }

            public int WeightsType
            {
                get;
                set;
            }

            public int DiffuseType
            {
                get;
                set;
            }

            public int IndexType
            {
                get;
                set;
            }
        }

        private struct DecodedVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 TexCoord;
            public bool HasNormal;
            public bool HasTexCoord;
        }
    }
}
