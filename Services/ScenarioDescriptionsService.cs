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

        public static LooseScenarioRegistrationResult
    BuildOrUpdateLooseScenarioDescriptions(
        byte[] stockScenarioDescriptionsXmb,
        string? existingLooseXml,
        string targetScenarioFile)
        {
            if (stockScenarioDescriptionsXmb ==
                null)
            {
                throw new ArgumentNullException(
                    nameof(stockScenarioDescriptionsXmb));
            }


            if (string.IsNullOrWhiteSpace(
                    targetScenarioFile))
            {
                throw new ArgumentException(
                    "Target scenario filename is empty.",
                    nameof(targetScenarioFile));
            }


            string stockXml =
                XmbDocumentService.Read(
                    stockScenarioDescriptionsXmb);


            XDocument stockDocument =
                XDocument.Parse(
                    stockXml);


            XDocument document;


            if (!string.IsNullOrWhiteSpace(
                    existingLooseXml))
            {
                try
                {
                    document =
                        XDocument.Parse(
                            existingLooseXml);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "The existing loose scenariodescriptions.xml " +
                        "could not be parsed.\n\n" +
                        "Delete or restore that file before trying again.",
                        ex);
                }
            }
            else
            {
                document =
                    new XDocument(
                        stockDocument);
            }


            string target =
                NormalizeScenarioPath(
                    targetScenarioFile);


            string templateFile =
                FindTemplateScenarioFile(
                    stockScenarioDescriptionsXmb,
                    target);


            // =========================================================
            // Find the STOCK template.
            // =========================================================

            XElement? stockTemplate =
                stockDocument
                    .Descendants()
                    .Where(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "ScenarioInfo",
                                StringComparison.Ordinal))
                    .FirstOrDefault(
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
                                       templateFile,
                                       StringComparison.OrdinalIgnoreCase);
                        });


            if (stockTemplate ==
                null)
            {
                throw new InvalidDataException(
                    "Unable to locate the stock ScenarioInfo template:\n\n" +
                    templateFile);
            }


            // =========================================================
            // Build a clean custom entry from the stock template.
            // =========================================================

            XElement customEntry =
                new XElement(
                    stockTemplate);


            XAttribute? customFile =
                customEntry.Attribute(
                    "File");


            if (customFile ==
                null)
            {
                throw new InvalidDataException(
                    "Stock ScenarioInfo template contains no File attribute.");
            }


            customFile.Value =
                target;


            // =========================================================
            // Find EXISTING custom registration.
            //
            // IMPORTANT:
            // We only look for THIS custom target.
            //
            // We do NOT remove any other repeated stock File entries.
            // =========================================================

            List<XElement> existingTargets =
                document
                    .Descendants()
                    .Where(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "ScenarioInfo",
                                StringComparison.Ordinal))
                    .Where(
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
                                       target,
                                       StringComparison.OrdinalIgnoreCase);
                        })
                    .ToList();


            bool added;


            if (existingTargets.Count ==
                0)
            {
                added =
                    true;


                // -----------------------------------------------------
                // Insert immediately after the corresponding stock map.
                // -----------------------------------------------------

                XElement? looseTemplate =
                    document
                        .Descendants()
                        .Where(
                            x =>
                                string.Equals(
                                    x.Name.LocalName,
                                    "ScenarioInfo",
                                    StringComparison.Ordinal))
                        .FirstOrDefault(
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
                                           templateFile,
                                           StringComparison.OrdinalIgnoreCase);
                            });


                if (looseTemplate ==
                    null)
                {
                    throw new InvalidDataException(
                        "Unable to locate the stock template in the " +
                        "loose ScenarioDescriptions document:\n\n" +
                        templateFile);
                }


                looseTemplate.AddAfterSelf(
                    customEntry);
            }
            else
            {
                added =
                    false;


                // -----------------------------------------------------
                // Update the first existing registration.
                // -----------------------------------------------------

                existingTargets[0]
                    .ReplaceWith(
                        customEntry);


                // -----------------------------------------------------
                // Only remove accidental duplicates of THIS CUSTOM MAP.
                //
                // Stock maps are never deduplicated.
                // -----------------------------------------------------

                for (int i = 1;
                     i < existingTargets.Count;
                     i++)
                {
                    existingTargets[i]
                        .Remove();
                }
            }


            string outputXml =
                SerializeDocumentUtf8(
                    document);


            // =========================================================
            // Verification.
            // =========================================================

            XDocument verification =
                XDocument.Parse(
                    outputXml);


            int targetCount =
                verification
                    .Descendants()
                    .Where(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "ScenarioInfo",
                                StringComparison.Ordinal))
                    .Count(
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
                                       target,
                                       StringComparison.OrdinalIgnoreCase);
                        });


            if (targetCount !=
                1)
            {
                throw new InvalidDataException(
                    "ScenarioDescriptions verification failed.\n\n" +
                    $"Expected exactly one custom entry for:\n" +
                    $"{target}\n\n" +
                    $"Found: {targetCount}");
            }


            int scenarioCount =
                verification
                    .Descendants()
                    .Count(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "ScenarioInfo",
                                StringComparison.Ordinal));


            return new LooseScenarioRegistrationResult
            {
                Xml =
                    outputXml,

                TargetScenarioFile =
                    target,

                TemplateScenarioFile =
                    templateFile,

                Added =
                    added,

                RemovedDuplicateCount =
                    Math.Max(
                        0,
                        existingTargets.Count - 1),

                ScenarioCount =
                    scenarioCount
            };
        }


        public static bool ContainsScenarioFile(
            string xml,
            string scenarioFile)
        {
            if (string.IsNullOrWhiteSpace(
                    xml))
            {
                return false;
            }


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


        private static string SerializeDocumentUtf8(
            XDocument document)
        {
            System.Text.StringBuilder builder =
                new System.Text.StringBuilder();


            using Utf8StringWriter stringWriter =
                new Utf8StringWriter(
                    builder);


            System.Xml.XmlWriterSettings settings =
                new System.Xml.XmlWriterSettings
                {
                    Indent =
                        true,

                    OmitXmlDeclaration =
                        false,

                    Encoding =
                        new System.Text.UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false),

                    NewLineChars =
                        Environment.NewLine,

                    NewLineHandling =
                        System.Xml.NewLineHandling.Replace
                };


            using (System.Xml.XmlWriter writer =
                   System.Xml.XmlWriter.Create(
                       stringWriter,
                       settings))
            {
                document.Save(
                    writer);
            }


            string result =
                builder.ToString();


            // Defensive verification.
            if (result.Contains(
                    "encoding=\"utf-16\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Internal XML encoding error: " +
                    "ScenarioDescriptions was serialized as UTF-16.");
            }


            return result;
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

    internal sealed class Utf8StringWriter :
    StringWriter
    {
        public Utf8StringWriter(
            System.Text.StringBuilder builder)
            : base(
                builder,
                System.Globalization.CultureInfo.InvariantCulture)
        {
        }


        public override System.Text.Encoding Encoding =>
            new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);
    }

    public sealed class LooseScenarioRegistrationResult
    {
        public string Xml
        {
            get;
            init;
        } =
            string.Empty;


        public string TargetScenarioFile
        {
            get;
            init;
        } =
            string.Empty;


        public string TemplateScenarioFile
        {
            get;
            init;
        } =
            string.Empty;


        public bool Added
        {
            get;
            init;
        }


        public int RemovedDuplicateCount
        {
            get;
            init;
        }


        public int ScenarioCount
        {
            get;
            init;
        }
    }
}