using Ensemble.Models;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;

namespace Ensemble.Services
{
    public static class XmbDocumentService
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

        public static string Read(
            byte[] xmbData)
        {
            if (xmbData == null)
            {
                throw new ArgumentNullException(
                    nameof(xmbData));
            }

            byte[] packedData =
                ExtractPackedXmxData(
                    xmbData);

            return ParsePackedXmx(
                packedData);
        }

        // =========================================================
        // XMB / ECF CONTAINER
        // =========================================================

        private static byte[] ExtractPackedXmxData(
            byte[] xmbData)
        {
            if (xmbData.Length <
                EcfHeaderSize)
            {
                throw new InvalidDataException(
                    "XMB is too small to contain a valid ECF header.");
            }

            uint magic =
                ReadUInt32BigEndian(
                    xmbData,
                    0);

            if (magic != EcfMagic)
            {
                throw new InvalidDataException(
                    $"Invalid XMB ECF magic. " +
                    $"Expected 0x{EcfMagic:X8}, " +
                    $"found 0x{magic:X8}.");
            }

            uint headerSize =
                ReadUInt32BigEndian(
                    xmbData,
                    4);

            uint declaredFileSize =
                ReadUInt32BigEndian(
                    xmbData,
                    12);

            ushort numChunks =
                ReadUInt16BigEndian(
                    xmbData,
                    16);

            uint fileId =
                ReadUInt32BigEndian(
                    xmbData,
                    20);

            ushort chunkExtraDataSize =
                ReadUInt16BigEndian(
                    xmbData,
                    24);

            if (fileId != XmbEcfId)
            {
                throw new InvalidDataException(
                    $"This ECF is not a Halo Wars XMB. " +
                    $"Expected ID 0x{XmbEcfId:X8}, " +
                    $"found 0x{fileId:X8}.");
            }

            if (declaredFileSize != 0 &&
                declaredFileSize != xmbData.Length)
            {
                throw new InvalidDataException(
                    $"XMB file-size mismatch. " +
                    $"Header says {declaredFileSize:N0} bytes, " +
                    $"actual size is {xmbData.Length:N0} bytes.");
            }

            int chunkHeaderSize =
                checked(
                    EcfChunkHeaderSize +
                    chunkExtraDataSize);

            long chunkTableEnd =
                (long)headerSize +
                ((long)chunkHeaderSize *
                 numChunks);

            if (headerSize <
                    EcfHeaderSize ||
                chunkTableEnd >
                    xmbData.Length)
            {
                throw new InvalidDataException(
                    "XMB contains an invalid ECF chunk table.");
            }

            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int p =
                    checked(
                        (int)headerSize +
                        (i * chunkHeaderSize));

                ulong chunkId =
                    ReadUInt64BigEndian(
                        xmbData,
                        p);

                if (chunkId !=
                    XmxPackedDataChunkId)
                {
                    continue;
                }

                uint chunkOffset =
                    ReadUInt32BigEndian(
                        xmbData,
                        p + 8);

                uint chunkSize =
                    ReadUInt32BigEndian(
                        xmbData,
                        p + 12);

                uint expectedAdler =
                    ReadUInt32BigEndian(
                        xmbData,
                        p + 16);

                ushort resourceFlags =
                    ReadUInt16BigEndian(
                        xmbData,
                        p + 22);

                if ((resourceFlags &
                     DeflateStreamResourceFlag) == 0)
                {
                    throw new InvalidDataException(
                        "The XMX data chunk is not marked " +
                        "as a Halo Wars Deflate Stream.");
                }

                long chunkEnd =
                    (long)chunkOffset +
                    chunkSize;

                if (chunkOffset >=
                        xmbData.Length ||
                    chunkEnd >
                        xmbData.Length)
                {
                    throw new InvalidDataException(
                        "XMX chunk points outside the XMB file.");
                }

                byte[] compressed =
                    xmbData
                        .AsSpan(
                            checked(
                                (int)chunkOffset),
                            checked(
                                (int)chunkSize))
                        .ToArray();

                uint actualAdler =
                    EraCompressionService.Adler32(
                        compressed);

                if (actualAdler !=
                    expectedAdler)
                {
                    throw new InvalidDataException(
                        $"XMX chunk failed its Adler32 check.\n\n" +
                        $"Expected: 0x{expectedAdler:X8}\n" +
                        $"Actual:   0x{actualAdler:X8}");
                }

                return
                    EraCompressionService
                        .DecompressDeflateStream(
                            compressed,
                            0);
            }

