using Ensemble.Models;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Ensemble.Services
{
    public static class StringTableService
    {
        private const long FirstCustomLocId =
            60000;


        // =========================================================
        // FIND ALL DE LANGUAGE STRING TABLES
        //
        // Halo Wars DE stores:
        //
        // data\stringtable-en.xml.xmb
        // data\stringtable-de.xml.xmb
        // data\stringtable-fr.xml.xmb
        // etc.
        // =========================================================

        public static List<LocalizedStringTableSource>
            FindLocalizedStringTables(
                EraArchiveInfo archive)
        {
            if (archive == null)
            {
                throw new ArgumentNullException(
                    nameof(archive));
            }


            List<LocalizedStringTableSource> result =
                new();


            const string prefix =
                "stringtable-";

            const string suffix =
                ".xml.xmb";


            foreach (EraChunkInfo chunk
                     in archive.Chunks)
            {
                string normalized =
                    NormalizeArchivePath(
                        chunk.FileName);


                int slash =
                    normalized.LastIndexOf(
                        '\\');


                string leaf =
                    slash >= 0
                        ? normalized[
                            (slash + 1)..]
                        : normalized;


                if (!leaf.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    !leaf.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }


                string languageCode =
                    leaf[
                        prefix.Length..^suffix.Length];


                if (string.IsNullOrWhiteSpace(
                        languageCode))
                {
                    continue;
                }


                result.Add(
                    new LocalizedStringTableSource(
                        chunk,
                        languageCode,
                        $"stringtable-{languageCode}.xml"));
            }


            result =
                result
                    .OrderBy(
                        x => x.LanguageCode,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();


            if (result.Count == 0)
            {
                throw new InvalidDataException(
                    "root.era contains no Halo Wars DE " +
                    "language StringTables.");
            }


            return result;
        }


        // =========================================================
        // FIND A FREE CUSTOM LOCALIZATION ID
        //
        // Check EVERY language table and all existing loose tables
        // so the chosen ID is safe globally.
        // =========================================================

        public static long FindFreeCustomStringId(
            IEnumerable<byte[]> stockStringTableXmbs,
            IEnumerable<string?> existingLooseXmls)
        {
            HashSet<long> usedIds =
                new();


            foreach (byte[] xmb
                     in stockStringTableXmbs)
            {
                string xml =
                    XmbDocumentService.Read(
                        xmb);


                XDocument document =
                    XDocument.Parse(
                        xml);


                CollectStringIds(
                    document,
                    usedIds);
            }


            foreach (string? xml
                     in existingLooseXmls)
            {
                if (string.IsNullOrWhiteSpace(
                        xml))
                {
                    continue;
                }


                try
                {
                    XDocument document =
                        XDocument.Parse(
                            xml);


                    CollectStringIds(
                        document,
                        usedIds);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "An existing loose Halo Wars StringTable " +
                        "could not be parsed.",
                        ex);
                }
            }


            for (long id = FirstCustomLocId;
                 id < int.MaxValue;
                 id++)
            {
                if (!usedIds.Contains(
                        id))
                {
                    return id;
                }
            }


            throw new InvalidDataException(
                "Unable to allocate a custom localization ID.");
        }


        // =========================================================
        // BUILD ONE LANGUAGE-SPECIFIC LOOSE STRING TABLE
        // =========================================================

        public static string BuildOrUpdateLooseStringTable(
            byte[] stockStringTableXmb,
            string? existingLooseXml,
            long stringId,
            string value)
        {
            if (stockStringTableXmb == null)
            {
                throw new ArgumentNullException(
                    nameof(stockStringTableXmb));
            }


            if (string.IsNullOrWhiteSpace(
                    value))
            {
                throw new ArgumentException(
                    "Custom map name cannot be empty.",
                    nameof(value));
            }


            string stockXml =
                XmbDocumentService.Read(
                    stockStringTableXmb);


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
                        "The existing loose StringTable " +
                        "could not be parsed.",
                        ex);
                }
            }
            else
            {
                document =
                    new XDocument(
                        stockDocument);
            }


            // =====================================================
            // Original Ensemble schema uses:
            //
            // <Language name="English">
            //     <String _locID="...">Text</String>
            // </Language>
            //
            // DE's per-language files may still retain this layout.
            // =====================================================

            List<XElement> languageNodes =
                document
                    .Descendants()
                    .Where(
                        x =>
                            string.Equals(
                                x.Name.LocalName,
                                "Language",
                                StringComparison.Ordinal))
                    .ToList();


            if (languageNodes.Count > 0)
            {
                foreach (XElement language
                         in languageNodes)
                {
                    UpsertString(
                        language,
                        stringId,
                        value);
                }
            }
            else
            {
                // Defensive support for a DE table whose String
                // entries live directly beneath the root.
                XElement root =
                    document.Root
                    ?? throw new InvalidDataException(
                        "StringTable XML contains no root node.");


                UpsertString(
                    root,
                    stringId,
                    value);
            }


            string output =
                SerializeDocumentUtf8(
                    document);


            if (!ContainsString(
                    output,
                    stringId,
                    value))
            {
                throw new InvalidDataException(
                    "StringTable verification failed after " +
                    "adding the custom map name.");
            }


            return output;
        }


        // =========================================================
        // VERIFY
        // =========================================================

        public static bool ContainsString(
            string xml,
            long stringId,
            string expectedValue)
        {
            if (string.IsNullOrWhiteSpace(
                    xml))
            {
                return false;
            }


            XDocument document =
                XDocument.Parse(
                    xml);


            return document
                .Descendants()
                .Where(
                    x =>
                        string.Equals(
                            x.Name.LocalName,
                            "String",
                            StringComparison.Ordinal))
                .Any(
                    x =>
                    {
                        string? idText =
                            x.Attribute(
                                "_locID")
                            ?.Value;


                        return
                            long.TryParse(
                                idText,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out long id)
                            &&
                            id ==
                                stringId
                            &&
                            string.Equals(
                                x.Value,
                                expectedValue,
                                StringComparison.Ordinal);
                    });
        }


        private static void UpsertString(
            XElement parent,
            long stringId,
            string value)
        {
            XElement? existing =
                parent
                    .Elements()
                    .FirstOrDefault(
                        x =>
                        {
                            if (!string.Equals(
                                    x.Name.LocalName,
                                    "String",
                                    StringComparison.Ordinal))
                            {
                                return false;
                            }


                            string? idText =
                                x.Attribute(
                                    "_locID")
                                ?.Value;


                            return
                                long.TryParse(
                                    idText,
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out long id)
                                &&
                                id ==
                                    stringId;
                        });


            if (existing != null)
            {
                existing.Value =
                    value;

                return;
            }


            parent.Add(
                new XElement(
                    "String",

                    new XAttribute(
                        "_locID",
                        stringId.ToString(
                            CultureInfo.InvariantCulture)),

                    new XAttribute(
                        "category",
                        "Skirmish"),

                    new XAttribute(
                        "subtitle",
                        "false"),

                    value));
        }


        private static void CollectStringIds(
            XDocument document,
            HashSet<long> result)
        {
            foreach (XElement element
                     in document
                         .Descendants()
                         .Where(
                             x =>
                                 string.Equals(
                                     x.Name.LocalName,
                                     "String",
                                     StringComparison.Ordinal)))
            {
                string? value =
                    element.Attribute(
                        "_locID")
                    ?.Value;


                if (long.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long id))
                {
                    result.Add(
                        id);
                }
            }
        }


        private static string SerializeDocumentUtf8(
            XDocument document)
        {
            System.Text.StringBuilder builder =
                new();


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


            return builder.ToString();
        }


        private static string NormalizeArchivePath(
            string path)
        {
            return path
                .Replace(
                    '/',
                    '\\')
                .TrimStart(
                    '\\');
        }
    }


    public sealed class LocalizedStringTableSource
    {
        public LocalizedStringTableSource(
            EraChunkInfo chunk,
            string languageCode,
            string looseFileName)
        {
            Chunk =
                chunk;

            LanguageCode =
                languageCode;

            LooseFileName =
                looseFileName;
        }


        public EraChunkInfo Chunk
        {
            get;
        }


        public string LanguageCode
        {
            get;
        }


        public string LooseFileName
        {
            get;
        }
    }
}