using Ensemble.Models;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace Ensemble.Services
{
    public static class ScenarioParserService
    {
        public static ScenarioMap Parse(string xmlText)
        {
            if (string.IsNullOrWhiteSpace(xmlText))
            {
                throw new ArgumentException(
                    "Scenario XML cannot be empty.",
                    nameof(xmlText));
            }

            XDocument document =
                XDocument.Parse(xmlText);

            XElement root =
                document.Root
                ?? throw new InvalidOperationException(
                    "Scenario XML has no root element.");

            if (!string.Equals(
                    root.Name.LocalName,
                    "Scenario",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected Scenario root but found {root.Name}.");
            }

            ScenarioMap map =
                new ScenarioMap
                {
                    Terrain =
                        root.Element("Terrain")?.Value.Trim()
                        ?? string.Empty,

                    MinX =
                        ReadElementFloat(
                            root,
                            "SimBoundsMinX",
                            0),

                    MinZ =
                        ReadElementFloat(
                            root,
                            "SimBoundsMinZ",
                            0),

                    MaxX =
                        ReadElementFloat(
                            root,
                            "SimBoundsMaxX",
                            1024),

                    MaxZ =
                        ReadElementFloat(
                            root,
                            "SimBoundsMaxZ",
                            1024)
                };

            map.Name =
                string.IsNullOrWhiteSpace(map.Terrain)
                    ? "Scenario"
                    : map.Terrain;

            ParsePlayerStarts(
                root,
                map);

            ParseObjects(
                root,
                map);

            ParseDesignSpheres(
                root,
                map);

            ParseDesignPaths(
                root,
                map);

            return map;
        }

        private static void ParsePlayerStarts(
            XElement root,
            ScenarioMap map)
        {
            XElement? positions =
                root.Element("Positions");

            if (positions == null)
                return;

            foreach (XElement element
                     in positions.Elements("Position"))
            {
                ScenarioPlayerStart start =
                    new ScenarioPlayerStart
                    {
                        Player =
                            ReadIntAttribute(
                                element,
                                "Player"),

                        Number =
                            ReadIntAttribute(
                                element,
                                "Number"),

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

                        DefaultCamera =
                            ReadBoolAttribute(
                                element,
                                "DefaultCamera"),

                        CameraYaw =
                            ReadFloatAttribute(
                                element,
                                "CameraYaw"),

                        CameraPitch =
                            ReadFloatAttribute(
                                element,
                                "CameraPitch"),

                        CameraZoom =
                            ReadFloatAttribute(
                                element,
                                "CameraZoom")
                    };

                map.PlayerStarts.Add(
                    start);
            }
        }

        private static void ParseObjects(
            XElement root,
            ScenarioMap map)
        {
            XElement? objects =
                root.Element("Objects");

            if (objects == null)
                return;

            foreach (XElement element
                     in objects.Elements("Object"))
            {
                ScenarioObject obj =
                    new ScenarioObject
                    {
                        Id =
                            ReadIntAttribute(
                                element,
                                "ID"),

                        IsSquad =
                            ReadBoolAttribute(
                                element,
                                "IsSquad"),

                        Player =
                            ReadIntAttribute(
                                element,
                                "Player"),

                        TintValue =
                            ReadFloatAttribute(
                                element,
                                "TintValue"),

                        EditorName =
                            ReadAttribute(
                                element,
                                "EditorName"),

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
                                "VisualVariationIndex"),

                        Type =
                            ReadDirectText(
                                element)
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

                map.Objects.Add(
                    obj);
            }
        }

        private static void ParseDesignSpheres(
            XElement root,
            ScenarioMap map)
        {
            XElement? spheres =
                root
                    .Element("DesignObjects")
                    ?.Element("Spheres");

            if (spheres == null)
                return;

            foreach (XElement element
                     in spheres.Elements("Sphere"))
            {
                ScenarioSphere sphere =
                    new ScenarioSphere
                    {
                        Id =
                            ReadIntAttribute(
                                element,
                                "ID"),

                        Name =
                            ReadAttribute(
                                element,
                                "Name"),

                        Position =
                            ParseVector3(
                                ReadAttribute(
                                    element,
                                    "Position")),

                        Radius =
                            ReadFloatAttribute(
                                element,
                                "Radius"),

                        Type =
                            element
                                .Element("Data")
                                ?.Element("Type")
                                ?.Value
                                .Trim()
                            ?? string.Empty
                    };

                map.Spheres.Add(
                    sphere);
            }
        }

        private static void ParseDesignPaths(
            XElement root,
            ScenarioMap map)
        {
            XElement? lines =
                root
                    .Element("DesignObjects")
                    ?.Element("Lines");

            if (lines == null)
                return;

            foreach (XElement element
                     in lines.Elements("Lines"))
            {
                ScenarioPath path =
                    new ScenarioPath
                    {
                        Id =
                            ReadIntAttribute(
                                element,
                                "ID"),

                        Name =
                            ReadAttribute(
                                element,
                                "Name"),

                        Position =
                            ParseVector3(
                                ReadAttribute(
                                    element,
                                    "Position")),

                        Type =
                            element
                                .Element("Data")
                                ?.Element("Type")
                                ?.Value
                                .Trim()
                            ?? string.Empty
                    };

                string points =
                    element
                        .Element("Points")
                        ?.Value
                        .Trim()
                    ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(
                    points))
                {
                    foreach (string point
                             in points.Split(
                                 '|',
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        path.Points.Add(
                            ParseVector3(
                                point));
                    }
                }

                map.Paths.Add(
                    path);
            }
        }

        private static string ReadDirectText(
            XElement element)
        {
            XText? text =
                element
                    .Nodes()
                    .OfType<XText>()
                    .FirstOrDefault(
                        t =>
                            !string.IsNullOrWhiteSpace(
                                t.Value));

            return
                text?.Value.Trim()
                ?? string.Empty;
        }

        private static string ReadAttribute(
            XElement element,
            string name)
        {
            return
                element.Attribute(name)?.Value
                ?? string.Empty;
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

        private static float ReadFloatAttribute(
            XElement element,
            string name)
        {
            string value =
                ReadAttribute(
                    element,
                    name);

            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result)
                    ? result
                    : 0;
        }

        private static bool ReadBoolAttribute(
            XElement element,
            string name)
        {
            string value =
                ReadAttribute(
                    element,
                    name);

            return bool.TryParse(
                value,
                out bool result)
                && result;
        }

        private static float ReadElementFloat(
            XElement parent,
            string name,
            float fallback)
        {
            string? value =
                parent.Element(name)?.Value;

            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result)
                    ? result
                    : fallback;
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

            if (pieces.Length != 3)
            {
                throw new FormatException(
                    $"Invalid Halo Wars vector: {value}");
            }

            return new Vector3(
                ParseFloat(pieces[0]),
                ParseFloat(pieces[1]),
                ParseFloat(pieces[2]));
        }

        private static float ParseFloat(
            string value)
        {
            return float.Parse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }
    }
}