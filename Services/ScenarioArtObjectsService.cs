using Ensemble.Models;
using System.Globalization;
using System.IO;
using System.Numerics;
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

        internal sealed class TransformWriteResult
        {
            public byte[] ModifiedXmb
            {
                get;
                init;
            } =
                Array.Empty<byte>();


            public int ChangedObjectCount
            {
                get;
                init;
            }
        }


        // =========================================================
        // READ ALL ART OBJECTS
        // =========================================================

        public static List<ScenarioArtObject> ReadArtObjects(
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


            return ParseArtObjects(
                document);
        }


        // =========================================================
        // VEGETATION COUNT
        // =========================================================

        public static int CountVegetationObjects(
            byte[] sc2XmbData)
        {
            return ReadArtObjects(
                    sc2XmbData)
                .Count(
                    obj =>
                        IsVegetation(
                            obj.Type,
                            obj.EditorName));
        }


        // =========================================================
        // REMOVE VEGETATION
        // =========================================================

        public static VegetationRemovalResult RemoveAllVegetation(
            byte[] originalSc2XmbData)
        {
            ArgumentNullException.ThrowIfNull(
                originalSc2XmbData);


            List<ScenarioArtObject> vegetation =
                ReadArtObjects(
                    originalSc2XmbData)
                    .Where(
                        obj =>
                            IsVegetation(
                                obj.Type,
                                obj.EditorName))
                    .ToList();


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


            // Reuse Ensemble's proven structural XMB deletion
            // path. Only the matching SC2 object IDs are removed.

            ScenarioMap edit =
                new ScenarioMap();


            foreach (ScenarioArtObject obj
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

            List<ScenarioArtObject> remaining =
                ReadArtObjects(
                    rebuilt)
                    .Where(
                        obj =>
                            IsVegetation(
                                obj.Type,
                                obj.EditorName))
                    .ToList();


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
                            obj =>
                                obj.Type)
                        .Where(
                            type =>
                                !string.IsNullOrWhiteSpace(
                                    type))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            type =>
                                type,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
            };
        }

        // =========================================================
        // WRITE ART OBJECT TRANSFORMS
        // =========================================================

        public static TransformWriteResult ApplyTransforms(
            byte[] sourceSc2XmbData,
            IReadOnlyCollection<ScenarioArtObject> currentArtObjects)
        {
            ArgumentNullException.ThrowIfNull(
                sourceSc2XmbData);

            ArgumentNullException.ThrowIfNull(
                currentArtObjects);


            List<ScenarioArtObject> sourceObjects =
                ReadArtObjects(
                    sourceSc2XmbData);


            Dictionary<int, ScenarioArtObject> sourceById =
                sourceObjects
                    .ToDictionary(
                        obj =>
                            obj.Id);


            List<ScenarioArtObject> changed =
                new();


            foreach (ScenarioArtObject current
                     in currentArtObjects)
            {
                // The pending SC2 may already have had an object removed
                // by Remove All Vegetation.
                //
                // Do not accidentally recreate deleted ArtObjects.

                if (!sourceById.TryGetValue(
                        current.Id,
                        out ScenarioArtObject? original))
                {
                    continue;
                }


                if (!TransformEquals(
                        original,
                        current))
                {
                    changed.Add(
                        current);
                }
            }


            if (changed.Count ==
                0)
            {
                return new TransformWriteResult
                {
                    ModifiedXmb =
                        sourceSc2XmbData
                            .ToArray(),

                    ChangedObjectCount =
                        0
                };
            }


            byte[] rebuilt =
                XmbDocumentService
                    .WriteArtObjectTransforms(
                        sourceSc2XmbData,
                        changed);


            // =========================================================
            // ROUND-TRIP VERIFICATION
            // =========================================================

            Dictionary<int, ScenarioArtObject> verification =
                ReadArtObjects(
                    rebuilt)
                    .ToDictionary(
                        obj =>
                            obj.Id);


            foreach (ScenarioArtObject expected
                     in changed)
            {
                if (!verification.TryGetValue(
                        expected.Id,
                        out ScenarioArtObject? actual))
                {
                    throw new InvalidDataException(
                        "SC2 transform verification lost ArtObject " +
                        $"ID {expected.Id}.");
                }


                if (!VectorNearlyEqual(
                        expected.Position,
                        actual.Position))
                {
                    throw new InvalidDataException(
                        "SC2 ArtObject Position failed verification.\n\n" +
                        $"ID: {expected.Id}");
                }


                if (!VectorNearlyEqual(
                        expected.Forward,
                        actual.Forward))
                {
                    throw new InvalidDataException(
                        "SC2 ArtObject Forward failed verification.\n\n" +
                        $"ID: {expected.Id}");
                }


                if (!VectorNearlyEqual(
                        expected.Right,
                        actual.Right))
                {
                    throw new InvalidDataException(
                        "SC2 ArtObject Right failed verification.\n\n" +
                        $"ID: {expected.Id}");
                }
            }


            return new TransformWriteResult
            {
                ModifiedXmb =
                    rebuilt,

                ChangedObjectCount =
                    changed.Count
            };
        }


        // =========================================================
        // ART OBJECT PARSER
        // =========================================================

        private static List<ScenarioArtObject> ParseArtObjects(
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


            List<ScenarioArtObject> result =
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
                int id =
                    ReadIntAttribute(
                        element,
                        "ID");


                string editorName =
                    ReadAttribute(
                        element,
                        "EditorName");


                ScenarioArtObject obj =
                    new ScenarioArtObject
                    {
                        Id =
                            id,

                        SourceObjectId =
                            id,

                        EditorName =
                            editorName,

                        OriginalEditorName =
                            editorName,

                        Type =
                            ReadDirectText(
                                element),

                        Position =
                            ParseVector3(
                                ReadAttribute(
                                    element,
                                    "Position")),

                        Forward =
                            ParseVector3(
                                ReadAttribute(
                                    element,
                                    "Forward")),

                        Right =
                            ParseVector3(
                                ReadAttribute(
                                    element,
                                    "Right")),

                        Group =
                            ReadIntAttribute(
                                element,
                                "Group"),

                        VisualVariationIndex =
                            ReadIntAttribute(
                                element,
                                "VisualVariationIndex")
                    };


                foreach (XElement flag
                         in element.Elements("Flag"))
                {
                    string value =
                        flag.Value.Trim();


                    if (!string.IsNullOrWhiteSpace(
                            value))
                    {
                        obj.Flags.Add(
                            value);
                    }
                }


                result.Add(
                    obj);
            }


            return result;
        }


        // =========================================================
        // VEGETATION CLASSIFICATION
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
        // XML HELPERS
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


        private static string ReadAttribute(
            XElement element,
            string name)
        {
            return element
                .Attribute(
                    name)
                ?.Value
                ??
                string.Empty;
        }


        private static int ReadIntAttribute(
            XElement element,
            string name)
        {
            string value =
                ReadAttribute(
                    element,
                    name);


            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                    ? result
                    : 0;
        }


        private static Vector3 ParseVector3(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return Vector3.Zero;
            }


            string[] pieces =
                value.Split(',');


            if (pieces.Length !=
                3)
            {
                throw new FormatException(
                    $"Invalid Halo Wars vector: {value}");
            }


            return new Vector3(
                ParseFloat(
                    pieces[0]),

                ParseFloat(
                    pieces[1]),

                ParseFloat(
                    pieces[2]));
        }


        private static float ParseFloat(
            string value)
        {
            return float.Parse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static bool TransformEquals(
            ScenarioArtObject a,
            ScenarioArtObject b)
        {
            return
                VectorNearlyEqual(
                    a.Position,
                    b.Position)
                &&
                VectorNearlyEqual(
                    a.Forward,
                    b.Forward)
                &&
                VectorNearlyEqual(
                    a.Right,
                    b.Right);
        }


        private static bool VectorNearlyEqual(
            System.Numerics.Vector3 a,
            System.Numerics.Vector3 b)
        {
            return
                System.Numerics.Vector3
                    .DistanceSquared(
                        a,
                        b) <
                0.000001f;
        }
    }
}