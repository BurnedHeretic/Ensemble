using Ensemble.Models;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Ensemble.Services
{
    /// <summary>
    /// v36.3 fixes the interpretation of Halo Wars skirmish Positions.
    ///
    /// Stock maps do NOT use Position.Number as a unique player ID:
    ///   1v1: Number 1, Number 2
    ///   2v2: Number 1 x2, Number 2 x2
    ///   3v3: Number 1 x3, Number 2 x3
    ///
    /// Position.Player remains -1 for normal skirmish starts. The <Players>
    /// collection is a separate structure and must contain Player1..PlayerN.
    ///
    /// This partial shares PlayerStartStructureService's proven packed-XMX
    /// structural reader/writer so no XML re-encoding shortcut is required.
    /// </summary>
    internal static partial class PlayerStartStructureService
    {
        public static byte[] SynchronizeSkirmish(
            byte[] xmbData,
            IReadOnlyList<ScenarioPlayerStart> starts,
            int playerCount)
        {
            ArgumentNullException.ThrowIfNull(xmbData);
            ArgumentNullException.ThrowIfNull(starts);

            if (playerCount is not (2 or 4 or 6))
            {
                throw new InvalidDataException(
                    "Halo Wars skirmish maps support 2, 4 or 6 active players.");
            }

            if (starts.Count != playerCount)
            {
                throw new InvalidDataException(
                    $"The editor contains {starts.Count} playable starts but the map is configured for {playerCount} players.");
            }

            int teamSize =
                playerCount / 2;

            PackedDocument doc =
                ReadPackedDocument(
                    xmbData);

            List<StructuralNode> nodes =
                BuildStructuralNodes(
                    doc);

            List<byte> variants =
                doc.Data
                    .AsSpan(
                        checked((int)doc.VariantData.Offset),
                        checked((int)doc.VariantData.Count))
                    .ToArray()
                    .ToList();

            HashSet<int> nodesToRemove =
                new();

            SynchronizePositionNodesV363(
                doc,
                nodes,
                variants,
                starts,
                teamSize,
                nodesToRemove);

            SynchronizePlayerNodesV363(
                doc,
                nodes,
                variants,
                playerCount,
                teamSize,
                nodesToRemove);

            if (nodesToRemove.Count > 0)
            {
                nodes =
                    CompactStructuralNodes(
                        nodes,
                        nodesToRemove);
            }

            byte[] packed =
                BuildPackedXmx(
                    nodes,
                    variants,
                    doc.Layout,
                    doc.BigEndian);

            byte[] rebuilt =
                ReplacePackedData(
                    xmbData,
                    packed);

            VerifySkirmishStructureV363(
                rebuilt,
                playerCount);

            return rebuilt;
        }

        public static bool IsSkirmishStructureSynchronized(
            byte[] xmbData,
            int playerCount)
        {
            if (xmbData == null ||
                playerCount is not (2 or 4 or 6))
            {
                return false;
            }

            try
            {
                VerifySkirmishStructureV363(
                    xmbData,
                    playerCount);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SynchronizePositionNodesV363(
            PackedDocument doc,
            List<StructuralNode> nodes,
            List<byte> variants,
            IReadOnlyList<ScenarioPlayerStart> starts,
            int teamSize,
            HashSet<int> nodesToRemove)
        {
            int positionsNodeIndex =
                FindPositionsNode(
                    doc);

            List<int> directPositionNodes =
                GetDirectPositionChildren(
                    doc,
                    positionsNodeIndex);

            if (directPositionNodes.Count == 0)
            {
                throw new InvalidDataException(
                    "The source scenario contains no <Positions><Position> template.");
            }

            List<int> playable =
                new();

            List<int> preservedSpecial =
                new();

            foreach (int nodeIndex
                     in directPositionNodes)
            {
                int number =
                    TryGetIntegerAttribute(
                        doc.Nodes[nodeIndex],
                        "Number",
                        doc,
                        out int parsedNumber)
                        ? parsedNumber
                        : 0;

                int player =
                    TryGetIntegerAttribute(
                        doc.Nodes[nodeIndex],
                        "Player",
                        doc,
                        out int parsedPlayer)
                        ? parsedPlayer
                        : -1;

                // Spectator / observer positions (normally 7) are real Halo
                // Wars scenario data but are not editable skirmish starts.
                if (number == 7 ||
                    player == 7)
                {
                    preservedSpecial.Add(
                        nodeIndex);

                    continue;
                }

                playable.Add(
                    nodeIndex);
            }

            if (playable.Count == 0)
            {
                throw new InvalidDataException(
                    "The source scenario contains no normal skirmish Position template.");
            }

            List<int> sourceGroup1 =
                playable
                    .Where(
                        index =>
                            TryGetIntegerAttribute(
                                doc.Nodes[index],
                                "Number",
                                doc,
                                out int number) &&
                            number == 1)
                    .ToList();

            List<int> sourceGroup2 =
                playable
                    .Where(
                        index =>
                            TryGetIntegerAttribute(
                                doc.Nodes[index],
                                "Number",
                                doc,
                                out int number) &&
                            number == 2)
                    .ToList();

            int fallbackTemplate =
                playable[0];

            int group1Template =
                sourceGroup1.FirstOrDefault(
                    fallbackTemplate);

            int group2Template =
                sourceGroup2.FirstOrDefault(
                    fallbackTemplate);

            HashSet<int> usedExisting =
                new();

            List<uint> desiredChildren =
                new();

            for (int slot = 0;
                 slot < starts.Count;
                 slot++)
            {
                int group =
                    slot < teamSize
                        ? 1
                        : 2;

                List<int> preferred =
                    group == 1
                        ? sourceGroup1
                        : sourceGroup2;

                int nodeIndex =
                    preferred.FirstOrDefault(
                        index =>
                            !usedExisting.Contains(
                                index));

                if (nodeIndex <= 0 &&
                    !preferred.Contains(0))
                {
                    nodeIndex =
                        -1;
                }

                if (nodeIndex < 0)
                {
                    int template =
                        group == 1
                            ? group1Template
                            : group2Template;

                    uint cloneIndex =
                        CloneStructuralSubtree(
                            nodes,
                            template,
                            checked((uint)positionsNodeIndex),
                            doc.Data,
                            doc.VariantData,
                            variants,
                            doc.BigEndian);

                    nodeIndex =
                        checked((int)cloneIndex);
                }
                else
                {
                    usedExisting.Add(
                        nodeIndex);
                }

                ScenarioPlayerStart start =
                    starts[slot];

                start.Player =
                    -1;

                start.Number =
                    group;

                StructuralNode node =
                    nodes[nodeIndex];

                SetIntegerAttribute(
                    node,
                    "Number",
                    group,
                    variants,
                    doc.BigEndian);

                SetIntegerAttribute(
                    node,
                    "Player",
                    -1,
                    variants,
                    doc.BigEndian);

                SetVectorAttribute(
                    node,
                    "Position",
                    start.Position,
                    variants,
                    doc.BigEndian);

                SetVectorAttribute(
                    node,
                    "Forward",
                    NormaliseForward(
                        start.Forward),
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "UnitStartObject1",
                    -1,
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "UnitStartObject2",
                    -1,
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "UnitStartObject3",
                    -1,
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "UnitStartObject4",
                    -1,
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "RallyStartObject",
                    -1,
                    variants,
                    doc.BigEndian);

                desiredChildren.Add(
                    checked((uint)nodeIndex));
            }

            foreach (int nodeIndex
                     in playable)
            {
                if (!usedExisting.Contains(
                        nodeIndex) &&
                    !desiredChildren.Contains(
                        checked((uint)nodeIndex)))
                {
                    CollectStructuralSubtreeIndices(
                        nodes,
                        nodeIndex,
                        nodesToRemove);
                }
            }

            // Preserve any unusual non-Position direct children as well as the
            // spectator/observer Position node(s).
            HashSet<int> directPositionSet =
                directPositionNodes
                    .ToHashSet();

            List<uint> otherChildren =
                nodes[positionsNodeIndex]
                    .Children
                    .Where(
                        value =>
                            !directPositionSet.Contains(
                                checked((int)value)))
                    .ToList();

            nodes[positionsNodeIndex]
                .Children
                .Clear();

            nodes[positionsNodeIndex]
                .Children
                .AddRange(
                    desiredChildren);

            foreach (int special
                     in preservedSpecial)
            {
                nodes[positionsNodeIndex]
                    .Children
                    .Add(
                        checked((uint)special));
            }

            nodes[positionsNodeIndex]
                .Children
                .AddRange(
                    otherChildren);
        }

        private static void SynchronizePlayerNodesV363(
            PackedDocument doc,
            List<StructuralNode> nodes,
            List<byte> variants,
            int playerCount,
            int teamSize,
            HashSet<int> nodesToRemove)
        {
            int playersNodeIndex =
                FindContainerNodeV363(
                    doc,
                    "Players");

            List<int> directPlayers =
                GetDirectNamedChildrenV363(
                    doc,
                    playersNodeIndex,
                    "Player");

            if (directPlayers.Count == 0)
            {
                throw new InvalidDataException(
                    "The source scenario contains no <Players><Player> template.");
            }

            Dictionary<int, int> playerNodes =
                new();

            List<int> preserved =
                new();

            foreach (int nodeIndex
                     in directPlayers)
            {
                string name =
                    ReadOriginalStringAttributeV363(
                        nodes[nodeIndex],
                        "Name",
                        doc);

                if (TryParsePlayerNameV363(
                        name,
                        out int slot) &&
                    slot >= 1 &&
                    slot <= 6)
                {
                    if (!playerNodes.ContainsKey(
                            slot))
                    {
                        playerNodes[slot] =
                            nodeIndex;
                    }
                    else
                    {
                        CollectStructuralSubtreeIndices(
                            nodes,
                            nodeIndex,
                            nodesToRemove);
                    }

                    continue;
                }

                // Creeps / Flood / neutral scripted players remain untouched.
                preserved.Add(
                    nodeIndex);
            }

            if (playerNodes.Count == 0)
            {
                throw new InvalidDataException(
                    "The source scenario contains no Player1..Player6 template.");
            }

            int genericTemplate =
                playerNodes
                    .OrderBy(pair => pair.Key)
                    .First()
                    .Value;

            int team1Template =
                playerNodes.TryGetValue(
                    1,
                    out int p1)
                    ? p1
                    : genericTemplate;

            int team2Template =
                playerNodes.TryGetValue(
                    2,
                    out int p2)
                    ? p2
                    : genericTemplate;

            HashSet<int> used =
                new();

            List<uint> desired =
                new();

            for (int slot = 1;
                 slot <= playerCount;
                 slot++)
            {
                int nodeIndex;

                if (playerNodes.TryGetValue(
                        slot,
                        out int existing))
                {
                    nodeIndex =
                        existing;

                    used.Add(
                        existing);
                }
                else
                {
                    int template =
                        slot <= teamSize
                            ? team1Template
                            : team2Template;

                    uint cloneIndex =
                        CloneStructuralSubtree(
                            nodes,
                            template,
                            checked((uint)playersNodeIndex),
                            doc.Data,
                            doc.VariantData,
                            variants,
                            doc.BigEndian);

                    nodeIndex =
                        checked((int)cloneIndex);
                }

                StructuralNode node =
                    nodes[nodeIndex];

                SetStringAttributeV363(
                    node,
                    "Name",
                    $"Player{slot}",
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "Color",
                    slot,
                    variants,
                    doc.BigEndian);

                SetIntegerAttributeIfPresent(
                    node,
                    "Team",
                    slot <= teamSize
                        ? 1
                        : 2,
                    variants,
                    doc.BigEndian);

                desired.Add(
                    checked((uint)nodeIndex));
            }

            foreach (KeyValuePair<int, int> pair
                     in playerNodes)
            {
                if (pair.Key > playerCount ||
                    !used.Contains(
                        pair.Value) &&
                    !desired.Contains(
                        checked((uint)pair.Value)))
                {
                    CollectStructuralSubtreeIndices(
                        nodes,
                        pair.Value,
                        nodesToRemove);
                }
            }

            HashSet<int> directPlayerSet =
                directPlayers
                    .ToHashSet();

            List<uint> otherChildren =
                nodes[playersNodeIndex]
                    .Children
                    .Where(
                        value =>
                            !directPlayerSet.Contains(
                                checked((int)value)))
                    .ToList();

            nodes[playersNodeIndex]
                .Children
                .Clear();

            nodes[playersNodeIndex]
                .Children
                .AddRange(
                    desired);

            foreach (int special
                     in preserved)
            {
                nodes[playersNodeIndex]
                    .Children
                    .Add(
                        checked((uint)special));
            }

            nodes[playersNodeIndex]
                .Children
                .AddRange(
                    otherChildren);
        }

        private static int FindContainerNodeV363(
            PackedDocument doc,
            string wantedName)
        {
            List<int> matches =
                new();

            for (int i = 0;
                 i < doc.Nodes.Count;
                 i++)
            {
                string name =
                    DecodeVariant(
                        doc.Nodes[i].NameVariant,
                        doc.Data,
                        doc.VariantData,
                        doc.BigEndian);

                if (name.Equals(
                        wantedName,
                        StringComparison.Ordinal))
                {
                    matches.Add(
                        i);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"The scenario does not contain a unique <{wantedName}> node.");
            }

            return matches[0];
        }

        private static List<int> GetDirectNamedChildrenV363(
            PackedDocument doc,
            int parentIndex,
            string wantedName)
        {
            List<int> result =
                new();

            XmxNode parent =
                doc.Nodes[parentIndex];

            for (uint i = 0;
                 i < parent.Children.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            parent.Children.Offset +
                            (ulong)i * 4));

                uint childValue =
                    ReadUInt32(
                        doc.Data,
                        p,
                        doc.BigEndian);

                if (childValue >=
                    doc.Nodes.Count)
                {
                    continue;
                }

                int childIndex =
                    checked((int)childValue);

                string name =
                    DecodeVariant(
                        doc.Nodes[childIndex].NameVariant,
                        doc.Data,
                        doc.VariantData,
                        doc.BigEndian);

                if (name.Equals(
                        wantedName,
                        StringComparison.Ordinal))
                {
                    result.Add(
                        childIndex);
                }
            }

            return result;
        }

        private static string ReadOriginalStringAttributeV363(
            StructuralNode node,
            string name,
            PackedDocument doc)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        item =>
                            item.Name.Equals(
                                name,
                                StringComparison.Ordinal));

            if (attr == null)
                return string.Empty;

            return DecodeVariant(
                attr.ValueVariant,
                doc.Data,
                doc.VariantData,
                doc.BigEndian);
        }

        private static bool TryParsePlayerNameV363(
            string value,
            out int slot)
        {
            slot =
                0;

            if (!value.StartsWith(
                    "Player",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(
                value["Player".Length..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out slot);
        }

        private static void SetStringAttributeV363(
            StructuralNode node,
            string name,
            string value,
            List<byte> variants,
            bool bigEndian)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        item =>
                            item.Name.Equals(
                                name,
                                StringComparison.Ordinal));

            if (attr == null)
            {
                throw new InvalidDataException(
                    $"Player node contains no '{name}' attribute.");
            }

            uint typeBits =
                attr.ValueVariant >>
                24;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            if (type == 8)
            {
                byte[] bytes =
                    Encoding.ASCII.GetBytes(
                        value);

                AlignByteList(
                    variants,
                    1);

                int offset =
                    variants.Count;

                EnsureVariantPoolOffset(
                    offset);

                variants.AddRange(
                    bytes);

                variants.Add(
                    0);

                attr.ValueVariant =
                    (0x88u << 24) |
                    ((uint)offset &
                     0x00FFFFFFu);

                return;
            }

            if (type == 9)
            {
                AlignByteList(
                    variants,
                    2);

                int offset =
                    variants.Count;

                EnsureVariantPoolOffset(
                    offset);

                Encoding encoding =
                    bigEndian
                        ? Encoding.BigEndianUnicode
                        : Encoding.Unicode;

                variants.AddRange(
                    encoding.GetBytes(
                        value));

                variants.Add(
                    0);

                variants.Add(
                    0);

                attr.ValueVariant =
                    (0x89u << 24) |
                    ((uint)offset &
                     0x00FFFFFFu);

                return;
            }

            throw new InvalidDataException(
                $"Player attribute '{name}' is not stored as an XMX string.");
        }

        private static void VerifySkirmishStructureV363(
            byte[] xmbData,
            int playerCount)
        {
            XDocument xml =
                XDocument.Parse(
                    XmbDocumentService.Read(
                        xmbData));

            XElement root =
                xml.Root
                ?? throw new InvalidDataException(
                    "Scenario XML has no root node.");

            List<XElement> positions =
                root
                    .Element("Positions")
                    ?.Elements("Position")
                    .Where(
                        element =>
                        {
                            int player =
                                ReadXmlIntV363(
                                    element,
                                    "Player",
                                    -1);

                            int number =
                                ReadXmlIntV363(
                                    element,
                                    "Number",
                                    0);

                            return player != 7 &&
                                   number != 7;
                        })
                    .ToList()
                ??
                new List<XElement>();

            if (positions.Count !=
                playerCount)
            {
                throw new InvalidDataException(
                    $"Skirmish structure verification expected {playerCount} playable Position nodes but found {positions.Count}.");
            }

            int teamSize =
                playerCount / 2;

            int team1 =
                positions.Count(
                    element =>
                        ReadXmlIntV363(
                            element,
                            "Number",
                            0) ==
                        1);

            int team2 =
                positions.Count(
                    element =>
                        ReadXmlIntV363(
                            element,
                            "Number",
                            0) ==
                        2);

            if (team1 != teamSize ||
                team2 != teamSize)
            {
                throw new InvalidDataException(
                    $"Skirmish Position groups are invalid. Number=1: {team1}, Number=2: {team2}, expected {teamSize} each.");
            }

            if (positions.Any(
                    element =>
                        ReadXmlIntV363(
                            element,
                            "Player",
                            -999) !=
                        -1))
            {
                throw new InvalidDataException(
                    "Normal Halo Wars skirmish Position.Player values must remain -1.");
            }

            List<XElement> players =
                root
                    .Element("Players")
                    ?.Elements("Player")
                    .Where(
                        element =>
                            TryParsePlayerNameV363(
                                element.Attribute("Name")
                                    ?.Value
                                ??
                                string.Empty,
                                out int slot) &&
                            slot >= 1 &&
                            slot <= 6)
                    .ToList()
                ??
                new List<XElement>();

            if (players.Count !=
                playerCount)
            {
                throw new InvalidDataException(
                    $"Skirmish structure verification expected Player1..Player{playerCount}, but found {players.Count} active PlayerN nodes.");
            }

            HashSet<string> names =
                players
                    .Select(
                        element =>
                            element.Attribute("Name")
                                ?.Value
                            ??
                            string.Empty)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            for (int slot = 1;
                 slot <= playerCount;
                 slot++)
            {
                if (!names.Contains(
                        $"Player{slot}"))
                {
                    throw new InvalidDataException(
                        $"Scenario <Players> is missing Player{slot}.");
                }
            }
        }

        private static int ReadXmlIntV363(
            XElement element,
            string name,
            int fallback)
        {
            return int.TryParse(
                    element.Attribute(name)
                        ?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value)
                ? value
                : fallback;
        }
    }
}
