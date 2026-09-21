using System.Diagnostics;
using System.Text.Json;

namespace Ensemble.Services
{
    /// <summary>
    /// Unified Master Chief Collection .map import facade.
    ///
    /// All MCC engines are handled by the separate Ensemble.HaloMapBridge
    /// process, which uses the pinned Reclaimer format backend. Keeping that
    /// GPL-linked component out-of-process avoids coupling the main Ensemble
    /// editor assembly to Reclaimer while still giving one import workflow for
    /// every MCC Blam .map generation.
    ///
    /// The pre-existing native Reach reader remains as a fallback so Reach
    /// imports do not regress if the helper cannot be launched.
    /// </summary>
    internal static class HaloMccMapImportService
    {
        internal sealed class ExtractResult
        {
            public string SourceMapPath { get; init; } =
                string.Empty;

            public string ObjPath { get; init; } =
                string.Empty;

            public string SourceGame { get; init; } =
                string.Empty;

            public string Engine { get; init; } =
                string.Empty;

            public string CacheType { get; init; } =
                string.Empty;

            public string BuildString { get; init; } =
                string.Empty;

            public string ProviderName { get; init; } =
                string.Empty;

            public bool IsMcc { get; init; }

            public List<string> BspTags { get; init; } =
                new();

            public List<string> SkippedBspDiagnostics { get; init; } =
                new();

            public long VertexCount { get; init; }

            public long TriangleCount { get; init; }

            public int MeshPlacementCount { get; init; }

            public float AppliedScale { get; set; } =
                1.0f;

            public float SourceWidth { get; set; }

            public float SourceDepth { get; set; }
        }

        private sealed class BridgeResult
        {
            public bool Success { get; set; }

            public string SourceMapPath { get; set; } =
                string.Empty;

            public string ObjPath { get; set; } =
                string.Empty;

            public string SourceGame { get; set; } =
                string.Empty;

            public string Engine { get; set; } =
                string.Empty;

            public string CacheType { get; set; } =
                string.Empty;

            public string BuildString { get; set; } =
                string.Empty;

            public string ProviderName { get; set; } =
                string.Empty;

            public bool IsMcc { get; set; }

            public List<string> BspTags { get; set; } =
                new();

            public List<string> SkippedBspDiagnostics { get; set; } =
                new();

            public long VertexCount { get; set; }

            public long TriangleCount { get; set; }

            public int MeshPlacementCount { get; set; }

            public string? Error { get; set; }
        }

        public static ExtractResult ExtractMapToObj(
            string mapPath,
            string outputObjPath,
            float worldScale = 1.0f,
            Action<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(
                    mapPath) ||
                !File.Exists(
                    mapPath))
            {
                throw new FileNotFoundException(
                    "The selected Halo MCC .map file could not be found.",
                    mapPath);
            }

            if (!Path.GetExtension(
                    mapPath)
                    .Equals(
                        ".map",
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "MCC map import expects a .map cache file.");
            }

            if (string.IsNullOrWhiteSpace(
                    outputObjPath))
            {
                throw new ArgumentException(
                    "An output OBJ path is required.",
                    nameof(
                        outputObjPath));
            }

            if (!float.IsFinite(
                    worldScale) ||
                worldScale <=
                    0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(
                        worldScale),
                    "World scale must be positive and finite.");
            }

            string fullMapPath =
                Path.GetFullPath(
                    mapPath);

            string fullObjPath =
                Path.GetFullPath(
                    outputObjPath);

            string? outputDirectory =
                Path.GetDirectoryName(
                    fullObjPath);

            if (!string.IsNullOrWhiteSpace(
                    outputDirectory))
            {
                Directory.CreateDirectory(
                    outputDirectory);
            }

            Exception? bridgeError =
                null;

            string? helper =
                FindBridgeExecutable();

