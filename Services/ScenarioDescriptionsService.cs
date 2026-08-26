using Ensemble.Models;
using System.IO;
using System.Xml.Linq;

namespace Ensemble.Services
{
    public static class ScenarioDescriptionsService
    {
        public static EraChunkInfo FindScenarioDescriptionsChunk(
            EraArchiveInfo archive)
        {
            if (archive == null)
            {
                throw new ArgumentNullException(
                    nameof(archive));
            }


            EraChunkInfo? result =
                archive.Chunks
                    .FirstOrDefault(
                        x =>
                            NormalizeArchivePath(
                                x.FileName)
                            .EndsWith(
                                "scenariodescriptions.xml.xmb",
                                StringComparison.OrdinalIgnoreCase));


            if (result ==
                null)
            {
                throw new InvalidDataException(
                    "root.era does not contain " +
                    "ScenarioDescriptions.xml.xmb.");
            }


            return result;
        }


        public static string BuildScenarioRegistrationPath(
            EraChunkInfo scenarioChunk)
        {
            if (scenarioChunk == null)
            {
                throw new ArgumentNullException(
                    nameof(scenarioChunk));
            }


            string path =
                NormalizeArchivePath(
                    scenarioChunk.FileName);


            const string scenarioPrefix =
                "scenario\\";


            if (!path.StartsWith(
                    scenarioPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Scenario archive filename does not begin " +
                    "with 'scenario\\'.\n\n" +
                    path);
            }


            if (!path.EndsWith(
                    ".scn.xmb",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Current scenario archive filename does not " +
                    "end in .scn.xmb.");
            }


            // Example:
            //
            // scenario\skirmish\design\blood_gulch\
            // blood_gulch_ensemble.scn.xmb
            //
            // becomes:
            //
            // skirmish\design\blood_gulch\
            // blood_gulch_ensemble.scn

            path =
                path[
                    scenarioPrefix.Length..];


            path =
                path[
                    ..^4]; // remove ".xmb"


            return path;
        }


        public static string FindTemplateScenarioFile(
            byte[] scenarioDescriptionsXmb,
            string targetScenarioFile)
        {
            string xml =
                XmbDocumentService.Read(
                    scenarioDescriptionsXmb);


            XDocument document =
                XDocument.Parse(
                    xml);


            string target =
                NormalizeScenarioPath(
                    targetScenarioFile);


            List<XElement> scenarioInfos =
                document
                    .Descendants()
                    .Where(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "ScenarioInfo",
                                StringComparison.Ordinal))
                    .ToList();


            // Don't accidentally register the same map twice.
            foreach (XElement info
                     in scenarioInfos)
            {
                string? file =
                    info.Attribute(
                        "File")
                    ?.Value;


                if (file ==
                    null)
                {
                    continue;
                }


                if (string.Equals(
                        NormalizeScenarioPath(
                            file),
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "This custom scenario is already " +
                        "registered:\n\n" +
                        target);
                }
            }


            string targetDirectory =
                GetScenarioDirectory(
                    target);


            List<XElement> candidates =
                scenarioInfos
                    .Where(
                        info =>
                        {
                            string? file =
                                info.Attribute(
                                    "File")
                                ?.Value;

                            if (file ==
                                null)
                            {
                                return false;
                            }


                            return string.Equals(
                                GetScenarioDirectory(
                                    NormalizeScenarioPath(
                                        file)),
                                targetDirectory,
                                StringComparison.OrdinalIgnoreCase);
                        })
                    .ToList();


            if (candidates.Count ==
                0)
            {
                throw new InvalidDataException(
                    "Unable to find a stock ScenarioInfo " +
                    "in the same map directory as:\n\n" +
                    target);
            }


            // -----------------------------------------------------
            // Best case:
            //
            // directory:
            // skirmish\design\blood_gulch\
            //
            // expected stock file:
            // skirmish\design\blood_gulch\blood_gulch.scn
            // -----------------------------------------------------

            string trimmedDirectory =
                targetDirectory
                    .TrimEnd(
                        '\\');


            int lastSlash =
                trimmedDirectory
                    .LastIndexOf(
                        '\\');


            string folderName =
                lastSlash >=
                    0
                    ? trimmedDirectory[
                        (lastSlash + 1)..]
                    : trimmedDirectory;


            string expectedStockFile =
                targetDirectory +
                folderName +
                ".scn";


            XElement? exactStock =
                candidates
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                NormalizeScenarioPath(
                                    x.Attribute(
                                        "File")!
                                    .Value),
                                expectedStockFile,
                                StringComparison.OrdinalIgnoreCase));


            if (exactStock !=
                null)
            {
                return NormalizeScenarioPath(
                    exactStock.Attribute(
                        "File")!
                    .Value);
            }


            // Otherwise prefer a Final map.
            List<XElement> finalCandidates =
                candidates
                    .Where(
                        x =>
                            string.Equals(
                                x.Attribute(
                                    "Type")
                                ?.Value,
                                "Final",
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();


            if (finalCandidates.Count ==
                1)
            {
                return NormalizeScenarioPath(
                    finalCandidates[0]
                        .Attribute(
                            "File")!
                        .Value);
            }


            if (candidates.Count ==
                1)
            {
                return NormalizeScenarioPath(
                    candidates[0]
                        .Attribute(
                            "File")!
                        .Value);
            }


            throw new InvalidDataException(
                "Multiple possible ScenarioInfo templates " +
                "were found for this map directory.");
        }


        public static bool ContainsScenarioFile(
            byte[] scenarioDescriptionsXmb,
            string scenarioFile)
        {
            string xml =
                XmbDocumentService.Read(
                    scenarioDescriptionsXmb);


            XDocument document =
                XDocument.Parse(
                    xml);


            string expected =
                NormalizeScenarioPath(
                    scenarioFile);


            return document
                .Descendants()
                .Where(
                    x =>
                        string.Equals(
                            x.Name.LocalName,
                            "ScenarioInfo",
                            StringComparison.Ordinal))
                .Any(
                    x =>
                    {
                        string? file =
                            x.Attribute(
                                "File")
                            ?.Value;


                        return file !=
                                   null &&
                               string.Equals(
                                   NormalizeScenarioPath(
                                       file),
                                   expected,
                                   StringComparison.OrdinalIgnoreCase);
                    });
        }


        private static string NormalizeArchivePath(
            string value)
        {
            return value
                .Replace(
                    '/',
                    '\\')
                .TrimStart(
                    '\\')
                .Trim();
        }


        private static string NormalizeScenarioPath(
            string value)
        {
            return value
                .Replace(
                    '/',
                    '\\')
                .TrimStart(
                    '\\')
                .Trim();
        }


        private static string GetScenarioDirectory(
            string value)
        {
            int slash =
                value.LastIndexOf(
                    '\\');


            return slash >=
                    0
                    ? value[
                        ..(slash + 1)]
                    : string.Empty;
        }
    }
}