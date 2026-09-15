using Ensemble.Models;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Ensemble.Services
{
    /// <summary>
    /// v33 structural writer for Scenario/Positions/Position entries.
    ///
    /// XmbDocumentService can update values on player starts that already
    /// exist, but stock 2P/4P maps do not contain enough Position nodes when
    /// a creator raises the custom map to 4P/6P.  This service safely clones
    /// and removes complete Position subtrees while preserving unknown Halo
    /// Wars attributes, then writes the selected player/start transforms.
    /// </summary>
    internal static class PlayerStartStructureService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const uint XmbEcfId =
            0xE43ABC00;

        private const ulong XmxPackedDataChunkId =
            0x00000000A9C96500UL;

        private const uint XmxSignature =
            0x71439800;

        private const ushort DeflateStreamResourceFlag =
            0x0004;

        private const int EcfHeaderSize =
            32;

        private const int EcfChunkHeaderSize =
            24;

        private const int XmxAttributeSize =
            8;

        public static byte[] Synchronize(
            byte[] xmbData,
            IReadOnlyCollection<ScenarioPlayerStart> starts)
        {
            ArgumentNullException.ThrowIfNull(
                xmbData);

            ArgumentNullException.ThrowIfNull(
                starts);

            if (starts.Count is not
                (2 or 4 or 6))
            {
                throw new InvalidDataException(
                    "Halo Wars custom maps must contain exactly 2, 4 or 6 player starts.");
            }

            List<ScenarioPlayerStart> orderedStarts =
                starts
                    .OrderBy(
                        start =>
                            start.Number)
                    .ToList();

            if (orderedStarts.Any(
                    start =>
                        start.Number < 1 ||
                        start.Number > 6) ||
                orderedStarts
                    .Select(
                        start =>
                            start.Number)
                    .Distinct()
                    .Count() !=
                orderedStarts.Count)
            {
                throw new InvalidDataException(
                    "Player start numbers must be unique values from 1 to 6.");
            }

            PackedDocument doc =
                ReadPackedDocument(
                    xmbData);

            List<StructuralNode> nodes =
                BuildStructuralNodes(
                    doc);

            List<byte> variants =
                doc.Data
                    .AsSpan(
                        checked(
                            (int)doc.VariantData.Offset),
                        checked(
                            (int)doc.VariantData.Count))
                    .ToArray()
                    .ToList();

            int positionsNodeIndex =
                FindPositionsNode(
                    doc);

            List<int> originalPositionNodes =
                GetDirectPositionChildren(
                    doc,
                    positionsNodeIndex);

            if (originalPositionNodes.Count ==
                0)
            {
                throw new InvalidDataException(
                    "The source scenario has no <Positions><Position> template. " +
                    "Ensemble needs at least one stock player-start entry to create additional slots safely.");
            }

            Dictionary<int, int> firstNodeByNumber =
                new();

            HashSet<int> nodesToRemove =
                new();

            HashSet<int> wantedNumbers =
                orderedStarts
                    .Select(
                        start =>
                            start.Number)
                    .ToHashSet();

            foreach (int nodeIndex
                     in originalPositionNodes)
            {
                if (!TryGetIntegerAttribute(
                        doc.Nodes[nodeIndex],
                        "Number",
                        doc,
                        out int number))
                {
                    CollectStructuralSubtreeIndices(
                        nodes,
                        nodeIndex,
                        nodesToRemove);

                    continue;
                }

                if (!wantedNumbers.Contains(
                        number))
                {
                    CollectStructuralSubtreeIndices(
                        nodes,
                        nodeIndex,
                        nodesToRemove);

                    continue;
                }

                if (!firstNodeByNumber.TryAdd(
                        number,
                        nodeIndex))
                {
                    // Normalise duplicate/grouped Position entries into one
                    // explicit slot per custom-map player number.
                    CollectStructuralSubtreeIndices(
                        nodes,
                        nodeIndex,
                        nodesToRemove);
                }
            }

            int templateNodeIndex =
                originalPositionNodes[0];

            foreach (ScenarioPlayerStart start
                     in orderedStarts)
            {
                int nodeIndex;

                if (!firstNodeByNumber.TryGetValue(
                        start.Number,
                        out nodeIndex))
                {
                    uint cloneIndex =
                        CloneStructuralSubtree(
                            nodes,
                            templateNodeIndex,
                            checked(
                                (uint)positionsNodeIndex),
                            doc.Data,
                            doc.VariantData,
                            variants,
                            doc.BigEndian);

                    nodes[positionsNodeIndex]
                        .Children
                        .Add(
                            cloneIndex);

                    nodeIndex =
                        checked(
                            (int)cloneIndex);

                    firstNodeByNumber[
                        start.Number] =
                            nodeIndex;

                    // A cloned stock position can reference donor starting
                    // units/rally objects.  New player slots must not inherit
                    // those IDs from P1/P2.
                    SetIntegerAttributeIfPresent(
                        nodes[nodeIndex],
                        "UnitStartObject1",
                        -1,
                        variants,
                        doc.BigEndian);

                    SetIntegerAttributeIfPresent(
                        nodes[nodeIndex],
                        "UnitStartObject2",
                        -1,
                        variants,
                        doc.BigEndian);

                    SetIntegerAttributeIfPresent(
                        nodes[nodeIndex],
                        "UnitStartObject3",
                        -1,
                        variants,
                        doc.BigEndian);

                    SetIntegerAttributeIfPresent(
                        nodes[nodeIndex],
                        "UnitStartObject4",
                        -1,
                        variants,
                        doc.BigEndian);

                    SetIntegerAttributeIfPresent(
                        nodes[nodeIndex],
                        "RallyStartObject",
                        -1,
                        variants,
                        doc.BigEndian);
                }

                WritePlayerStartNode(
                    nodes[nodeIndex],
                    start,
                    variants,
                    doc.BigEndian);
            }

            if (nodesToRemove.Count >
                0)
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

            Verify(
                rebuilt,
                orderedStarts);

            return rebuilt;
        }

        private static void WritePlayerStartNode(
            StructuralNode node,
            ScenarioPlayerStart start,
            List<byte> variants,
            bool bigEndian)
        {
            SetIntegerAttribute(
                node,
                "Number",
                start.Number,
                variants,
                bigEndian);

            SetIntegerAttribute(
                node,
                "Player",
                start.Player,
                variants,
                bigEndian);

            SetVectorAttribute(
                node,
                "Position",
                start.Position,
                variants,
                bigEndian);

            SetVectorAttribute(
                node,
                "Forward",
                NormaliseForward(
                    start.Forward),
                variants,
                bigEndian);
        }

        private static Vector3 NormaliseForward(
            Vector3 value)
        {
            Vector3 horizontal =
                new Vector3(
                    value.X,
                    0,
                    value.Z);

            if (!float.IsFinite(
                    horizontal.X) ||
                !float.IsFinite(
                    horizontal.Z) ||
                horizontal.LengthSquared() <
                    0.000001f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(
                horizontal);
        }

        private static void Verify(
            byte[] xmbData,
            IReadOnlyCollection<ScenarioPlayerStart> expected)
        {
            XDocument xml =
                XDocument.Parse(
                    XmbDocumentService.Read(
                        xmbData));

            List<XElement> positions =
                xml
                    .Descendants(
                        "Positions")
                    .SelectMany(
                        element =>
                            element.Elements(
                                "Position"))
                    .ToList();

            if (positions.Count !=
                expected.Count)
            {
                throw new InvalidDataException(
                    $"Player-start XMB verification failed. Expected {expected.Count} positions, found {positions.Count}.");
            }

            foreach (ScenarioPlayerStart start
                     in expected)
            {
                XElement? node =
                    positions
                        .FirstOrDefault(
                            element =>
                                int.TryParse(
                                    element.Attribute(
                                        "Number")
                                        ?.Value,
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out int number) &&
                                number ==
                                    start.Number);

                if (node ==
                    null)
                {
                    throw new InvalidDataException(
                        $"Player-start XMB verification lost Player {start.Number}.");
                }

                if (!int.TryParse(
                        node.Attribute(
                            "Player")
                            ?.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int player) ||
                    player !=
                        start.Player)
                {
                    throw new InvalidDataException(
                        $"Player-start XMB verification failed for Player {start.Number} ownership.");
                }
            }
        }

        private static int FindPositionsNode(
            PackedDocument doc)
        {
            List<int> candidates =
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

                if (name ==
                    "Positions")
                {
                    candidates.Add(
                        i);
                }
            }

            foreach (int candidate
                     in candidates)
            {
                XmxNode parent =
                    doc.Nodes[candidate];

                for (uint i = 0;
                     i < parent.Children.Count;
                     i++)
                {
                    int p =
                        checked(
                            (int)(
                                parent.Children.Offset +
                                (ulong)i *
                                4));

                    uint childIndex =
                        ReadUInt32(
                            doc.Data,
                            p,
                            doc.BigEndian);

                    if (childIndex >=
                        doc.Nodes.Count)
                    {
                        continue;
                    }

                    string childName =
                        DecodeVariant(
                            doc.Nodes[
                                checked(
                                    (int)childIndex)]
                                .NameVariant,
                            doc.Data,
                            doc.VariantData,
                            doc.BigEndian);

                    if (childName ==
                        "Position")
                    {
                        return candidate;
                    }
                }
            }

            if (candidates.Count ==
                1)
            {
                return candidates[0];
            }

            throw new InvalidDataException(
                "The scenario does not contain a unique <Positions> node.");
        }

        private static List<int> GetDirectPositionChildren(
            PackedDocument doc,
            int positionsNodeIndex)
        {
            List<int> result =
                new();

            XmxNode positions =
                doc.Nodes[
                    positionsNodeIndex];

            for (uint i = 0;
                 i < positions.Children.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            positions.Children.Offset +
                            (ulong)i *
                            4));

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
                    checked(
                        (int)childValue);

                string name =
                    DecodeVariant(
                        doc.Nodes[childIndex]
                            .NameVariant,
                        doc.Data,
                        doc.VariantData,
                        doc.BigEndian);

                if (name ==
                    "Position")
                {
                    result.Add(
                        childIndex);
                }
            }

            return result;
        }

        private static byte[] ReplacePackedData(
            byte[] originalXmb,
            byte[] packedData)
        {
            byte[] compressed =
                EraCompressionService
                    .CompressDeflateStream(
                        packedData);

            byte[] rebuilt =
                EcfFileService
                    .ReplaceChunk(
                        originalXmb,
                        XmxPackedDataChunkId,
                        compressed);

            _ = XmbDocumentService.Read(
                rebuilt);

            return rebuilt;
        }

        private static PackedDocument ReadPackedDocument(
            byte[] xmbData)
        {
            byte[] packed =
                ExtractPackedXmxData(
                    xmbData);

            bool bigEndian =
                DetermineEndianness(
                    packed);

            PackedLayout layout =
                DetectPackedLayout(
                    packed,
                    bigEndian);

            PackedArray nodes =
                ReadPackedArray(
                    packed,
                    layout.NodesArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            PackedArray variants =
                ReadPackedArray(
                    packed,
                    layout.VariantArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            List<XmxNode> parsed =
                new(
                    checked(
                        (int)nodes.Count));

            for (uint i = 0;
                 i < nodes.Count;
                 i++)
            {
                int offset =
                    checked(
                        (int)(
                            nodes.Offset +
                            (ulong)i *
                            (ulong)layout.NodeSize));

                parsed.Add(
                    ParseNode(
                        packed,
                        offset,
                        layout,
                        bigEndian));
            }

            return new PackedDocument
            {
                Data =
                    packed,

                BigEndian =
                    bigEndian,

                Layout =
                    layout,

                NodesArray =
                    nodes,

                VariantData =
                    variants,

                Nodes =
                    parsed
            };
        }

        private static List<StructuralNode> BuildStructuralNodes(
            PackedDocument doc)
        {
            List<StructuralNode> result =
                new(
                    doc.Nodes.Count);

            foreach (XmxNode source
                     in doc.Nodes)
            {
                StructuralNode node =
                    new()
                    {
                        Parent =
                            source.Parent,

                        NameVariant =
                            source.NameVariant,

                        TextVariant =
                            source.TextVariant
                    };

                for (uint a = 0;
                     a < source.Attributes.Count;
                     a++)
                {
                    int p =
                        checked(
                            (int)(
                                source.Attributes.Offset +
                                (ulong)a *
                                XmxAttributeSize));

                    uint nameVariant =
                        ReadUInt32(
                            doc.Data,
                            p,
                            doc.BigEndian);

                    uint valueVariant =
                        ReadUInt32(
                            doc.Data,
                            p + 4,
                            doc.BigEndian);

                    node.Attributes.Add(
                        new StructuralAttribute
                        {
                            Name =
                                DecodeVariant(
                                    nameVariant,
                                    doc.Data,
                                    doc.VariantData,
                                    doc.BigEndian),

                            NameVariant =
                                nameVariant,

                            ValueVariant =
                                valueVariant
                        });
                }

                for (uint c = 0;
                     c < source.Children.Count;
                     c++)
                {
                    int p =
                        checked(
                            (int)(
                                source.Children.Offset +
                                (ulong)c *
                                4));

                    node.Children.Add(
                        ReadUInt32(
                            doc.Data,
                            p,
                            doc.BigEndian));
                }

                result.Add(
                    node);
            }

            return result;
        }

        private static bool TryGetIntegerAttribute(
            XmxNode node,
            string name,
            PackedDocument doc,
            out int value)
        {
            value =
                0;

            for (uint a = 0;
                 a < node.Attributes.Count;
                 a++)
            {
                int p =
                    checked(
                        (int)(
                            node.Attributes.Offset +
                            (ulong)a *
                            XmxAttributeSize));

                uint nameVariant =
                    ReadUInt32(
                        doc.Data,
                        p,
                        doc.BigEndian);

                string attributeName =
                    DecodeVariant(
                        nameVariant,
                        doc.Data,
                        doc.VariantData,
                        doc.BigEndian);

                if (attributeName !=
                    name)
                {
                    continue;
                }

                uint valueVariant =
                    ReadUInt32(
                        doc.Data,
                        p + 4,
                        doc.BigEndian);

                string text =
                    DecodeVariant(
                        valueVariant,
                        doc.Data,
                        doc.VariantData,
                        doc.BigEndian);

                return int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return false;
        }

        private static uint CloneStructuralSubtree(
            List<StructuralNode> nodes,
            int sourceIndex,
            uint newParent,
            byte[] originalData,
            PackedArray originalVariantData,
            List<byte> variants,
            bool bigEndian)
        {
            StructuralNode source =
                nodes[sourceIndex];

            StructuralNode clone =
                new()
                {
                    Parent =
                        newParent,

                    NameVariant =
                        CloneVariantPayload(
                            source.NameVariant,
                            originalData,
                            originalVariantData,
                            variants,
                            bigEndian),

                    TextVariant =
                        CloneVariantPayload(
                            source.TextVariant,
                            originalData,
                            originalVariantData,
                            variants,
                            bigEndian)
                };

            foreach (StructuralAttribute attr
                     in source.Attributes)
            {
                clone.Attributes.Add(
                    new StructuralAttribute
                    {
                        Name =
                            attr.Name,

                        NameVariant =
                            CloneVariantPayload(
                                attr.NameVariant,
                                originalData,
                                originalVariantData,
                                variants,
                                bigEndian),

                        ValueVariant =
                            CloneVariantPayload(
                                attr.ValueVariant,
                                originalData,
                                originalVariantData,
                                variants,
                                bigEndian)
                    });
            }

            uint cloneIndex =
                checked(
                    (uint)nodes.Count);

            nodes.Add(
                clone);

            // Children are cloned recursively.  Position currently has no
            // meaningful children in stock skirmish scenarios, but preserving
            // the complete subtree keeps this compatible with unusual maps.
            foreach (uint childIndex
                     in source.Children.ToArray())
            {
                uint clonedChild =
                    CloneStructuralSubtree(
                        nodes,
                        checked(
                            (int)childIndex),
                        cloneIndex,
                        originalData,
                        originalVariantData,
                        variants,
                        bigEndian);

                clone.Children.Add(
                    clonedChild);
            }

            return cloneIndex;
        }

        private static uint CloneVariantPayload(
            uint variant,
            byte[] originalData,
            PackedArray originalVariantData,
            List<byte> variants,
            bool bigEndian)
        {
            uint typeBits =
                variant >>
                24;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            bool isOffset =
                (typeBits &
                 0x80) !=
                0;

            if (!isOffset)
            {
                return variant;
            }

            uint relative =
                variant &
                0x00FFFFFFu;

            int sourceOffset =
                GetVariantOffset(
                    originalVariantData,
                    relative,
                    1);

            int length;
            int alignment;

            switch (type)
            {
                case 2:
                case 4:
                    length =
                        4;
                    alignment =
                        4;
                    break;

                case 6:
                    length =
                        8;
                    alignment =
                        8;
                    break;

                case 8:
                    {
                        int end =
                            sourceOffset;

                        int limit =
                            GetVariantDataEnd(
                                originalVariantData);

                        while (end <
                                   limit &&
                               originalData[end] !=
                                   0)
                        {
                            end++;
                        }

                        if (end >=
                            limit)
                        {
                            throw new InvalidDataException(
                                "Unterminated XMX ANSI string while cloning player start.");
                        }

                        length =
                            end -
                            sourceOffset +
                            1;

                        alignment =
                            1;
                        break;
                    }

                case 9:
                    {
                        int end =
                            sourceOffset;

                        int limit =
                            GetVariantDataEnd(
                                originalVariantData);

                        bool terminated =
                            false;

                        while (end + 1 <
                               limit)
                        {
                            ushort value =
                                ReadUInt16(
                                    originalData,
                                    end,
                                    bigEndian);

                            end +=
                                2;

                            if (value ==
                                0)
                            {
                                terminated =
                                    true;
                                break;
                            }
                        }

                        if (!terminated)
                        {
                            throw new InvalidDataException(
                                "Unterminated XMX Unicode string while cloning player start.");
                        }

                        length =
                            end -
                            sourceOffset;

                        alignment =
                            2;
                        break;
                    }

                case 10:
                    {
                        int vectorSize =
                            1 +
                            (int)(
                                (typeBits &
                                 0x30) >>
                                4);

                        length =
                            vectorSize *
                            4;

                        alignment =
                            4;
                        break;
                    }

                default:
                    throw new InvalidDataException(
                        $"Cannot clone offset XMX variant type {type} for a player start.");
            }

            AlignByteList(
                variants,
                alignment);

            int newOffset =
                variants.Count;

            EnsureVariantPoolOffset(
                newOffset);

            for (int i = 0;
                 i < length;
                 i++)
            {
                variants.Add(
                    originalData[
                        sourceOffset +
                        i]);
            }

            return
                (variant &
                 0xFF000000u)
                |
                ((uint)newOffset &
                 0x00FFFFFFu);
        }

        private static void SetIntegerAttribute(
            StructuralNode node,
            string name,
            int value,
            List<byte> variants,
            bool bigEndian)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        item =>
                            item.Name ==
                            name);

            if (attr ==
                null)
            {
                throw new InvalidDataException(
                    $"Player Position contains no '{name}' attribute.");
            }

            SetIntegerVariant(
                attr,
                value,
                variants,
                bigEndian);
        }

        private static void SetIntegerAttributeIfPresent(
            StructuralNode node,
            string name,
            int value,
            List<byte> variants,
            bool bigEndian)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        item =>
                            item.Name ==
                            name);

            if (attr ==
                null)
            {
                return;
            }

            SetIntegerVariant(
                attr,
                value,
                variants,
                bigEndian);
        }

        private static void SetIntegerVariant(
            StructuralAttribute attr,
            int value,
            List<byte> variants,
            bool bigEndian)
        {
            uint variant =
                attr.ValueVariant;

            uint typeBits =
                variant >>
                24;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            bool isOffset =
                (typeBits &
                 0x80) !=
                0;

            bool isUnsigned =
                (typeBits &
                 0x40) !=
                0;

            if (type ==
                    3 &&
                !isOffset)
            {
                if (isUnsigned)
                {
                    if (value < 0 ||
                        value >
                            0x00FFFFFF)
                    {
                        throw new InvalidDataException(
                            $"Integer value {value} does not fit XMX UInt24.");
                    }
                }
                else if (value <
                             -8388608 ||
                         value >
                             8388607)
                {
                    throw new InvalidDataException(
                        $"Integer value {value} does not fit XMX Int24.");
                }

                attr.ValueVariant =
                    (variant &
                     0xFF000000u)
                    |
                    (unchecked(
                        (uint)value) &
                     0x00FFFFFFu);

                return;
            }

            if (type ==
                    4 &&
                isOffset)
            {
                int offset =
                    checked(
                        (int)(
                            variant &
                            0x00FFFFFFu));

                if (offset >
                    variants.Count -
                    4)
                {
                    throw new InvalidDataException(
                        "XMX Int32 points outside the variant pool.");
                }

                Span<byte> temp =
                    stackalloc byte[4];

                if (bigEndian)
                {
                    BinaryPrimitives
                        .WriteInt32BigEndian(
                            temp,
                            value);
                }
                else
                {
                    BinaryPrimitives
                        .WriteInt32LittleEndian(
                            temp,
                            value);
                }

                for (int i = 0;
                     i < 4;
                     i++)
                {
                    variants[
                        offset +
                        i] =
                            temp[i];
                }

                return;
            }

            throw new InvalidDataException(
                $"Unsupported XMX integer variant type {type} on player Position.");
        }

        private static void SetVectorAttribute(
            StructuralNode node,
            string name,
            Vector3 value,
            List<byte> variants,
            bool bigEndian)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        item =>
                            item.Name ==
                            name);

            if (attr ==
                null)
            {
                throw new InvalidDataException(
                    $"Player Position contains no '{name}' attribute.");
            }

            uint typeBits =
                attr.ValueVariant >>
                24;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            bool isOffset =
                (typeBits &
                 0x80) !=
                0;

            int vectorSize =
                1 +
                (int)(
                    (typeBits &
                     0x30) >>
                    4);

            if (type !=
                    10 ||
                !isOffset ||
                vectorSize <
                    3)
            {
                throw new InvalidDataException(
                    $"Player Position attribute '{name}' is not a 3-component XMX float vector.");
            }

            int offset =
                checked(
                    (int)(
                        attr.ValueVariant &
                        0x00FFFFFFu));

            if (offset < 0 ||
                offset +
                    12 >
                variants.Count)
            {
                throw new InvalidDataException(
                    $"Player Position vector '{name}' points outside the variant pool.");
            }

            WriteFloatToList(
                variants,
                offset,
                value.X,
                bigEndian);

            WriteFloatToList(
                variants,
                offset + 4,
                value.Y,
                bigEndian);

            WriteFloatToList(
                variants,
                offset + 8,
                value.Z,
                bigEndian);
        }

        private static void WriteFloatToList(
            List<byte> variants,
            int offset,
            float value,
            bool bigEndian)
        {
            Span<byte> temp =
                stackalloc byte[4];

            uint bits =
                BitConverter.SingleToUInt32Bits(
                    value);

            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteUInt32BigEndian(
                        temp,
                        bits);
            }
            else
            {
                BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        temp,
                        bits);
            }

            for (int i = 0;
                 i < 4;
                 i++)
            {
                variants[
                    offset +
                    i] =
                        temp[i];
            }
        }

        private static void CollectStructuralSubtreeIndices(
            IReadOnlyList<StructuralNode> nodes,
            int rootIndex,
            HashSet<int> result)
        {
            Stack<int> pending =
                new();

            pending.Push(
                rootIndex);

            while (pending.Count >
                   0)
            {
                int index =
                    pending.Pop();

                if (index < 0 ||
                    index >=
                        nodes.Count)
                {
                    throw new InvalidDataException(
                        "Player-start structure contains an invalid XMX node index.");
                }

                if (!result.Add(
                        index))
                {
                    continue;
                }

                foreach (uint child
                         in nodes[index]
                             .Children)
                {
                    pending.Push(
                        checked(
                            (int)child));
                }
            }
        }

        private static List<StructuralNode> CompactStructuralNodes(
            IReadOnlyList<StructuralNode> sourceNodes,
            HashSet<int> removedNodes)
        {
            int[] remap =
                new int[
                    sourceNodes.Count];

            Array.Fill(
                remap,
                -1);

            List<StructuralNode> result =
                new(
                    sourceNodes.Count -
                    removedNodes.Count);

            for (int oldIndex = 0;
                 oldIndex <
                     sourceNodes.Count;
                 oldIndex++)
            {
                if (removedNodes.Contains(
                        oldIndex))
                {
                    continue;
                }

                remap[oldIndex] =
                    result.Count;

                result.Add(
                    sourceNodes[oldIndex]);
            }

            for (int oldIndex = 0;
                 oldIndex <
                     sourceNodes.Count;
                 oldIndex++)
            {
                int newIndex =
                    remap[oldIndex];

                if (newIndex <
                    0)
                {
                    continue;
                }

                StructuralNode node =
                    result[newIndex];

                if (node.Parent !=
                    uint.MaxValue)
                {
                    int oldParent =
                        checked(
                            (int)node.Parent);

                    if (oldParent < 0 ||
                        oldParent >=
                            remap.Length ||
                        remap[oldParent] <
                            0)
                    {
                        throw new InvalidDataException(
                            "A retained XMX player-start node has an invalid/deleted parent.");
                    }

                    node.Parent =
                        checked(
                            (uint)remap[oldParent]);
                }

                List<uint> children =
                    new();

                foreach (uint oldChildValue
                         in node.Children)
                {
                    int oldChild =
                        checked(
                            (int)oldChildValue);

                    if (oldChild < 0 ||
                        oldChild >=
                            remap.Length)
                    {
                        throw new InvalidDataException(
                            "Player-start XMX node references an invalid child.");
                    }

                    int newChild =
                        remap[oldChild];

                    if (newChild >=
                        0)
                    {
                        children.Add(
                            checked(
                                (uint)newChild));
                    }
                }

                node.Children.Clear();
                node.Children.AddRange(
                    children);
            }

            return result;
        }

        private static byte[] BuildPackedXmx(
            IReadOnlyList<StructuralNode> nodes,
            IReadOnlyList<byte> variantBytes,
            PackedLayout layout,
            bool bigEndian)
        {
            int alignment =
                layout.PointerSize ==
                    8
                    ? 8
                    : 4;

            int nodesOffset =
                AlignValue(
                    layout.RootStructureSize,
                    alignment);

            int cursor =
                checked(
                    nodesOffset +
                    nodes.Count *
                    layout.NodeSize);

            int[] attributeOffsets =
                new int[
                    nodes.Count];

            int[] childOffsets =
                new int[
                    nodes.Count];

            for (int i = 0;
                 i < nodes.Count;
                 i++)
            {
                StructuralNode node =
                    nodes[i];

                if (node.Attributes.Count >
                    0)
                {
                    cursor =
                        AlignValue(
                            cursor,
                            8);

                    attributeOffsets[i] =
                        cursor;

                    cursor =
                        checked(
                            cursor +
                            node.Attributes.Count *
                            XmxAttributeSize);
                }

                if (node.Children.Count >
                    0)
                {
                    cursor =
                        AlignValue(
                            cursor,
                            4);

                    childOffsets[i] =
                        cursor;

                    cursor =
                        checked(
                            cursor +
                            node.Children.Count *
                            4);
                }
            }

            int variantOffset =
                AlignValue(
                    cursor,
                    8);

            byte[] result =
                new byte[
                    checked(
                        variantOffset +
                        variantBytes.Count)];

            WriteUInt32(
                result,
                0,
                XmxSignature,
                bigEndian);

            WritePackedArrayHeader(
                result,
                layout.NodesArrayOffset,
                checked(
                    (uint)nodes.Count),
                checked(
                    (ulong)nodesOffset),
                layout.PointerSize,
                bigEndian);

            WritePackedArrayHeader(
                result,
                layout.VariantArrayOffset,
                checked(
                    (uint)variantBytes.Count),
                checked(
                    (ulong)variantOffset),
                layout.PointerSize,
                bigEndian);

            for (int i = 0;
                 i < nodes.Count;
                 i++)
            {
                StructuralNode node =
                    nodes[i];

                int p =
                    checked(
                        nodesOffset +
                        i *
                        layout.NodeSize);

                WriteUInt32(
                    result,
                    p +
                    layout.NodeParentOffset,
                    node.Parent,
                    bigEndian);

                WriteUInt32(
                    result,
                    p +
                    layout.NodeNameOffset,
                    node.NameVariant,
                    bigEndian);

                WriteUInt32(
                    result,
                    p +
                    layout.NodeTextOffset,
                    node.TextVariant,
                    bigEndian);

                WritePackedArrayHeader(
                    result,
                    p +
                    layout.NodeAttributesOffset,
                    checked(
                        (uint)node.Attributes.Count),
                    node.Attributes.Count ==
                            0
                        ? ulong.MaxValue
                        : checked(
                            (ulong)attributeOffsets[i]),
                    layout.PointerSize,
                    bigEndian);

                WritePackedArrayHeader(
                    result,
                    p +
                    layout.NodeChildrenOffset,
                    checked(
                        (uint)node.Children.Count),
                    node.Children.Count ==
                            0
                        ? ulong.MaxValue
                        : checked(
                            (ulong)childOffsets[i]),
                    layout.PointerSize,
                    bigEndian);

                for (int a = 0;
                     a < node.Attributes.Count;
                     a++)
                {
                    int ap =
                        attributeOffsets[i] +
                        a *
                        XmxAttributeSize;

                    WriteUInt32(
                        result,
                        ap,
                        node.Attributes[a]
                            .NameVariant,
                        bigEndian);

                    WriteUInt32(
                        result,
                        ap + 4,
                        node.Attributes[a]
                            .ValueVariant,
                        bigEndian);
                }

                for (int c = 0;
                     c < node.Children.Count;
                     c++)
                {
                    WriteUInt32(
                        result,
                        childOffsets[i] +
                        c *
                        4,
                        node.Children[c],
                        bigEndian);
                }
            }

            for (int i = 0;
                 i < variantBytes.Count;
                 i++)
            {
                result[
                    variantOffset +
                    i] =
                        variantBytes[i];
            }

            return result;
        }

        private static byte[] ExtractPackedXmxData(
            byte[] xmbData)
        {
            if (xmbData.Length <
                EcfHeaderSize)
            {
                throw new InvalidDataException(
                    "XMB is too small.");
            }

            if (ReadUInt32(
                    xmbData,
                    0,
                    true) !=
                EcfMagic)
            {
                throw new InvalidDataException(
                    "Invalid XMB ECF magic.");
            }

            uint headerSize =
                ReadUInt32(
                    xmbData,
                    4,
                    true);

            uint declaredSize =
                ReadUInt32(
                    xmbData,
                    12,
                    true);

            ushort numChunks =
                ReadUInt16(
                    xmbData,
                    16,
                    true);

            uint fileId =
                ReadUInt32(
                    xmbData,
                    20,
                    true);

            ushort chunkExtra =
                ReadUInt16(
                    xmbData,
                    24,
                    true);

            if (fileId !=
                XmbEcfId)
            {
                throw new InvalidDataException(
                    "ECF is not a Halo Wars XMB.");
            }

            if (declaredSize !=
                    0 &&
                declaredSize !=
                    xmbData.Length)
            {
                throw new InvalidDataException(
                    "XMB file size does not match its ECF header.");
            }

            int chunkHeaderSize =
                checked(
                    EcfChunkHeaderSize +
                    chunkExtra);

            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int p =
                    checked(
                        (int)headerSize +
                        i *
                        chunkHeaderSize);

                ulong chunkId =
                    ReadUInt64(
                        xmbData,
                        p,
                        true);

                if (chunkId !=
                    XmxPackedDataChunkId)
                {
                    continue;
                }

                uint offset =
                    ReadUInt32(
                        xmbData,
                        p + 8,
                        true);

                uint size =
                    ReadUInt32(
                        xmbData,
                        p + 12,
                        true);

                ushort flags =
                    ReadUInt16(
                        xmbData,
                        p + 22,
                        true);

                if ((flags &
                     DeflateStreamResourceFlag) ==
                    0)
                {
                    throw new InvalidDataException(
                        "XMX chunk is not a Halo Wars Deflate Stream.");
                }

                if ((ulong)offset +
                        size >
                    (ulong)xmbData.Length)
                {
                    throw new InvalidDataException(
                        "XMX chunk points outside the XMB.");
                }

                byte[] compressed =
                    xmbData
                        .AsSpan(
                            checked(
                                (int)offset),
                            checked(
                                (int)size))
                        .ToArray();

                return EraCompressionService
                    .DecompressDeflateStream(
                        compressed,
                        0);
            }

            throw new InvalidDataException(
                "XMB does not contain its packed XMX chunk.");
        }

        private static bool DetermineEndianness(
            byte[] data)
        {
            if (ReadUInt32(
                    data,
                    0,
                    true) ==
                XmxSignature)
            {
                return true;
            }

            if (ReadUInt32(
                    data,
                    0,
                    false) ==
                XmxSignature)
            {
                return false;
            }

            throw new InvalidDataException(
                "Invalid packed XMX signature.");
        }

        private static PackedLayout DetectPackedLayout(
            byte[] data,
            bool bigEndian)
        {
            PackedLayout layout32 =
                PackedLayout.Create32Bit();

            if (IsPlausibleLayout(
                    data,
                    bigEndian,
                    layout32))
            {
                return layout32;
            }

            PackedLayout layout64 =
                PackedLayout.Create64Bit();

            if (IsPlausibleLayout(
                    data,
                    bigEndian,
                    layout64))
            {
                return layout64;
            }

            throw new InvalidDataException(
                "Unable to determine the packed XMX structure layout.");
        }

        private static bool IsPlausibleLayout(
            byte[] data,
            bool bigEndian,
            PackedLayout layout)
        {
            try
            {
                if (data.Length <
                    layout.RootStructureSize)
                {
                    return false;
                }

                PackedArray nodes =
                    ReadPackedArray(
                        data,
                        layout.NodesArrayOffset,
                        bigEndian,
                        layout.PointerSize);

                PackedArray variants =
                    ReadPackedArray(
                        data,
                        layout.VariantArrayOffset,
                        bigEndian,
                        layout.PointerSize);

                return nodes.Count >
                           0 &&
                       nodes.Count <
                           100_000_000 &&
                       IsArrayInsideBuffer(
                           data,
                           nodes,
                           layout.NodeSize) &&
                       IsArrayInsideBuffer(
                           data,
                           variants,
                           1);
            }
            catch
            {
                return false;
            }
        }

        private static XmxNode ParseNode(
            byte[] data,
            int offset,
            PackedLayout layout,
            bool bigEndian)
        {
            XmxNode node =
                new()
                {
                    Parent =
                        ReadUInt32(
                            data,
                            offset +
                            layout.NodeParentOffset,
                            bigEndian),

                    NameVariant =
                        ReadUInt32(
                            data,
                            offset +
                            layout.NodeNameOffset,
                            bigEndian),

                    TextVariant =
                        ReadUInt32(
                            data,
                            offset +
                            layout.NodeTextOffset,
                            bigEndian),

                    Attributes =
                        ReadPackedArray(
                            data,
                            offset +
                            layout.NodeAttributesOffset,
                            bigEndian,
                            layout.PointerSize),

                    Children =
                        ReadPackedArray(
                            data,
                            offset +
                            layout.NodeChildrenOffset,
                            bigEndian,
                            layout.PointerSize)
                };

            if (!IsArrayInsideBuffer(
                    data,
                    node.Attributes,
                    XmxAttributeSize) ||
                !IsArrayInsideBuffer(
                    data,
                    node.Children,
                    4))
            {
                throw new InvalidDataException(
                    "XMX node contains an invalid packed array.");
            }

            return node;
        }

        private static PackedArray ReadPackedArray(
            byte[] data,
            int offset,
            bool bigEndian,
            int pointerSize)
        {
            int pointerField =
                pointerSize ==
                    8
                    ? 8
                    : 4;

            int structureSize =
                pointerSize ==
                    8
                    ? 16
                    : 8;

            EnsureRange(
                data,
                offset,
                structureSize);

            uint count =
                ReadUInt32(
                    data,
                    offset,
                    bigEndian);

            ulong pointer =
                pointerSize ==
                    8
                    ? ReadUInt64(
                        data,
                        offset +
                        pointerField,
                        bigEndian)
                    : ReadUInt32(
                        data,
                        offset +
                        pointerField,
                        bigEndian);

            if (count ==
                0)
            {
                return new PackedArray(
                    0,
                    0);
            }

            if (pointer ==
                    0x00000000FFFFFFFFUL ||
                pointer ==
                    ulong.MaxValue)
            {
                throw new InvalidDataException(
                    "Packed XMX array has elements but a null pointer.");
            }

            return new PackedArray(
                count,
                pointer);
        }

        private static bool IsArrayInsideBuffer(
            byte[] data,
            PackedArray array,
            int elementSize)
        {
            if (array.Count ==
                0)
            {
                return true;
            }

            if (array.Offset >=
                (ulong)data.Length)
            {
                return false;
            }

            ulong end =
                array.Offset +
                (ulong)array.Count *
                (ulong)elementSize;

            return end >=
                       array.Offset &&
                   end <=
                       (ulong)data.Length;
        }

        private static string DecodeVariant(
            uint value,
            byte[] data,
            PackedArray variants,
            bool bigEndian)
        {
            uint typeBits =
                value >>
                24;

            uint bits =
                value &
                0x00FFFFFFu;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            bool isOffset =
                (typeBits &
                 0x80) !=
                0;

            bool isUnsigned =
                (typeBits &
                 0x40) !=
                0;

            int vectorSize =
                1 +
                (int)(
                    (typeBits &
                     0x30) >>
                    4);

            switch (type)
            {
                case 0:
                    return string.Empty;

                case 1:
                    return DecodeFloat24(
                            bits)
                        .ToString(
                            "G6",
                            CultureInfo.InvariantCulture);

                case 2:
                    {
                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                4);

                        float value32 =
                            BitConverter
                                .Int32BitsToSingle(
                                    unchecked(
                                        (int)ReadUInt32(
                                            data,
                                            p,
                                            bigEndian)));

                        return value32.ToString(
                            "G8",
                            CultureInfo.InvariantCulture);
                    }

                case 3:
                    {
                        if (isUnsigned)
                        {
                            return bits.ToString(
                                CultureInfo.InvariantCulture);
                        }

                        int signed =
                            (int)bits;

                        if ((signed &
                             0x00800000) !=
                            0)
                        {
                            signed |=
                                unchecked(
                                    (int)0xFF000000);
                        }

                        return signed.ToString(
                            CultureInfo.InvariantCulture);
                    }

                case 4:
                    {
                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                4);

                        uint raw =
                            ReadUInt32(
                                data,
                                p,
                                bigEndian);

                        return isUnsigned
                            ? raw.ToString(
                                CultureInfo.InvariantCulture)
                            : unchecked(
                                (int)raw)
                                .ToString(
                                    CultureInfo.InvariantCulture);
                    }

                case 5:
                    {
                        bool negative =
                            (bits &
                             0x00800000) !=
                            0;

                        uint raw =
                            bits &
                            0x007FFFFF;

                        string text =
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}.{1:0000}",
                                raw / 10000,
                                raw % 10000);

                        return negative
                            ? "-" +
                              text
                            : text;
                    }

                case 6:
                    {
                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                8);

                        double number =
                            BitConverter
                                .Int64BitsToDouble(
                                    unchecked(
                                        (long)ReadUInt64(
                                            data,
                                            p,
                                            bigEndian)));

                        return number.ToString(
                            "G17",
                            CultureInfo.InvariantCulture);
                    }

                case 7:
                    return bits ==
                           0
                        ? "false"
                        : "true";

                case 8:
                    {
                        if (!isOffset)
                        {
                            char[] chars =
                            {
                                (char)(
                                    bits &
                                    0xFF),
                                (char)(
                                    (bits >> 8) &
                                    0xFF),
                                (char)(
                                    (bits >> 16) &
                                    0xFF)
                            };

                            return new string(
                                    chars)
                                .TrimEnd(
                                    '\0');
                        }

                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                1);

                        int limit =
                            GetVariantDataEnd(
                                variants);

                        int end =
                            p;

                        while (end <
                                   limit &&
                               data[end] !=
                                   0)
                        {
                            end++;
                        }

                        return Encoding.ASCII
                            .GetString(
                                data,
                                p,
                                end -
                                p);
                    }

                case 9:
                    {
                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                2);

                        int limit =
                            GetVariantDataEnd(
                                variants);

                        StringBuilder text =
                            new();

                        while (p + 1 <
                               limit)
                        {
                            ushort c =
                                ReadUInt16(
                                    data,
                                    p,
                                    bigEndian);

                            p +=
                                2;

                            if (c ==
                                0)
                            {
                                break;
                            }

                            text.Append(
                                (char)c);
                        }

                        return text.ToString();
                    }

                case 10:
                    {
                        int p =
                            GetVariantOffset(
                                variants,
                                bits,
                                vectorSize *
                                4);

                        string[] values =
                            new string[
                                vectorSize];

                        for (int i = 0;
                             i < vectorSize;
                             i++)
                        {
                            float number =
                                BitConverter
                                    .Int32BitsToSingle(
                                        unchecked(
                                            (int)ReadUInt32(
                                                data,
                                                p +
                                                i *
                                                4,
                                                bigEndian)));

                            values[i] =
                                number.ToString(
                                    "G8",
                                    CultureInfo.InvariantCulture);
                        }

                        return string.Join(
                            ",",
                            values);
                    }

                default:
                    throw new InvalidDataException(
                        $"Unsupported XMX variant type {type}.");
            }
        }

        private static float DecodeFloat24(
            uint value)
        {
            uint sign =
                value &
                0x00800000;

            int exponentBits =
                (int)(
                    (value >> 17) &
                    0x3F);

            if (exponentBits ==
                0)
            {
                return sign !=
                       0
                    ? -0.0f
                    : 0.0f;
            }

            int exponent =
                exponentBits -
                31;

            uint mantissa =
                value &
                0x0001FFFF;

            uint newExponent =
                checked(
                    (uint)(
                        exponent +
                        127));

            uint resultBits =
                (sign !=
                     0
                    ? 0x80000000u
                    : 0u)
                |
                (newExponent <<
                 23)
                |
                (mantissa <<
                 6);

            return BitConverter
                .Int32BitsToSingle(
                    unchecked(
                        (int)resultBits));
        }

        private static int GetVariantOffset(
            PackedArray variants,
            uint relative,
            int bytes)
        {
            if ((ulong)relative +
                    (ulong)bytes >
                variants.Count)
            {
                throw new InvalidDataException(
                    "XMX variant points outside its pool.");
            }

            ulong absolute =
                variants.Offset +
                relative;

            if (absolute >
                int.MaxValue)
            {
                throw new InvalidDataException(
                    "XMX variant offset is too large.");
            }

            return checked(
                (int)absolute);
        }

        private static int GetVariantDataEnd(
            PackedArray variants)
        {
            ulong end =
                variants.Offset +
                variants.Count;

            if (end >
                int.MaxValue)
            {
                throw new InvalidDataException(
                    "XMX variant pool is too large.");
            }

            return checked(
                (int)end);
        }

        private static void WritePackedArrayHeader(
            byte[] data,
            int offset,
            uint count,
            ulong pointer,
            int pointerSize,
            bool bigEndian)
        {
            WriteUInt32(
                data,
                offset,
                count,
                bigEndian);

            if (pointerSize ==
                8)
            {
                ulong actual =
                    count ==
                        0
                        ? 0x00000000FFFFFFFFUL
                        : pointer;

                WriteUInt64(
                    data,
                    offset + 8,
                    actual,
                    bigEndian);
            }
            else
            {
                WriteUInt32(
                    data,
                    offset + 4,
                    count ==
                        0
                        ? uint.MaxValue
                        : checked(
                            (uint)pointer),
                    bigEndian);
            }
        }

        private static int AlignValue(
            int value,
            int alignment)
        {
            int remainder =
                value %
                alignment;

            return remainder ==
                   0
                ? value
                : checked(
                    value +
                    alignment -
                    remainder);
        }

        private static void AlignByteList(
            List<byte> bytes,
            int alignment)
        {
            while (bytes.Count %
                   alignment !=
                   0)
            {
                bytes.Add(
                    0);
            }
        }

        private static void EnsureVariantPoolOffset(
            int offset)
        {
            if (offset <
                    0 ||
                offset >
                    16_777_215)
            {
                throw new InvalidDataException(
                    "XMX variant pool exceeded the 24-bit offset limit.");
            }
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

            ReadOnlySpan<byte> span =
                data.AsSpan(
                    offset,
                    4);

            return bigEndian
                ? BinaryPrimitives
                    .ReadUInt32BigEndian(
                        span)
                : BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        span);
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

            ReadOnlySpan<byte> span =
                data.AsSpan(
                    offset,
                    2);

            return bigEndian
                ? BinaryPrimitives
                    .ReadUInt16BigEndian(
                        span)
                : BinaryPrimitives
                    .ReadUInt16LittleEndian(
                        span);
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

            ReadOnlySpan<byte> span =
                data.AsSpan(
                    offset,
                    8);

            return bigEndian
                ? BinaryPrimitives
                    .ReadUInt64BigEndian(
                        span)
                : BinaryPrimitives
                    .ReadUInt64LittleEndian(
                        span);
        }

        private static void WriteUInt32(
            byte[] data,
            int offset,
            uint value,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                4);

            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteUInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4),
                        value);
            }
            else
            {
                BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        data.AsSpan(
                            offset,
                            4),
                        value);
            }
        }

        private static void WriteUInt64(
            byte[] data,
            int offset,
            ulong value,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                8);

            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteUInt64BigEndian(
                        data.AsSpan(
                            offset,
                            8),
                        value);
            }
            else
            {
                BinaryPrimitives
                    .WriteUInt64LittleEndian(
                        data.AsSpan(
                            offset,
                            8),
                        value);
            }
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
                    "Attempted to access data outside the XMB buffer.");
            }
        }

        private sealed class PackedDocument
        {
            public byte[] Data
            {
                get;
                init;
            } = Array.Empty<byte>();

            public bool BigEndian
            {
                get;
                init;
            }

            public PackedLayout Layout
            {
                get;
                init;
            } = PackedLayout.Create64Bit();

            public PackedArray NodesArray
            {
                get;
                init;
            }

            public PackedArray VariantData
            {
                get;
                init;
            }

            public List<XmxNode> Nodes
            {
                get;
                init;
            } = new();
        }

        private readonly struct PackedArray
        {
            public PackedArray(
                uint count,
                ulong offset)
            {
                Count =
                    count;

                Offset =
                    offset;
            }

            public uint Count
            {
                get;
            }

            public ulong Offset
            {
                get;
            }
        }

        private sealed class XmxNode
        {
            public uint Parent
            {
                get;
                init;
            }

            public uint NameVariant
            {
                get;
                init;
            }

            public uint TextVariant
            {
                get;
                init;
            }

            public PackedArray Attributes
            {
                get;
                init;
            }

            public PackedArray Children
            {
                get;
                init;
            }
        }

        private sealed class StructuralNode
        {
            public uint Parent
            {
                get;
                set;
            }

            public uint NameVariant
            {
                get;
                set;
            }

            public uint TextVariant
            {
                get;
                set;
            }

            public List<StructuralAttribute> Attributes
            {
                get;
            } = new();

            public List<uint> Children
            {
                get;
            } = new();
        }

        private sealed class StructuralAttribute
        {
            public string Name
            {
                get;
                set;
            } = string.Empty;

            public uint NameVariant
            {
                get;
                set;
            }

            public uint ValueVariant
            {
                get;
                set;
            }
        }

        private sealed class PackedLayout
        {
            public int PointerSize
            {
                get;
                init;
            }

            public int RootStructureSize
            {
                get;
                init;
            }

            public int NodesArrayOffset
            {
                get;
                init;
            }

            public int VariantArrayOffset
            {
                get;
                init;
            }

            public int NodeSize
            {
                get;
                init;
            }

            public int NodeParentOffset
            {
                get;
                init;
            }

            public int NodeNameOffset
            {
                get;
                init;
            }

            public int NodeTextOffset
            {
                get;
                init;
            }

            public int NodeAttributesOffset
            {
                get;
                init;
            }

            public int NodeChildrenOffset
            {
                get;
                init;
            }

            public static PackedLayout Create32Bit()
            {
                return new PackedLayout
                {
                    PointerSize =
                        4,

                    RootStructureSize =
                        20,

                    NodesArrayOffset =
                        4,

                    VariantArrayOffset =
                        12,

                    NodeSize =
                        28,

                    NodeParentOffset =
                        0,

                    NodeNameOffset =
                        4,

                    NodeTextOffset =
                        8,

                    NodeAttributesOffset =
                        12,

                    NodeChildrenOffset =
                        20
                };
            }

            public static PackedLayout Create64Bit()
            {
                return new PackedLayout
                {
                    PointerSize =
                        8,

                    RootStructureSize =
                        40,

                    NodesArrayOffset =
                        8,

                    VariantArrayOffset =
                        24,

                    NodeSize =
                        48,

                    NodeParentOffset =
                        0,

                    NodeNameOffset =
                        4,

                    NodeTextOffset =
                        8,

                    NodeAttributesOffset =
                        16,

                    NodeChildrenOffset =
                        32
                };
            }
        }
    }
}