            if (!string.IsNullOrWhiteSpace(
                    helper))
            {
                try
                {
                    progress?.Invoke(
                        "Reading MCC cache through Ensemble Halo Map Bridge...");

                    ExtractResult bridgeResult =
                        ExtractWithBridge(
                            helper,
                            fullMapPath,
                            fullObjPath);

                    if (MathF.Abs(
                            worldScale -
                            1.0f) >
                        0.000001f)
                    {
                        HaloMapImportService
                            .ScaleGeneratedObj(
                                bridgeResult.ObjPath,
                                worldScale);
                    }

                    bridgeResult.AppliedScale =
                        worldScale;

                    return bridgeResult;
                }
                catch (Exception ex)
                {
                    bridgeError =
                        ex;

                    progress?.Invoke(
                        "MCC bridge could not decode this cache; checking the native Reach fallback...");
                }
            }
            else
            {
                bridgeError =
                    new FileNotFoundException(
                        "Ensemble.HaloMapBridge.exe is missing from the HaloMapBridge output folder.");
            }

            // ---------------------------------------------------------
            // Reach compatibility fallback.
            //
            // This intentionally tries the existing native parser after the
            // all-games bridge. If the source is Reach, current users retain
            // the proven v35-v37 path even if the helper is unavailable.
            // Other MCC generations will fail quickly here and receive the
            // combined, actionable bridge error below.
            // ---------------------------------------------------------
            try
            {
                progress?.Invoke(
                    "Trying Ensemble's native Halo Reach reader...");

                HaloMapImportService.ExtractResult reach =
                    HaloMapImportService
                        .ExtractReachMapToObj(
                            fullMapPath,
                            fullObjPath,
                            new HaloMapImportService.ExtractOptions
                            {
                                WorldScale =
                                    worldScale
                            },
                            progress);

                return new ExtractResult
                {
                    SourceMapPath =
                        reach.SourceMapPath,

                    ObjPath =
                        reach.ObjPath,

                    SourceGame =
                        "Halo: Reach",

                    Engine =
                        "HaloReach",

                    CacheType =
                        "MCC Reach native fallback",

                    BuildString =
                        reach.BuildString,

                    ProviderName =
                        "Ensemble Native Reach",

                    IsMcc =
                        true,

                    BspTags =
                        reach.BspTags.ToList(),

                    SkippedBspDiagnostics =
                        reach.SkippedBspDiagnostics.ToList(),

                    VertexCount =
                        reach.VertexCount,

                    TriangleCount =
                        reach.TriangleCount,

                    MeshPlacementCount =
                        reach.MeshPlacementCount,

                    AppliedScale =
                        reach.AppliedScale,

                    SourceWidth =
                        reach.SourceWidth,

                    SourceDepth =
                        reach.SourceDepth
                };
            }
            catch (Exception nativeReachError)
            {
                throw new InvalidDataException(
                    "Ensemble could not import this MCC .map.\n\n" +
                    "All-games bridge:\n" +
                    FormatExceptionMessage(
                        bridgeError)
                    +
                    "\n\nNative Reach fallback:\n" +
                    FormatExceptionMessage(
                        nativeReachError)
                    +
                    "\n\nSupported MCC .map engines in the all-games bridge are Halo CE, Halo 2, Halo 3, Halo 3: ODST, Halo: Reach, Halo 4, and Halo 2: Anniversary Multiplayer.\n\n" +
                    "For Halo CE Anniversary and Halo 2 Anniversary campaign maps, the .map contains the classic Blam BSP. Their remastered Saber visual layer is stored separately and is not part of this .map importer.",
                    bridgeError
                    ??
                    nativeReachError);
            }
        }

        private static ExtractResult ExtractWithBridge(
            string helperPath,
            string mapPath,
            string outputObjPath)
        {
            string jsonPath =
                outputObjPath +
                ".ensemble-mcc.json";

            try
            {
                ProcessStartInfo startInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            helperPath,

                        UseShellExecute =
                            false,

                        CreateNoWindow =
                            true,

                        RedirectStandardOutput =
                            true,

                        RedirectStandardError =
                            true
                    };

                startInfo.ArgumentList.Add(
                    "extract");

                startInfo.ArgumentList.Add(
                    "--map");

                startInfo.ArgumentList.Add(
                    mapPath);

                startInfo.ArgumentList.Add(
                    "--obj");

                startInfo.ArgumentList.Add(
                    outputObjPath);

                startInfo.ArgumentList.Add(
                    "--json");

                startInfo.ArgumentList.Add(
                    jsonPath);

                using Process process =
                    Process.Start(
                        startInfo)
                    ??
                    throw new InvalidOperationException(
                        "Windows could not start Ensemble.HaloMapBridge.");

                Task<string> standardOutput =
                    process.StandardOutput
                        .ReadToEndAsync();

                Task<string> standardError =
                    process.StandardError
                        .ReadToEndAsync();

                process.WaitForExit();

                Task.WaitAll(
                    standardOutput,
                    standardError);

                BridgeResult? result =
                    null;

                if (File.Exists(
                        jsonPath))
                {
                    string json =
                        File.ReadAllText(
                            jsonPath);

                    result =
                        JsonSerializer.Deserialize<BridgeResult>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive =
                                    true
                            });
                }

                if (process.ExitCode !=
                        0 ||
                    result ==
                        null ||
                    !result.Success)
                {
                    string detail =
                        result?.Error;

                    if (string.IsNullOrWhiteSpace(
                            detail))
                    {
                        detail =
                            standardError.Result;
                    }

                    if (string.IsNullOrWhiteSpace(
                            detail))
                    {
                        detail =
                            standardOutput.Result;
                    }

                    throw new InvalidDataException(
                        "The MCC map bridge failed.\n\n" +
                        (
                            string.IsNullOrWhiteSpace(
                                detail)
                                ? $"Bridge exit code: {process.ExitCode}"
                                : detail.Trim()
                        ));
                }

                if (!File.Exists(
                        outputObjPath))
                {
                    throw new InvalidDataException(
                        "The MCC map bridge reported success but produced no OBJ.");
                }

                if (result.TriangleCount <=
                        0 ||
                    result.VertexCount <=
                        0)
                {
                    throw new InvalidDataException(
                        "The MCC map bridge produced no usable BSP triangles.");
                }

                return new ExtractResult
                {
                    SourceMapPath =
                        result.SourceMapPath,

                    ObjPath =
                        outputObjPath,

                    SourceGame =
                        result.SourceGame,

                    Engine =
                        result.Engine,

                    CacheType =
                        result.CacheType,

                    BuildString =
                        result.BuildString,

                    ProviderName =
                        string.IsNullOrWhiteSpace(
                            result.ProviderName)
                            ? "Ensemble Halo Map Bridge"
                            : result.ProviderName,

                    IsMcc =
                        result.IsMcc,

                    BspTags =
                        result.BspTags
                        ??
                        new(),

                    SkippedBspDiagnostics =
                        result.SkippedBspDiagnostics
                        ??
                        new(),

                    VertexCount =
                        result.VertexCount,

                    TriangleCount =
                        result.TriangleCount,

                    MeshPlacementCount =
                        result.MeshPlacementCount,

                    AppliedScale =
                        1.0f
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(
                            jsonPath))
                    {
                        File.Delete(
                            jsonPath);
                    }
                }
                catch
                {
                    // Temporary diagnostics are disposable.
                }
            }
        }

        private static string? FindBridgeExecutable()
        {
            string baseDirectory =
                AppContext.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(
                    baseDirectory,
                    "HaloMapBridge",
                    "Ensemble.HaloMapBridge.exe"),

                Path.Combine(
                    baseDirectory,
                    "Ensemble.HaloMapBridge.exe"),

                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        "..",
                        "..",
                        "..",
                        "..",
                        "Ensemble.HaloMapBridge",
                        "bin",
#if DEBUG
                        "Debug",
#else
                        "Release",
#endif
                        "net8.0-windows",
                        "Ensemble.HaloMapBridge.exe"))
            };

            return candidates
                .FirstOrDefault(
                    File.Exists);
        }

        private static string FormatExceptionMessage(
            Exception? exception)
        {
            if (exception ==
                null)
            {
                return "No diagnostic was produced.";
            }

            string message =
                exception.Message;

            if (exception.InnerException !=
                    null &&
                !string.Equals(
                    exception.InnerException.Message,
                    message,
                    StringComparison.Ordinal))
            {
                message +=
                    "\n" +
                    exception.InnerException.Message;
            }

            return message;
        }
    }
}
