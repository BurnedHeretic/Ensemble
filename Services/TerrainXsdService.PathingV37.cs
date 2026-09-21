using Ensemble.Models;

namespace Ensemble.Services
{
    /// <summary>
    /// v37 land-pathing synchronization for edited terrain.
    ///
    /// Ensemble's original XSD writer already synchronizes SimHeights. Halo
    /// Wars also stores a per-data-tile obstruction byte map in XSD chunk
    /// 0x4444. The original Phoenix Editor derives land obstruction from the
    /// terrain's four corner heights using a 35 degree slope threshold.
    ///
    /// This extension reproduces that rule ONLY for tiles whose visual terrain
    /// actually changed, preserving hand-authored/special pathing elsewhere.
    /// Flood and Scarab obstruction components are retained.
    /// </summary>
    internal static partial class TerrainXsdService
    {
        private const ulong
            ObstructionsChunkIdV37 =
                0x4444;

        private const byte
            NoObstructionV37 =
                0;

        private const byte
            LandObstructionV37 =
                1;

        private const byte
            WaterHolderV37 =
                2;

        private const byte
            LandFloodScarabV37 =
                3;

        private const byte
            NonSolidV37 =
                4;

        private const byte
            FloodObstructionV37 =
                5;

        private const byte
            ScarabObstructionV37 =
                6;

        private const byte
            LandFloodV37 =
                7;

        private const byte
            LandScarabV37 =
                8;

        private const byte
            FloodScarabV37 =
                9;