            throw new InvalidDataException(
                $"XMB does not contain the required XMX chunk " +
                $"0x{XmxPackedDataChunkId:X16}.");
        }

        // =========================================================
        // PACKED XMX ROOT
        // =========================================================

        private static string ParsePackedXmx(
            byte[] data)
        {
            if (data.Length < 20)
            {
                throw new InvalidDataException(
                    "Packed XMX data is too small.");
            }

            bool bigEndian;

            uint signatureBig =
                ReadUInt32(
                    data,
                    0,
                    true);

            uint signatureLittle =
                ReadUInt32(
                    data,
                    0,
                    false);

            if (signatureBig ==
                XmxSignature)
            {
                bigEndian = true;
            }
            else if (signatureLittle ==
                     XmxSignature)
            {
                bigEndian = false;
            }
            else
            {
                throw new InvalidDataException(
                    $"Invalid packed XMX signature.\n\n" +
                    $"Big-endian:    0x{signatureBig:X8}\n" +
                    $"Little-endian: 0x{signatureLittle:X8}");
            }

            PackedLayout layout =
                DetectPackedLayout(
                    data,
                    bigEndian);

            PackedArray nodes =
                ReadPackedArray(
                    data,
                    layout.NodesArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            PackedArray variantData =
                ReadPackedArray(
                    data,
                    layout.VariantArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            if (nodes.Count == 0)
            {
                throw new InvalidDataException(
                    "XMX contains no XML nodes.");
            }

            ValidateArray(
                data,
                nodes,
                layout.NodeSize,
                "node");

            ValidateByteArray(
                data,
                variantData,
                "variant data");

            List<XmxNode> parsedNodes =
                new List<XmxNode>(
                    checked(
                        (int)nodes.Count));

            for (uint i = 0;
                 i < nodes.Count;
                 i++)
            {
                int nodeOffset =
                    checked(
                        (int)(
                            nodes.Offset +
                            ((ulong)i *
                             (ulong)layout.NodeSize)));

                XmxNode node =
                    ParseNode(
                        data,
                        nodeOffset,
                        layout,
                        bigEndian);

                parsedNodes.Add(
                    node);
            }

            StringBuilder output =
                new StringBuilder();

            output.AppendLine(
                "<?xml version=\"1.0\"?>");

            output.AppendLine(
                $"<!-- Ensemble XMB decoder | " +
                $"{(bigEndian ? "Big Endian" : "Little Endian")} | " +
                $"{layout.PointerSize * 8}-bit packed layout | " +
                $"{nodes.Count:N0} nodes -->");

            HashSet<int> recursionStack =
                new HashSet<int>();

            RenderNode(
                output,
                0,
                parsedNodes,
                data,
                variantData,
                bigEndian,
                recursionStack,
                0);

            return output.ToString();
        }

        public static byte[] WriteScenario(
            byte[] originalXmbData,
            ScenarioMap scenario)
        {
            if (originalXmbData == null)
                throw new ArgumentNullException(
                    nameof(originalXmbData));

            if (scenario == null)
                throw new ArgumentNullException(
                    nameof(scenario));

            byte[] packedData =
                ExtractPackedXmxData(
                    originalXmbData);

            bool bigEndian;

            uint signatureBig =
                ReadUInt32(
                    packedData,
                    0,
                    true);

            uint signatureLittle =
                ReadUInt32(
                    packedData,
                    0,
                    false);

            if (signatureBig ==
                XmxSignature)
            {
                bigEndian =
                    true;
            }
            else if (signatureLittle ==
                     XmxSignature)
            {
                bigEndian =
                    false;
            }
            else
            {
                throw new InvalidDataException(
                    "Invalid packed XMX signature.");
            }

            PackedLayout layout =
                DetectPackedLayout(
                    packedData,
                    bigEndian);

            PackedArray nodes =
                ReadPackedArray(
                    packedData,
                    layout.NodesArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            PackedArray variantData =
                ReadPackedArray(
                    packedData,
                    layout.VariantArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            List<XmxNode> parsedNodes =
                new List<XmxNode>(
                    checked(
                        (int)nodes.Count));

            for (uint i = 0;
                 i < nodes.Count;
                 i++)
            {
                int nodeOffset =
                    checked(
                        (int)(
                            nodes.Offset +
                            ((ulong)i *
                             (ulong)layout.NodeSize)));

                parsedNodes.Add(
                    ParseNode(
                        packedData,
                        nodeOffset,
                        layout,
                        bigEndian));
            }

            bool requiresStructuralRebuild =
                scenario.Objects.Any(
                    x =>
                    x.IsNewObject) || scenario.DeletedObjectIds.Count > 0;

            if (requiresStructuralRebuild)
            {
                packedData =
                    RebuildPackedXmxForStructuralEdits(
                        packedData,
                        layout,
                        bigEndian,
                        scenario);

                // Structural rebuilding changes every absolute
                // node/array address. Re-read the rebuilt structure
                // before applying normal property patches.

                layout =
                    DetectPackedLayout(
                        packedData,
                        bigEndian);

                nodes =
                    ReadPackedArray(
                        packedData,
                        layout.NodesArrayOffset,
                        bigEndian,
                        layout.PointerSize);

                variantData =
                    ReadPackedArray(
                        packedData,
                        layout.VariantArrayOffset,
                        bigEndian,
                        layout.PointerSize);

                parsedNodes =
                    new List<XmxNode>(
                        checked(
                            (int)nodes.Count));

                for (uint i = 0;
                     i < nodes.Count;
                     i++)
                {
                    int nodeOffset =
                        checked(
                            (int)(
                                nodes.Offset +
                                ((ulong)i *
                                 (ulong)layout.NodeSize)));

                    parsedNodes.Add(
                        ParseNode(
                            packedData,
                            nodeOffset,
                            layout,
                            bigEndian));
                }
            }

            PatchScenarioValues(
                packedData,
                parsedNodes,
                variantData,
                bigEndian,
                scenario);

            byte[] compressed =
                EraCompressionService
                    .CompressDeflateStream(
                        packedData);

            byte[] rebuiltXmb =
                EcfFileService.ReplaceChunk(
                    originalXmbData,
                    XmxPackedDataChunkId,
                    compressed);

            // Make sure Ensemble itself can immediately
            // decode what it just generated.
            _ = Read(
                rebuiltXmb);

            return rebuiltXmb;
        }

        private static void PatchScenarioValues(
            byte[] packedData,
            IReadOnlyList<XmxNode> nodes,
            PackedArray variantData,
            bool bigEndian,
            ScenarioMap scenario)
        {
            Dictionary<int, ScenarioObject> objects =
                new Dictionary<int, ScenarioObject>();

            foreach (ScenarioObject obj
                     in scenario.Objects)
            {
                objects[obj.Id] =
                    obj;
            }

            Dictionary<int, ScenarioPlayerStart> starts =
                new Dictionary<int, ScenarioPlayerStart>();

            foreach (ScenarioPlayerStart start
                     in scenario.PlayerStarts)
            {
                starts[start.Number] =
                    start;
            }

            Dictionary<int, ScenarioSphere> spheres =
                new Dictionary<int, ScenarioSphere>();

            foreach (ScenarioSphere sphere
                     in scenario.Spheres)
            {
                spheres[sphere.Id] =
                    sphere;
            }

            for (int nodeIndex = 0;
                 nodeIndex < nodes.Count;
                 nodeIndex++)
            {
                XmxNode node =
                    nodes[nodeIndex];

                string nodeName =
                    DecodeVariant(
                        node.NameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                string parentName =
                    GetParentNodeName(
                        node,
                        nodes,
                        packedData,
                        variantData,
                        bigEndian);

                // ---------------------------------------------------------
                // <Objects><Object ...>
                // ---------------------------------------------------------

                if (nodeName ==
                        "Object" &&
                    parentName ==
                        "Objects")
                {
                    if (!TryGetIntegerAttribute(
                            node,
                            "ID",
                            packedData,
                            variantData,
                            bigEndian,
                            out int id))
                    {
                        continue;
                    }

                    if (!objects.TryGetValue(
                            id,
                            out ScenarioObject? obj))
                    {
                        continue;
                    }

                    PatchVectorAttribute(
                        node,
                        "Position",
                        obj.Position,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchVectorAttribute(
                        node,
                        "Forward",
                        obj.Forward,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchVectorAttribute(
                        node,
                        "Right",
                        obj.Right,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchIntegerAttribute(
                        node,
                        "Player",
                        obj.Player,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchIntegerAttribute(
                        node,
                        "Group",
                        obj.Group,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchIntegerAttribute(
                        node,
                        "VisualVariationIndex",
                        obj.VisualVariationIndex,
                        packedData,
                        variantData,
                        bigEndian);

                    continue;
                }

                // ---------------------------------------------------------
                // <Positions><Position ...>
                // ---------------------------------------------------------

                if (nodeName ==
                        "Position" &&
                    parentName ==
                        "Positions")
                {
                    if (!TryGetIntegerAttribute(
                            node,
                            "Number",
                            packedData,
                            variantData,
                            bigEndian,
                            out int number))
                    {
                        continue;
                    }

                    if (!starts.TryGetValue(
                            number,
                            out ScenarioPlayerStart? start))
                    {
                        continue;
                    }

                    PatchVectorAttribute(
                        node,
                        "Position",
                        start.Position,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchVectorAttribute(
                        node,
                        "Forward",
                        start.Forward,
                        packedData,
                        variantData,
                        bigEndian);

                    continue;
                }

                // ---------------------------------------------------------
                // <Spheres><Sphere ...>
                // ---------------------------------------------------------

                if (nodeName ==
                        "Sphere" &&
                    parentName ==
                        "Spheres")
                {
                    if (!TryGetIntegerAttribute(
                            node,
                            "ID",
                            packedData,
                            variantData,
                            bigEndian,
                            out int id))
                    {
                        continue;
                    }

                    if (!spheres.TryGetValue(
                            id,
                            out ScenarioSphere? sphere))
                    {
                        continue;
                    }

                    PatchVectorAttribute(
                        node,
                        "Position",
                        sphere.Position,
                        packedData,
                        variantData,
                        bigEndian);

                    PatchFloatAttribute(
                        node,
                        "Radius",
                        sphere.Radius,
                        packedData,
                        variantData,
                        bigEndian);
                }
            }
        }

        private static byte[] RebuildPackedXmxForStructuralEdits(
            byte[] originalData,
            PackedLayout layout,
            bool bigEndian,
            ScenarioMap scenario)
        {
            PackedArray originalNodesArray =
                ReadPackedArray(
                    originalData,
                    layout.NodesArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            PackedArray originalVariantData =
                ReadPackedArray(
                    originalData,
                    layout.VariantArrayOffset,
                    bigEndian,
                    layout.PointerSize);

            List<StructuralNode> nodes =
                new();

            for (uint i = 0;
                 i < originalNodesArray.Count;
                 i++)
            {
                int nodeOffset =
                    checked(
                        (int)(
                            originalNodesArray.Offset +
                            ((ulong)i *
                             (ulong)layout.NodeSize)));

                XmxNode source =
                    ParseNode(
                        originalData,
                        nodeOffset,
                        layout,
                        bigEndian);

                StructuralNode node =
                    new StructuralNode
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
                                ((ulong)a *
                                 XmxAttributeSize)));

                    uint nameVariant =
                        ReadUInt32(
                            originalData,
                            p,
                            bigEndian);

                    uint valueVariant =
                        ReadUInt32(
                            originalData,
                            p + 4,
                            bigEndian);

                    node.Attributes.Add(
                        new StructuralAttribute
                        {
                            Name =
                                DecodeVariant(
                                    nameVariant,
                                    originalData,
                                    originalVariantData,
                                    bigEndian),

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
                                ((ulong)c * 4)));

                    node.Children.Add(
                        ReadUInt32(
                            originalData,
                            p,
                            bigEndian));
                }

                nodes.Add(
                    node);
            }

            List<byte> variantBytes =
                originalData
                    .AsSpan(
                        checked(
                            (int)originalVariantData.Offset),
                        checked(
                            (int)originalVariantData.Count))
                    .ToArray()
                    .ToList();

            int objectsNodeIndex =
                -1;

            Dictionary<int, int> objectNodeById =
                new();

            for (int i = 0;
                 i < nodes.Count;
                 i++)
            {
                string nodeName =
                    DecodeVariant(
                        nodes[i].NameVariant,
                        originalData,
                        originalVariantData,
                        bigEndian);

                if (nodeName ==
                    "Objects")
                {
                    objectsNodeIndex =
                        i;
                }

                if (nodeName !=
                    "Object")
                {
                    continue;
                }

                StructuralAttribute? idAttribute =
                    nodes[i]
                        .Attributes
                        .FirstOrDefault(
                            x =>
                                x.Name ==
                                "ID");

                if (idAttribute == null)
                    continue;

                string idText =
                    DecodeVariant(
                        idAttribute.ValueVariant,
                        originalData,
                        originalVariantData,
                        bigEndian);

                if (int.TryParse(
                        idText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int id))
                {
                    objectNodeById[id] =
                        i;
                }
            }

            if (objectsNodeIndex < 0)
            {
                throw new InvalidDataException(
                    "Scenario contains no <Objects> XMX node.");
            }

            foreach (ScenarioObject obj
                     in scenario.Objects.Where(
                         x =>
                             x.IsNewObject))
            {
                if (!objectNodeById.TryGetValue(
                        obj.SourceObjectId,
                        out int sourceNodeIndex))
                {
                    throw new InvalidDataException(
                        $"Cannot duplicate object {obj.Id}: " +
                        $"source object ID {obj.SourceObjectId} " +
                        "was not found in the original XMX.");
                }

                uint cloneIndex =
                    CloneStructuralSubtree(
                        nodes,
                        sourceNodeIndex,
                        checked(
                            (uint)objectsNodeIndex),
                        originalData,
                        originalVariantData,
                        variantBytes,
                        bigEndian);

                nodes[
                    objectsNodeIndex]
                    .Children
                    .Add(
                        cloneIndex);

                StructuralNode clone =
                    nodes[
                        checked(
                            (int)cloneIndex)];

                SetStructuralIntegerAttribute(
                    clone,
                    "ID",
                    obj.Id,
                    variantBytes,
                    bigEndian);

                objectNodeById[obj.Id] =
                    checked(
                        (int)cloneIndex);
            }

            HashSet<int> nodesToRemove =
                new HashSet<int>();

            foreach (int deletedId
                     in scenario.DeletedObjectIds)
            {
                if (!objectNodeById.TryGetValue(
                        deletedId,
                        out int objectNodeIndex))
                {
                    throw new InvalidDataException(
                        $"Cannot delete scenario object ID {deletedId}: " +
                        "its original XMX node could not be found.");
                }

                CollectStructuralSubtreeIndices(
                    nodes,
                    objectNodeIndex,
                    nodesToRemove);
            }

            if (nodesToRemove.Count >
                0)
            {
                nodes =
                    CompactStructuralNodes(
                        nodes,
                        nodesToRemove);
            }

            return BuildStructuralPackedXmx(
                nodes,
                variantBytes,
                layout,
                bigEndian);
        }

        private static void PatchIntegerAttribute(
            XmxNode node,
            string attributeName,
            int value,
            byte[] packedData,
            PackedArray variantData,
            bool bigEndian)
        {
            for (uint i = 0;
                 i < node.Attributes.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            node.Attributes.Offset +
                            ((ulong)i *
                             XmxAttributeSize)));

                uint nameVariant =
                    ReadUInt32(
                        packedData,
                        p,
                        bigEndian);

                string name =
                    DecodeVariant(
                        nameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                if (!string.Equals(
                        name,
                        attributeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int valueVariantOffset =
                    p + 4;

                uint variant =
                    ReadUInt32(
                        packedData,
                        valueVariantOffset,
                        bigEndian);

                uint typeBits =
                    variant >>
                    24;

                int type =
                    (int)(
                        typeBits &
                        0x0F);

                bool isOffset =
                    (variant &
                     0x80000000u) != 0;

                uint payload =
                    variant &
                    0x00FFFFFFu;

                // -------------------------------------------------
                // Type 3 = signed Int24 stored inline.
                // -------------------------------------------------

                if (type == 3 &&
                    !isOffset)
                {
                    if (value <
                            -8388608 ||
                        value >
                            8388607)
                    {
                        throw new InvalidDataException(
                            $"Value {value} cannot be represented " +
                            "as an XMX Int24.");
                    }

                    uint encoded =
                        unchecked(
                            (uint)value) &
                        0x00FFFFFFu;

                    uint newVariant =
                        (variant &
                         0xFF000000u) |
                        encoded;

                    WriteVariantUInt32(
                        packedData,
                        valueVariantOffset,
                        newVariant,
                        bigEndian);

                    return;
                }

                // -------------------------------------------------
                // Type 4 = external signed Int32.
                // -------------------------------------------------

                if (type == 4 &&
                    isOffset)
                {
                    int dataOffset =
                        GetVariantOffset(
                            variantData,
                            payload,
                            4);

                    WriteInt32(
                        packedData,
                        dataOffset,
                        value,
                        bigEndian);

                    return;
                }

                throw new InvalidDataException(
                    $"Scenario attribute '{attributeName}' " +
                    $"uses unsupported XMX integer variant " +
                    $"type {type}.");
            }

            throw new InvalidDataException(
                $"Scenario node does not contain attribute " +
                $"'{attributeName}'.");
        }

        private static uint CloneStructuralSubtree(
    List<StructuralNode> nodes,
    int sourceIndex,
    uint newParent,
    byte[] originalData,
    PackedArray originalVariantData,
    List<byte> variantBytes,
    bool bigEndian)
        {
            StructuralNode source =
                nodes[sourceIndex];

            StructuralNode clone =
                new StructuralNode
                {
                    Parent =
                        newParent,

                    NameVariant =
                        source.NameVariant,

                    TextVariant =
                        source.TextVariant
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
                            attr.NameVariant,

                        ValueVariant =
                            CloneVariantPayload(
                                attr.ValueVariant,
                                originalData,
                                originalVariantData,
                                variantBytes,
                                bigEndian)
                    });
            }

            uint cloneIndex =
                checked(
                    (uint)nodes.Count);

            nodes.Add(
                clone);

            foreach (uint childIndex
                     in source.Children)
            {
                uint clonedChild =
                    CloneStructuralSubtree(
                        nodes,
                        checked(
                            (int)childIndex),
                        cloneIndex,
                        originalData,
                        originalVariantData,
                        variantBytes,
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
    List<byte> variantBytes,
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
                 0x80) != 0;

            if (!isOffset)
            {
                return variant;
            }

            uint relativeOffset =
                variant &
                0x00FFFFFFu;

            int sourceOffset =
                GetVariantOffset(
                    originalVariantData,
                    relativeOffset,
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
                                "Unterminated XMX string while cloning.");
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

                        while (end + 1 <
                               limit)
                        {
                            ushort value =
                                ReadUInt16(
                                    originalData,
                                    end,
                                    bigEndian);

                            end += 2;

                            if (value == 0)
                                break;
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
                        $"Cannot structurally clone offset XMX " +
                        $"variant type {type}.");
            }

            AlignByteList(
                variantBytes,
                alignment);

            int newOffset =
                variantBytes.Count;

            if (newOffset >
                0x00FFFFFF)
            {
                throw new InvalidDataException(
                    "XMX variant pool exceeded its 24-bit limit.");
            }

            for (int i = 0;
                 i < length;
                 i++)
            {
                variantBytes.Add(
                    originalData[
                        sourceOffset + i]);
            }

            return
                (variant &
                 0xFF000000u)
                |
                ((uint)newOffset &
                 0x00FFFFFFu);
        }

        private static void CollectStructuralSubtreeIndices(
            IReadOnlyList<StructuralNode> nodes,
            int rootIndex,
            HashSet<int> result)
        {
            Stack<int> pending =
                new Stack<int>();

            pending.Push(
                rootIndex);

            while (pending.Count >
                   0)
            {
                int index =
                    pending.Pop();

                if (index < 0 ||
                    index >= nodes.Count)
                {
                    throw new InvalidDataException(
                        $"Structural XMX contains invalid node {index}.");
                }

                if (!result.Add(
                        index))
                {
                    continue;
                }

                foreach (uint child
                         in nodes[index].Children)
                {
                    if (child >
                        int.MaxValue)
                    {
                        throw new InvalidDataException(
                            "XMX child index exceeds supported range.");
                    }

                    pending.Push(
                        checked(
                            (int)child));
                }
            }
        }

        private static List<StructuralNode>
    CompactStructuralNodes(
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
                new List<StructuralNode>(
                    sourceNodes.Count -
                    removedNodes.Count);

            // ---------------------------------------------------------
            // First pass:
            // assign the new node indices.
            // ---------------------------------------------------------

            for (int oldIndex = 0;
                 oldIndex < sourceNodes.Count;
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

            // ---------------------------------------------------------
            // Second pass:
            // remap Parent and Children indices.
            // ---------------------------------------------------------

            for (int oldIndex = 0;
                 oldIndex < sourceNodes.Count;
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

                // Parent
                if (node.Parent !=
                    uint.MaxValue)
                {
                    int oldParent =
                        checked(
                            (int)node.Parent);

                    if (oldParent < 0 ||
                        oldParent >=
                            remap.Length)
                    {
                        throw new InvalidDataException(
                            "XMX node references an invalid parent.");
                    }

                    int newParent =
                        remap[oldParent];

                    if (newParent <
                        0)
                    {
                        throw new InvalidDataException(
                            "A retained XMX node has a deleted parent.");
                    }

                    node.Parent =
                        checked(
                            (uint)newParent);
                }

                // Children
                List<uint> remappedChildren =
                    new List<uint>();

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
                            "XMX node references an invalid child.");
                    }

                    int newChild =
                        remap[oldChild];

                    // Child belongs to a deleted subtree.
                    if (newChild <
                        0)
                    {
                        continue;
                    }

                    remappedChildren.Add(
                        checked(
                            (uint)newChild));
                }

                node.Children.Clear();

                node.Children.AddRange(
                    remappedChildren);
            }

            return result;
        }

        private static void SetStructuralIntegerAttribute(
    StructuralNode node,
    string attributeName,
    int value,
    List<byte> variantBytes,
    bool bigEndian)
        {
            StructuralAttribute? attr =
                node.Attributes
                    .FirstOrDefault(
                        x =>
                            x.Name ==
                            attributeName);

            if (attr == null)
            {
                throw new InvalidDataException(
                    $"Cloned object contains no " +
                    $"'{attributeName}' attribute.");
            }

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
                 0x80) != 0;

            if (type == 3 &&
                !isOffset)
            {
                if (value <
                        -8388608 ||
                    value >
                        8388607)
                {
                    throw new InvalidDataException(
                        $"Object ID {value} does not fit Int24.");
                }

                attr.ValueVariant =
                    (variant &
                     0xFF000000u)
                    |
                    (unchecked(
                        (uint)value)
                     &
                     0x00FFFFFFu);

                return;
            }

            if (type == 4 &&
                isOffset)
            {
                int relativeOffset =
                    checked(
                        (int)(
                            variant &
                            0x00FFFFFFu));

                if (relativeOffset >
                    variantBytes.Count - 4)
                {
                    throw new InvalidDataException(
                        "Cloned Int32 variant points outside pool.");
                }

                byte[] temp =
                    new byte[4];

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
                    variantBytes[
                        relativeOffset + i] =
                            temp[i];
                }

                return;
            }

            throw new InvalidDataException(
                $"Object ID uses unsupported XMX " +
                $"variant type {type}.");
        }

        private static byte[] BuildStructuralPackedXmx(
    IReadOnlyList<StructuralNode> nodes,
    IReadOnlyList<byte> variantBytes,
    PackedLayout layout,
    bool bigEndian)
        {
            int alignment =
                layout.PointerSize == 8
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

            int totalSize =
                checked(
                    variantOffset +
                    variantBytes.Count);

            byte[] result =
                new byte[
                    totalSize];

            WriteVariantUInt32(
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

                WriteVariantUInt32(
                    result,
                    p +
                    layout.NodeParentOffset,
                    node.Parent,
                    bigEndian);

                WriteVariantUInt32(
                    result,
                    p +
                    layout.NodeNameOffset,
                    node.NameVariant,
                    bigEndian);

                WriteVariantUInt32(
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
                    node.Attributes.Count == 0
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
                    node.Children.Count == 0
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

                    WriteVariantUInt32(
                        result,
                        ap,
                        node.Attributes[a]
                            .NameVariant,
                        bigEndian);

                    WriteVariantUInt32(
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
                    WriteVariantUInt32(
                        result,
                        childOffsets[i] +
                        c * 4,
                        node.Children[c],
                        bigEndian);
                }
            }

            for (int i = 0;
                 i < variantBytes.Count;
                 i++)
            {
                result[
                    variantOffset + i] =
                        variantBytes[i];
            }

            return result;
        }

        private static int AlignValue(
            int value,
            int alignment)
        {
            int remainder =
                value %
                alignment;

            return remainder == 0
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
            while ((bytes.Count %
                    alignment) != 0)
            {
                bytes.Add(
                    0);
            }
        }

        private static void WritePackedArrayHeader(
    byte[] data,
    int offset,
    uint count,
    ulong pointer,
    int pointerSize,
    bool bigEndian)
        {
            WriteVariantUInt32(
                data,
                offset,
                count,
                bigEndian);

            if (pointerSize == 8)
            {
                WriteUInt64Value(
                    data,
                    offset + 8,
                    count == 0
                        ? ulong.MaxValue
                        : pointer,
                    bigEndian);
            }
            else
            {
                WriteVariantUInt32(
                    data,
                    offset + 4,
                    count == 0
                        ? uint.MaxValue
                        : checked(
                            (uint)pointer),
                    bigEndian);
            }
        }

        private static void WriteUInt64Value(
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

        private static void WriteInt32(
    byte[] data,
    int offset,
    int value,
    bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                4);

            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4),
                        value);
            }
            else
            {
                BinaryPrimitives
                    .WriteInt32LittleEndian(
                        data.AsSpan(
                            offset,
                            4),
                        value);
            }
        }

        private static string GetParentNodeName(
            XmxNode node,
            IReadOnlyList<XmxNode> nodes,
            byte[] packedData,
            PackedArray variantData,
            bool bigEndian)
        {
            if (node.Parent ==
                uint.MaxValue)
            {
                return string.Empty;
            }

            if (node.Parent >=
                nodes.Count)
            {
                return string.Empty;
            }

            XmxNode parent =
                nodes[
                    checked(
                        (int)node.Parent)];

            return DecodeVariant(
                parent.NameVariant,
                packedData,
                variantData,
                bigEndian);
        }

        private static bool TryGetIntegerAttribute(
    XmxNode node,
    string attributeName,
    byte[] packedData,
    PackedArray variantData,
    bool bigEndian,
    out int value)
        {
            value =
                0;

            if (!TryGetAttributeText(
                    node,
                    attributeName,
                    packedData,
                    variantData,
                    bigEndian,
                    out string text))
            {
                return false;
            }

            return int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool TryGetAttributeText(
    XmxNode node,
    string attributeName,
    byte[] packedData,
    PackedArray variantData,
    bool bigEndian,
    out string value)
        {
            value =
                string.Empty;

            for (uint i = 0;
                 i < node.Attributes.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            node.Attributes.Offset +
                            ((ulong)i *
                             XmxAttributeSize)));

                uint nameVariant =
                    ReadUInt32(
                        packedData,
                        p,
                        bigEndian);

                string name =
                    DecodeVariant(
                        nameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                if (!string.Equals(
                        name,
                        attributeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                uint valueVariant =
                    ReadUInt32(
                        packedData,
                        p + 4,
                        bigEndian);

                value =
                    DecodeVariant(
                        valueVariant,
                        packedData,
                        variantData,
                        bigEndian);

                return true;
            }

            return false;
        }

        private static void PatchVectorAttribute(
            XmxNode node,
            string attributeName,
            System.Numerics.Vector3 value,
            byte[] packedData,
            PackedArray variantData,
            bool bigEndian)
        {
            for (uint i = 0;
                 i < node.Attributes.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            node.Attributes.Offset +
                            ((ulong)i *
                             XmxAttributeSize)));

                uint nameVariant =
                    ReadUInt32(
                        packedData,
                        p,
                        bigEndian);

                string name =
                    DecodeVariant(
                        nameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                if (!string.Equals(
                        name,
                        attributeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                uint variant =
                    ReadUInt32(
                        packedData,
                        p + 4,
                        bigEndian);

                uint typeBits =
                    variant >>
                    24;

                int type =
                    (int)(
                        typeBits &
                        0x0F);

                bool isOffset =
                    (typeBits &
                     0x80) != 0;

                int vectorSize =
                    1 +
                    (int)(
                        (typeBits &
                         0x30) >>
                        4);

                // cXMXVTFloatVec == 10.
                if (type != 10 ||
                    !isOffset ||
                    vectorSize != 3)
                {
                    throw new InvalidDataException(
                        $"Scenario attribute '{attributeName}' " +
                        "is not stored as a 3-component XMX float vector.");
                }

                uint relativeOffset =
                    variant &
                    0x00FFFFFF;

                int dataOffset =
                    GetVariantOffset(
                        variantData,
                        relativeOffset,
                        12);

                WriteSingle(
                    packedData,
                    dataOffset,
                    value.X,
                    bigEndian);

                WriteSingle(
                    packedData,
                    dataOffset + 4,
                    value.Y,
                    bigEndian);

                WriteSingle(
                    packedData,
                    dataOffset + 8,
                    value.Z,
                    bigEndian);

                return;
            }

            throw new InvalidDataException(
                $"Scenario node does not contain attribute '{attributeName}'.");
        }

        private static void PatchFloatAttribute(
    XmxNode node,
    string attributeName,
    float value,
    byte[] packedData,
    PackedArray variantData,
    bool bigEndian)
        {
            for (uint i = 0;
                 i < node.Attributes.Count;
                 i++)
            {
                int p =
                    checked(
                        (int)(
                            node.Attributes.Offset +
                            ((ulong)i *
                             XmxAttributeSize)));

                uint nameVariant =
                    ReadUInt32(
                        packedData,
                        p,
                        bigEndian);

                string name =
                    DecodeVariant(
                        nameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                if (!string.Equals(
                        name,
                        attributeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int valueVariantOffset =
                    p + 4;

                uint variant =
                    ReadUInt32(
                        packedData,
                        valueVariantOffset,
                        bigEndian);

                uint typeBits =
                    variant >>
                    24;

                int type =
                    (int)(
                        typeBits &
                        0x0F);

                bool isOffset =
                    (variant &
                     0x80000000u) != 0;

                uint payload =
                    variant &
                    0x00FFFFFFu;

                // =====================================================
                // Type 2:
                // Full 32-bit Single stored indirectly in variant pool.
                // =====================================================

                if (type == 2 &&
                    isOffset)
                {
                    int dataOffset =
                        GetVariantOffset(
                            variantData,
                            payload,
                            4);

                    WriteSingle(
                        packedData,
                        dataOffset,
                        value,
                        bigEndian);

                    return;
                }

                // =====================================================
                // Type 3:
                // Int24 stored directly inside the 32-bit variant.
                //
                // Blood Gulch design sphere Radius values use this.
                //
                // If the new value is still a whole number, preserve
                // the original Int24 representation exactly.
                // =====================================================

                if (type == 3 &&
                    IsWholeNumber(value) &&
                    value >= 0 &&
                    value <= 0x007FFFFF)
                {
                    uint integerValue =
                        checked(
                            (uint)value);

                    uint newVariant =
                        (variant &
                         0xFF000000u) |
                        (integerValue &
                         0x007FFFFFu);

                    WriteVariantUInt32(
                        packedData,
                        valueVariantOffset,
                        newVariant,
                        bigEndian);

                    return;
                }

                // =====================================================
                // Inline numeric value which can no longer be preserved
                // using its original representation.
                //
                // Convert it to XMX Single24.
                //
                // Type 1 = Single24 (S1E6M17)
                //
                // This changes only this four-byte variant. No memory
                // pool resize or XMX structural rebuild is required.
                // =====================================================

                if (type == 1 ||
                    type == 3 ||
                    type == 5)
                {
                    uint single24 =
                        EncodeSingle24(
                            value);

                    uint newVariant =
                        (1u << 24) |
                        (single24 &
                         0x00FFFFFFu);

                    WriteVariantUInt32(
                        packedData,
                        valueVariantOffset,
                        newVariant,
                        bigEndian);

                    return;
                }

                throw new InvalidDataException(
                    $"Scenario attribute '{attributeName}' " +
                    $"uses unsupported XMX variant type {type}. " +
                    "Ensemble will not modify it unsafely.");
            }

            throw new InvalidDataException(
                $"Scenario node does not contain attribute " +
                $"'{attributeName}'.");
        }

        private static bool IsWholeNumber(
            float value)
        {
            return MathF.Abs(
                value -
                MathF.Round(value)) <
                0.000001f;
        }

        private static uint EncodeSingle24(
            float value)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                throw new InvalidDataException(
                    "XMX Single24 cannot encode NaN or Infinity.");
            }

            // KSoft Single24:
            //
            // 1 sign bit
            // 6 exponent bits
            // 17 mantissa bits
            //
            // S1E6M17

            const int single32MantissaBits =
                23;

            const uint single32MantissaMask =
                0x007FFFFFu;

            const int single32ExponentShift =
                23;

            const uint single32ExponentMask =
                0x7F800000u;

            const uint single32SignMask =
                0x80000000u;


            const int single24MantissaBits =
                17;

            const uint single24MantissaMask =
                0x0001FFFFu;

            const int single24ExponentShift =
                17;

            const uint single24ExponentMask =
                0x007E0000u;

            const uint single24SignBit =
                0x00800000u;


            const int single32ExponentBias =
                127;

            const int single24ExponentBias =
                31;

            const int exponentBiasDifference =
                single32ExponentBias -
                single24ExponentBias;

            const int mantissaBitDifference =
                single32MantissaBits -
                single24MantissaBits;


            uint bits =
                BitConverter.SingleToUInt32Bits(
                    value);

            uint mantissa =
                bits &
                single32MantissaMask;

            uint exponent =
                (bits &
                 single32ExponentMask) >>
                single32ExponentShift;

            bool negative =
                (bits &
                 single32SignMask) != 0;


            // Match KSoft's special zero/subnormal handling.
            if (exponent == 0)
            {
                return single24SignBit;
            }


            int compressedExponent =
                checked(
                    (int)exponent -
                    exponentBiasDifference);

            if (compressedExponent <= 0 ||
                compressedExponent >= 64)
            {
                throw new InvalidDataException(
                    $"Value {value} cannot be represented " +
                    "safely as an XMX Single24.");
            }


            uint compressedMantissa =
                mantissa >>
                mantissaBitDifference;

            compressedMantissa &=
                single24MantissaMask;


            uint result =
                ((uint)compressedExponent <<
                 single24ExponentShift) &
                single24ExponentMask;

            result |=
                compressedMantissa;

            if (negative)
            {
                result |=
                    single24SignBit;
            }


            return
                result &
                0x00FFFFFFu;
        }

        private static void WriteVariantUInt32(
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

        private static void WriteSingle(
            byte[] data,
            int offset,
            float value,
            bool bigEndian)
        {
            EnsureRange(
                data,
                offset,
                4);

            uint bits =
                unchecked(
                    (uint)BitConverter
                        .SingleToInt32Bits(
                            value));

            if (bigEndian)
            {
                BinaryPrimitives
                    .WriteUInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4),
                        bits);
            }
            else
            {
                BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        data.AsSpan(
                            offset,
                            4),
                        bits);
            }
        }

        // =========================================================
        // 32-BIT / 64-BIT FORMAT DETECTION
        // =========================================================

        private static PackedLayout DetectPackedLayout(
            byte[] data,
            bool bigEndian)
        {
            // Original Halo Wars:
            //
            // uint signature
            // BPackedArray nodes
            // BPackedArray variantData
            //
            // BPackedArray contains:
            //
            // uint size
            // T* pointer
            //
            // Therefore the serialized structure differs depending
            // on whether the tool was compiled as 32-bit or 64-bit.

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

            string firstBytes =
                Convert.ToHexString(
                    data.AsSpan(
                        0,
                        Math.Min(
                            data.Length,
                            64)));

            throw new InvalidDataException(
                "Unable to determine the packed XMX structure layout.\n\n" +
                "Neither the original 32-bit layout nor the " +
                "64-bit Definitive Edition layout produced a " +
                "valid node table.\n\n" +
                $"First bytes:\n{firstBytes}");
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

                PackedArray variantData =
                    ReadPackedArray(
                        data,
                        layout.VariantArrayOffset,
                        bigEndian,
                        layout.PointerSize);

                if (nodes.Count == 0 ||
                    nodes.Count >=
                        100_000_000)
                {
                    return false;
                }

                if (variantData.Count >
                    0x00FFFFFF)
                {
                    return false;
                }

                if (!IsArrayInsideBuffer(
                        data,
                        nodes,
                        layout.NodeSize))
                {
                    return false;
                }

                if (!IsArrayInsideBuffer(
                        data,
                        variantData,
                        1))
                {
                    return false;
                }

                // Validate the first node enough to eliminate
                // false positives.

                int firstNodeOffset =
                    checked(
                        (int)nodes.Offset);

                uint nameVariant =
                    ReadUInt32(
                        data,
                        firstNodeOffset +
                        layout.NodeNameOffset,
                        bigEndian);

                int nameType =
                    (int)(
                        (nameVariant >> 24) &
                        0x0F);

                // Element names are stored as strings.
                if (nameType != 8 &&
                    nameType != 0)
                {
                    return false;
                }

                PackedArray attributes =
                    ReadPackedArray(
                        data,
                        firstNodeOffset +
                        layout.NodeAttributesOffset,
                        bigEndian,
                        layout.PointerSize);

                PackedArray children =
                    ReadPackedArray(
                        data,
                        firstNodeOffset +
                        layout.NodeChildrenOffset,
                        bigEndian,
                        layout.PointerSize);

                if (!IsArrayInsideBuffer(
                        data,
                        attributes,
                        XmxAttributeSize))
                {
                    return false;
                }

                if (!IsArrayInsideBuffer(
                        data,
                        children,
                        4))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =========================================================
        // NODES
        // =========================================================

        private static XmxNode ParseNode(
            byte[] data,
            int offset,
            PackedLayout layout,
            bool bigEndian)
        {
            uint parent =
                ReadUInt32(
                    data,
                    offset +
                    layout.NodeParentOffset,
                    bigEndian);

            uint nameVariant =
                ReadUInt32(
                    data,
                    offset +
                    layout.NodeNameOffset,
                    bigEndian);

            uint textVariant =
                ReadUInt32(
                    data,
                    offset +
                    layout.NodeTextOffset,
                    bigEndian);

            PackedArray attributes =
                ReadPackedArray(
                    data,
                    offset +
                    layout.NodeAttributesOffset,
                    bigEndian,
                    layout.PointerSize);

            PackedArray children =
                ReadPackedArray(
                    data,
                    offset +
                    layout.NodeChildrenOffset,
                    bigEndian,
                    layout.PointerSize);

            ValidateArray(
                data,
                attributes,
                XmxAttributeSize,
                "attribute");

            ValidateArray(
                data,
                children,
                4,
                "child-index");

            return new XmxNode
            {
                Parent =
                    parent,

                NameVariant =
                    nameVariant,

                TextVariant =
                    textVariant,

                Attributes =
                    attributes,

                Children =
                    children
            };
        }

        private static void RenderNode(
    StringBuilder output,
    int nodeIndex,
    IReadOnlyList<XmxNode> nodes,
    byte[] packedData,
    PackedArray variantData,
    bool bigEndian,
    HashSet<int> recursionStack,
    int indent)
        {
            if (nodeIndex < 0 ||
                nodeIndex >= nodes.Count)
            {
                throw new InvalidDataException(
                    $"XMX references invalid node {nodeIndex}.");
            }

            if (!recursionStack.Add(
                    nodeIndex))
            {
                throw new InvalidDataException(
                    "XMX contains a recursive node cycle.");
            }

            try
            {
                XmxNode node =
                    nodes[nodeIndex];

                string nodeName =
                    DecodeVariant(
                        node.NameVariant,
                        packedData,
                        variantData,
                        bigEndian);

                if (string.IsNullOrWhiteSpace(
                        nodeName))
                {
                    throw new InvalidDataException(
                        $"Node {nodeIndex} has an empty name.");
                }

                string indentation =
                    new string(
                        ' ',
                        indent * 2);

                output.Append(
                    indentation);

                output.Append('<');

                // IMPORTANT:
                // Do NOT pass this through XElement/XName.
                // Halo Wars stores and outputs prefixed names directly.
                output.Append(
                    nodeName);

                // =========================================================
                // Attributes
                // =========================================================

                for (uint i = 0;
                     i < node.Attributes.Count;
                     i++)
                {
                    int attrOffset =
                        checked(
                            (int)(
                                node.Attributes.Offset +
                                ((ulong)i *
                                 XmxAttributeSize)));

                    uint nameVariant =
                        ReadUInt32(
                            packedData,
                            attrOffset,
                            bigEndian);

                    uint valueVariant =
                        ReadUInt32(
                            packedData,
                            attrOffset + 4,
                            bigEndian);

                    string attrName =
                        DecodeVariant(
                            nameVariant,
                            packedData,
                            variantData,
                            bigEndian);

                    string attrValue =
                        DecodeVariant(
                            valueVariant,
                            packedData,
                            variantData,
                            bigEndian);

                    if (string.IsNullOrEmpty(
                            attrName))
                    {
                        continue;
                    }

                    output.Append(' ');

                    // Preserve the exact Halo Wars attribute name.
                    // This allows names such as xsi:type.
                    output.Append(
                        attrName);

                    output.Append(
                        "=\"");

                    output.Append(
                        EscapeXmlAttribute(
                            attrValue));

                    output.Append('"');
                }

                string text =
                    DecodeVariant(
                        node.TextVariant,
                        packedData,
                        variantData,
                        bigEndian);

                bool hasText =
                    !string.IsNullOrEmpty(
                        text);

                bool hasChildren =
                    node.Children.Count > 0;

                // =========================================================
                // Empty node
                // =========================================================

                if (!hasText &&
                    !hasChildren)
                {
                    output.AppendLine(
                        " />");

                    return;
                }

                // =========================================================
                // Text-only node
                // =========================================================

                if (!hasChildren)
                {
                    output.Append('>');

                    output.Append(
                        EscapeXmlText(
                            text));

                    output.Append(
                        "</");

                    output.Append(
                        nodeName);

                    output.AppendLine(
                        ">");

                    return;
                }

                // =========================================================
                // Node with children
                // =========================================================

                output.Append('>');

                if (hasText)
                {
                    output.Append(
                        EscapeXmlText(
                            text));
                }

                output.AppendLine();

                for (uint i = 0;
                     i < node.Children.Count;
                     i++)
                {
                    int childOffset =
                        checked(
                            (int)(
                                node.Children.Offset +
                                ((ulong)i * 4)));

                    uint childIndex =
                        ReadUInt32(
                            packedData,
                            childOffset,
                            bigEndian);

                    if (childIndex >=
                        nodes.Count)
                    {
                        throw new InvalidDataException(
                            $"Node {nodeIndex} references " +
                            $"invalid child {childIndex}.");
                    }

                    RenderNode(
                        output,
                        checked(
                            (int)childIndex),
                        nodes,
                        packedData,
                        variantData,
                        bigEndian,
                        recursionStack,
                        indent + 1);
                }

                output.Append(
                    indentation);

                output.Append(
                    "</");

                output.Append(
                    nodeName);

                output.AppendLine(
                    ">");
            }
            finally
            {
                recursionStack.Remove(
                    nodeIndex);
            }
        }

        private static string EscapeXmlText(
    string value)
        {
            if (string.IsNullOrEmpty(
                    value))
            {
                return string.Empty;
            }

            return value
                .Replace(
                    "&",
                    "&amp;")
                .Replace(
                    "<",
                    "&lt;")
                .Replace(
                    ">",
                    "&gt;");
        }

        private static string EscapeXmlAttribute(
            string value)
        {
            if (string.IsNullOrEmpty(
                    value))
            {
                return string.Empty;
            }

            return value
                .Replace(
                    "&",
                    "&amp;")
                .Replace(
                    "\"",
                    "&quot;")
                .Replace(
                    "<",
                    "&lt;")
                .Replace(
                    ">",
                    "&gt;");
        }

        // =========================================================
        // VARIANTS
        // =========================================================

        private static string DecodeVariant(
            uint value,
            byte[] packedData,
            PackedArray variantData,
            bool bigEndian)
        {
            uint typeBits =
                value >> 24;

            uint bits =
                value &
                0x00FFFFFF;

            int type =
                (int)(
                    typeBits &
                    0x0F);

            bool isOffset =
                (typeBits &
                 0x80) != 0;

            bool isUnsigned =
                (typeBits &
                 0x40) != 0;

            int vectorSize =
                1 +
                (int)(
                    (typeBits &
                     0x30) >> 4);

            switch (type)
            {
                // Null
                case 0:
                    return string.Empty;

                // Float24
                case 1:
                    {
                        float f =
                            DecodeFloat24(
                                bits);

                        return
                            f.ToString(
                                "G6",
                                CultureInfo.InvariantCulture);
                    }

                // Float32
                case 2:
                    {
                        RequireOffset(
                            isOffset,
                            type);

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                4);

                        float f =
                            ReadSingle(
                                packedData,
                                p,
                                bigEndian);

                        return
                            f.ToString(
                                "G8",
                                CultureInfo.InvariantCulture);
                    }

                // Int24
                case 3:
                    {
                        if (isUnsigned)
                        {
                            return
                                bits.ToString(
                                    CultureInfo.InvariantCulture);
                        }

                        int signed =
                            (int)bits;

                        if ((signed &
                             0x00800000) != 0)
                        {
                            signed |=
                                unchecked(
                                    (int)0xFF000000);
                        }

                        return
                            signed.ToString(
                                CultureInfo.InvariantCulture);
                    }

                // Int32
                case 4:
                    {
                        RequireOffset(
                            isOffset,
                            type);

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                4);

                        if (isUnsigned)
                        {
                            uint number =
                                ReadUInt32(
                                    packedData,
                                    p,
                                    bigEndian);

                            return
                                number.ToString(
                                    CultureInfo.InvariantCulture);
                        }

                        int signedNumber =
                            unchecked(
                                (int)ReadUInt32(
                                    packedData,
                                    p,
                                    bigEndian));

                        return
                            signedNumber.ToString(
                                CultureInfo.InvariantCulture);
                    }

                // Fixed-point decimal
                case 5:
                    {
                        bool negative =
                            (bits &
                             0x00800000) != 0;

                        uint raw =
                            bits &
                            0x007FFFFF;

                        uint integer =
                            raw / 10000;

                        uint fraction =
                            raw % 10000;

                        return
                            string.Format(
                                CultureInfo.InvariantCulture,
                                negative
                                    ? "-{0}.{1:0000}"
                                    : "{0}.{1:0000}",
                                integer,
                                fraction);
                    }

                // Double
                case 6:
                    {
                        RequireOffset(
                            isOffset,
                            type);

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                8);

                        double d =
                            ReadDouble(
                                packedData,
                                p,
                                bigEndian);

                        return
                            d.ToString(
                                "G17",
                                CultureInfo.InvariantCulture);
                    }

                // Bool
                case 7:
                    {
                        if (bits == 0)
                            return "false";

                        if (bits == 1)
                            return "true";

                        throw new InvalidDataException(
                            $"Invalid XMX boolean value {bits}.");
                    }

                // ANSI string
                case 8:
                    {
                        if (!isOffset)
                        {
                            char c0 =
                                (char)(
                                    bits &
                                    0xFF);

                            char c1 =
                                (char)(
                                    (bits >> 8) &
                                    0xFF);

                            char c2 =
                                (char)(
                                    (bits >> 16) &
                                    0xFF);

                            StringBuilder direct =
                                new StringBuilder(
                                    3);

                            if (c0 != '\0')
                                direct.Append(c0);

                            if (c1 != '\0')
                                direct.Append(c1);

                            if (c2 != '\0')
                                direct.Append(c2);

                            return
                                direct.ToString();
                        }

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                1);

                        return
                            ReadNullTerminatedAscii(
                                packedData,
                                p,
                                GetVariantDataEnd(
                                    variantData));
                    }

                // Unicode string
                case 9:
                    {
                        RequireOffset(
                            isOffset,
                            type);

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                2);

                        return
                            ReadNullTerminatedUnicode(
                                packedData,
                                p,
                                GetVariantDataEnd(
                                    variantData),
                                bigEndian);
                    }

                // Float vector
                case 10:
                    {
                        RequireOffset(
                            isOffset,
                            type);

                        int bytes =
                            checked(
                                vectorSize *
                                4);

                        int p =
                            GetVariantOffset(
                                variantData,
                                bits,
                                bytes);

                        string[] values =
                            new string[
                                vectorSize];

                        for (int i = 0;
                             i < vectorSize;
                             i++)
                        {
                            float f =
                                ReadSingle(
                                    packedData,
                                    p +
                                    (i * 4),
                                    bigEndian);

                            values[i] =
                                f.ToString(
                                    "G8",
                                    CultureInfo.InvariantCulture);
                        }

                        return
                            string.Join(
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

            if (exponentBits == 0)
            {
                return sign != 0
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

            uint bits =
                (sign != 0
                    ? 0x80000000u
                    : 0u)
                |
                (newExponent << 23)
                |
                (mantissa << 6);

            return
                BitConverter
                    .Int32BitsToSingle(
                        unchecked(
                            (int)bits));
        }

        // =========================================================
        // PACKED ARRAY HELPERS
        // =========================================================

        private static PackedArray ReadPackedArray(
            byte[] data,
            int offset,
            bool bigEndian,
            int pointerSize)
        {
            int pointerFieldOffset =
                pointerSize == 8
                    ? 8
                    : 4;

            int structureSize =
                pointerSize == 8
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

            ulong pointer;

            if (pointerSize == 8)
            {
                pointer =
                    ReadUInt64(
                        data,
                        offset +
                        pointerFieldOffset,
                        bigEndian);
            }
            else
            {
                pointer =
                    ReadUInt32(
                        data,
                        offset +
                        pointerFieldOffset,
                        bigEndian);
            }

            if (count == 0)
            {
                return
                    new PackedArray(
                        0,
                        NormalizeNullPointer(
                            pointer));
            }

            if (IsNullPackedPointer(
                    pointer))
            {
                throw new InvalidDataException(
                    "Packed XMX array has elements but no data pointer.");
            }

            return
                new PackedArray(
                    count,
                    pointer);
        }

        private static bool IsNullPackedPointer(
            ulong pointer)
        {
            return
                pointer ==
                    0x00000000FFFFFFFFUL ||
                pointer ==
                    ulong.MaxValue;
        }

        private static ulong NormalizeNullPointer(
            ulong pointer)
        {
            if (IsNullPackedPointer(
                    pointer))
            {
                return 0;
            }

            return pointer;
        }

        private static void ValidateArray(
            byte[] data,
            PackedArray array,
            int elementSize,
            string description)
        {
            if (!IsArrayInsideBuffer(
                    data,
                    array,
                    elementSize))
            {
                throw new InvalidDataException(
                    $"Packed XMX {description} array " +
                    $"points outside the data buffer.");
            }
        }

        private static bool IsArrayInsideBuffer(
            byte[] data,
            PackedArray array,
            int elementSize)
        {
            if (array.Count == 0)
            {
                return true;
            }

            if (array.Offset >=
                (ulong)data.Length)
            {
                return false;
            }

            ulong bytes =
                (ulong)array.Count *
                (ulong)elementSize;

            ulong end =
                array.Offset +
                bytes;

            if (end <
                array.Offset)
            {
                return false;
            }

            return end <=
                (ulong)data.Length;
        }

        private static void ValidateByteArray(
            byte[] data,
            PackedArray array,
            string description)
        {
            ValidateArray(
                data,
                array,
                1,
                description);
        }

        private static int GetVariantOffset(
            PackedArray variantData,
            uint relativeOffset,
            int requiredBytes)
        {
            ulong relativeEnd =
                (ulong)relativeOffset +
                (ulong)requiredBytes;

            if (relativeEnd >
                variantData.Count)
            {
                throw new InvalidDataException(
                    "XMX variant points outside the variant-data array.");
            }

            ulong absolute =
                variantData.Offset +
                relativeOffset;

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
            PackedArray variantData)
        {
            ulong end =
                variantData.Offset +
                variantData.Count;

            if (end >
                int.MaxValue)
            {
                throw new InvalidDataException(
                    "XMX variant data is too large.");
            }

            return checked(
                (int)end);
        }

        private static void RequireOffset(
            bool isOffset,
            int type)
        {
            if (!isOffset)
            {
                throw new InvalidDataException(
                    $"XMX variant type {type} must use an offset.");
            }
        }

        // =========================================================
        // STRING HELPERS
        // =========================================================

        private static string ReadNullTerminatedAscii(
            byte[] data,
            int start,
            int limit)
        {
            if (start < 0 ||
                start >= limit ||
                limit >
                    data.Length)
            {
                throw new InvalidDataException(
                    "Invalid XMX ANSI string offset.");
            }

            int end =
                start;

            while (end <
                       limit &&
                   data[end] !=
                       0)
            {
                end++;
            }

            if (end >=
                limit)
            {
                throw new InvalidDataException(
                    "Unterminated XMX ANSI string.");
            }

            return
                Encoding.ASCII.GetString(
                    data,
                    start,
                    end -
                    start);
        }

        private static string ReadNullTerminatedUnicode(
            byte[] data,
            int start,
            int limit,
            bool bigEndian)
        {
            if ((start & 1) !=
                0)
            {
                throw new InvalidDataException(
                    "XMX Unicode string is not 2-byte aligned.");
            }

            StringBuilder result =
                new StringBuilder();

            int p =
                start;

            while (p + 1 <
                   limit)
            {
                ushort value =
                    ReadUInt16(
                        data,
                        p,
                        bigEndian);

                p += 2;

                if (value == 0)
                {
                    return
                        result.ToString();
                }

                result.Append(
                    (char)value);
            }

            throw new InvalidDataException(
                "Unterminated XMX Unicode string.");
        }

        // =========================================================
        // BINARY HELPERS
        // =========================================================

        private static uint ReadUInt32BigEndian(
            byte[] data,
            int offset)
        {
            EnsureRange(
                data,
                offset,
                4);

            return
                BinaryPrimitives
                    .ReadUInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4));
        }

        private static ushort ReadUInt16BigEndian(
            byte[] data,
            int offset)
        {
            EnsureRange(
                data,
                offset,
                2);

            return
                BinaryPrimitives
                    .ReadUInt16BigEndian(
                        data.AsSpan(
                            offset,
                            2));
        }

        private static ulong ReadUInt64BigEndian(
            byte[] data,
            int offset)
        {
            EnsureRange(
                data,
                offset,
                8);

            return
                BinaryPrimitives
                    .ReadUInt64BigEndian(
                        data.AsSpan(
                            offset,
                            8));
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

            return
                BitConverter
                    .Int32BitsToSingle(
                        unchecked(
                            (int)bits));
        }

        private static double ReadDouble(
            byte[] data,
            int offset,
            bool bigEndian)
        {
            ulong bits =
                ReadUInt64(
                    data,
                    offset,
                    bigEndian);

            return
                BitConverter
                    .Int64BitsToDouble(
                        unchecked(
                            (long)bits));
        }

        private static void EnsureRange(
            byte[] data,
            int offset,
            int size)
        {
            if (offset < 0 ||
                size < 0 ||
                (long)offset +
                    size >
                    data.Length)
            {
                throw new InvalidDataException(
                    "Attempted to read outside the XMB data buffer.");
            }
        }

        // =========================================================
        // INTERNAL TYPES
        // =========================================================

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
                // BXMXData:
                //
                // 00 uint sig
                // 04 BPackedArray nodes   (8)
                // 12 BPackedArray variant (8)
                //
                // BNode:
                //
                // 00 parent
                // 04 name
                // 08 text
                // 12 attributes (8)
                // 20 children   (8)
                //
                // total = 28

                return
                    new PackedLayout
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
                // 64-bit MSVC packing:
                //
                // BXMXData:
                //
                // 00 uint sig
                // 04 padding
                // 08 BPackedArray nodes   (16)
                // 24 BPackedArray variant (16)
                //
                // BNode:
                //
                // 00 parent
                // 04 name
                // 08 text
                // 12 padding
                // 16 attributes (16)
                // 32 children   (16)
                //
                // total = 48

                return
                    new PackedLayout
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