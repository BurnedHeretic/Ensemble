using Ensemble.Models;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Ensemble.Services
{
    /// <summary>
    /// Reads and modifies the separate Halo Wars scenario
    /// ArtObjects companion file:
    ///
    ///     map.sc2.xmb
    ///
    /// Large scenery such as trees, rocks and decorative
    /// environment objects can live here rather than in
    /// the main .scn.xmb gameplay scenario.
    /// </summary>
    internal static class ScenarioArtObjectsService
    {
        internal sealed class VegetationRemovalResult
        {
            public byte[] ModifiedXmb
            {
                get;
                init;
            } =
                Array.Empty<byte>();


            public int RemovedObjectCount
            {
                get;
                init;
            }


            public List<string> RemovedObjectTypes
            {
                get;
                init;
            } =
                new();
        }


        // =========================================================
        // COUNT
        // =========================================================

        public static int CountVegetationObjects(
            byte[] sc2XmbData)
        {
            ArgumentNullException.ThrowIfNull(
                sc2XmbData);


            string xml =
                XmbDocumentService.Read(
                    sc2XmbData);


            XDocument document =
                XDocument.Parse(
                    xml);


            return FindVegetationObjects(
                document)
                .Count;
        }


        // =========================================================
        // REMOVE
        // =========================================================

        public static VegetationRemovalResult RemoveAllVegetation(
            byte[] originalSc2XmbData)
        {
            ArgumentNullException.ThrowIfNull(
                originalSc2XmbData);


            string xml =
                XmbDocumentService.Read(
                    originalSc2XmbData);


            XDocument document =
                XDocument.Parse(
                    xml);


            List<ArtVegetationObject> vegetation =
                FindVegetationObjects(
                    document);


            if (vegetation.Count ==
                0)
            {
                return new VegetationRemovalResult
                {
                    ModifiedXmb =
                        originalSc2XmbData
                            .ToArray(),

                    RemovedObjectCount =
                        0
                };
            }


            // XmbDocumentService already has a proven structural
            // object deletion path. We only need to give it the
            // IDs that should disappear from this XMB.

            ScenarioMap edit =
                new ScenarioMap();


            foreach (ArtVegetationObject obj
                     in vegetation)
            {
                edit.DeletedObjectIds.Add(
                    obj.Id);
            }


            byte[] rebuilt =
                XmbDocumentService
                    .WriteScenario(
                        originalSc2XmbData,
                        edit);


            // =====================================================
            // ROUND-TRIP VERIFICATION
            // =====================================================

            string verificationXml =
                XmbDocumentService.Read(
                    rebuilt);


            XDocument verificationDocument =
                XDocument.Parse(
                    verificationXml);


            List<ArtVegetationObject>
                remaining =
                    FindVegetationObjects(
                        verificationDocument);


            if (remaining.Count !=
                0)
            {
                throw new InvalidDataException(
                    "SC2 vegetation removal verification failed.\n\n" +
                    $"{remaining.Count} recognised vegetation " +
                    "objects remain.");
            }


            return new VegetationRemovalResult
            {
                ModifiedXmb =
                    rebuilt,

                RemovedObjectCount =
                    vegetation.Count,

                RemovedObjectTypes =
                    vegetation
                        .Select(
                            x =>
                                x.Type)
                        .Where(
                            x =>
                                !string.IsNullOrWhiteSpace(
                                    x))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            x =>
                                x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
            };
        }


        // =========================================================
        // ART OBJECT DISCOVERY
        // =========================================================

        private static List<ArtVegetationObject>
            FindVegetationObjects(
                XDocument document)
        {
            XElement? root =
                document.Root;


            if (root ==
                null)
            {
                throw new InvalidDataException(
                    "SC2 XMB contains no XML root.");
            }


            List<ArtVegetationObject> result =
                new();


            IEnumerable<XElement> objects =
                root
                    .Descendants()
                    .Where(
                        element =>
                            string.Equals(
                                element.Name.LocalName,
                                "Object",
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            element.Parent !=
                                null
                            &&
                            string.Equals(
                                element.Parent.Name.LocalName,
                                "Objects",
                                StringComparison.OrdinalIgnoreCase));


            foreach (XElement element
                     in objects)
            {
                string idText =
                    element
                        .Attribute(
                            "ID")
                        ?.Value
                    ??
                    string.Empty;


                if (!int.TryParse(
                        idText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int id))
                {
                    continue;
                }


                string editorName =
                    element
                        .Attribute(
                            "EditorName")
                        ?.Value
                    ??
                    string.Empty;


                string type =
                    ReadDirectText(
                        element);


                if (!IsVegetation(
                        type,
                        editorName))
                {
                    continue;
                }


                result.Add(
                    new ArtVegetationObject
                    {
                        Id =
                            id,

                        Type =
                            type,

                        EditorName =
                            editorName
                    });
            }


            return result;
        }


        // =========================================================
        // CLASSIFICATION
        // =========================================================

        private static bool IsVegetation(
            string type,
            string editorName)
        {
            string text =
                (
                    type +
                    " " +
                    editorName
                )
                .Replace(
                    '\\',
                    ' ')
                .Replace(
                    '/',
                    ' ')
                .Replace(
                    '_',
                    ' ')
                .Replace(
                    '-',
                    ' ')
                .ToLowerInvariant();


            string[] tokens =
                text.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);


            foreach (string token
                     in tokens)
            {
                if (token ==
                        "tree" ||
                    token.EndsWith(
                        "tree",
                        StringComparison.Ordinal) ||
                    token.StartsWith(
                        "pinetree",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "bush",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "shrub",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "fern",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "foliage",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "grass",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "flower",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "weed",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "reed",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "sapling",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "stump",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "cactus",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "palm",
                        StringComparison.Ordinal) ||
                    token.Contains(
                        "vine",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }


            return false;
        }


        // =========================================================
        // DIRECT OBJECT TYPE TEXT
        // =========================================================

        private static string ReadDirectText(
            XElement element)
        {
            XText? text =
                element
                    .Nodes()
                    .OfType<XText>()
                    .FirstOrDefault(
                        node =>
                            !string.IsNullOrWhiteSpace(
                                node.Value));


            return text
                ?.Value
                .Trim()
                ??
                string.Empty;
        }


        // =========================================================
        // MODEL
        // =========================================================

        private sealed class ArtVegetationObject
        {
            public int Id
            {
                get;
                init;
            }


            public string Type
            {
                get;
                init;
            } =
                string.Empty;


            public string EditorName
            {
                get;
                init;
            } =
                string.Empty;
        }
    }
}