        /// <summary>
        /// Returns the number of XSD data tiles whose obstruction byte changed.
        /// </summary>
        private static int SynchronizeSlopeDerivedLandObstructionsV37(
            byte[] xsdData,
            TerrainHeightMap originalVisualTerrain,
            TerrainHeightMap editedVisualTerrain)
        {
            ArgumentNullException.ThrowIfNull(
                xsdData);

            ArgumentNullException.ThrowIfNull(
                originalVisualTerrain);

            ArgumentNullException.ThrowIfNull(
                editedVisualTerrain);

            EcfChunkLocation headerChunk;

            EcfChunkLocation obstructionChunk;

            try
            {
                headerChunk =
                    FindEcfChunk(
                        xsdData,
                        HeaderChunkId);

                obstructionChunk =
                    FindEcfChunk(
                        xsdData,
                        ObstructionsChunkIdV37);
            }
            catch (InvalidDataException)
            {
                // A few unusual/exporter-era XSDs may omit optional data-tile
                // chunks. Height synchronization remains valid in that case.
                return 0;
            }

            int numTiles =
                ReadInt32BigEndian(
                    xsdData,
                    headerChunk.DataOffset +
                    4);

            float dataTileScale =
                ReadSingleBigEndian(
                    xsdData,
                    headerChunk.DataOffset +
                    8);

            if (numTiles <=
                    0 ||
                !float.IsFinite(
                    dataTileScale) ||
                dataTileScale <=
                    0)
            {
                return 0;
            }

            int required =
                checked(
                    numTiles *
                    numTiles);

            if (obstructionChunk.Size <
                required)
            {
                throw new InvalidDataException(
                    "XSD obstruction chunk is smaller than NumXDataTiles specifies.");
            }

            // Original Phoenix Editor:
            //   maxSlopeAngle = 35 degrees
            //   maxSlope = sin(maxSlopeAngle)
            //   slp = (highest - lowest) / mTileScale
            const float maxSlopeAngleDegrees =
                35.0f;

            float maxSlope =
                MathF.Sin(
                    maxSlopeAngleDegrees *
                    (MathF.PI /
                     180.0f));

            float changeTolerance =
                Math.Max(
                    0.0001f,
                    Math.Max(
                        originalVisualTerrain
                            .HeightQuantizationStep,
                        editedVisualTerrain
                            .HeightQuantizationStep)
                    *
                    0.51f);

            int changedTiles =
                0;

            for (int x = 0;
                 x < numTiles;
                 x++)
            {
                float u0 =
                    x /
                    (float)numTiles;

                float u1 =
                    (x + 1) /
                    (float)numTiles;

                for (int z = 0;
                     z < numTiles;
                     z++)
                {
                    float v0 =
                        z /
                        (float)numTiles;

                    float v1 =
                        (z + 1) /
                        (float)numTiles;

                    float o00 =
                        SampleTerrain(
                            originalVisualTerrain,
                            u0,
                            v0);

                    float o10 =
                        SampleTerrain(
                            originalVisualTerrain,
                            u1,
                            v0);

                    float o01 =
                        SampleTerrain(
                            originalVisualTerrain,
                            u0,
                            v1);

                    float o11 =
                        SampleTerrain(
                            originalVisualTerrain,
                            u1,
                            v1);

                    float e00 =
                        SampleTerrain(
                            editedVisualTerrain,
                            u0,
                            v0);

                    float e10 =
                        SampleTerrain(
                            editedVisualTerrain,
                            u1,
                            v0);

                    float e01 =
                        SampleTerrain(
                            editedVisualTerrain,
                            u0,
                            v1);

                    float e11 =
                        SampleTerrain(
                            editedVisualTerrain,
                            u1,
                            v1);

                    float greatestDelta =
                        Math.Max(
                            Math.Max(
                                MathF.Abs(
                                    e00 -
                                    o00),
                                MathF.Abs(
                                    e10 -
                                    o10)),
                            Math.Max(
                                MathF.Abs(
                                    e01 -
                                    o01),
                                MathF.Abs(
                                    e11 -
                                    o11)));

                    if (greatestDelta <=
                        changeTolerance)
                    {
                        continue;
                    }

                    float highest =
                        Math.Max(
                            Math.Max(
                                e00,
                                e10),
                            Math.Max(
                                e01,
                                e11));

                    float lowest =
                        Math.Min(
                            Math.Min(
                                e00,
                                e10),
                            Math.Min(
                                e01,
                                e11));

                    bool landObstructed =
                        (
                            highest -
                            lowest
                        ) /
                        dataTileScale
                        >
                        maxSlope;

                    // XSD exporter order is x-major:
                    // index = x * NumXDataTiles + z.
                    int index =
                        checked(
                            x *
                            numTiles +
                            z);

                    int dataOffset =
                        obstructionChunk
                            .DataOffset +
                        index;

                    byte originalType =
                        xsdData[
                            dataOffset];

                    // Preserve the two special non-combination enum values from
                    // the shipped engine/editor exactly as authored.
                    if (originalType
                            is WaterHolderV37 or
                               NonSolidV37)
                    {
                        continue;
                    }

                    bool flood =
                        originalType
                            is LandFloodScarabV37 or
                               FloodObstructionV37 or
                               LandFloodV37 or
                               FloodScarabV37;

                    bool scarab =
                        originalType
                            is LandFloodScarabV37 or
                               ScarabObstructionV37 or
                               LandScarabV37 or
                               FloodScarabV37;

                    byte updatedType =
                        ComposeObstructionTypeV37(
                            landObstructed,
                            flood,
                            scarab);

                    if (updatedType ==
                        originalType)
                    {
                        continue;
                    }

                    xsdData[
                        dataOffset] =
                            updatedType;

                    changedTiles++;
                }
            }

            if (changedTiles >
                0)
            {
                UpdateChunkAndHeaderChecksums(
                    xsdData,
                    obstructionChunk);
            }

            return changedTiles;
        }

        private static byte ComposeObstructionTypeV37(
            bool land,
            bool flood,
            bool scarab)
        {
            if (land &&
                flood &&
                scarab)
            {
                return LandFloodScarabV37;
            }

            if (land &&
                flood)
            {
                return LandFloodV37;
            }

            if (land &&
                scarab)
            {
                return LandScarabV37;
            }

            if (flood &&
                scarab)
            {
                return FloodScarabV37;
            }

            if (land)
                return LandObstructionV37;

            if (flood)
                return FloodObstructionV37;

            if (scarab)
                return ScarabObstructionV37;

            return NoObstructionV37;
        }
    }
}
