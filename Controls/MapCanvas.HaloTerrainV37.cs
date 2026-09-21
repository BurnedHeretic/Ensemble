using Ensemble.Models;

namespace Ensemble.Controls
{
    public sealed partial class MapCanvas
    {
        /// <summary>
        /// Applies an externally-generated terrain heightfield as one undoable
        /// terrain preview operation. SaveModifiedEraToPath already treats a
        /// non-empty terrain preview stack as persistent XTD/XSD work.
        /// </summary>
        public int ApplyImportedTerrainHeightsV37(
            IReadOnlyList<float> heights)
        {
            ArgumentNullException.ThrowIfNull(
                heights);

            if (_terrainHeightMap ==
                null)
            {
                throw new InvalidOperationException(
                    "No Halo Wars XTD terrain is loaded.");
            }

            if (heights.Count !=
                _terrainHeightMap.Heights.Length)
            {
                throw new InvalidDataException(
                    "Imported terrain height count does not match the loaded XTD.");
            }

            if (_isTerrainSculpting)
            {
                FinishTerrainStroke();
            }

            List<TerrainHeightChange> changes =
                new(
                    heights.Count);

            for (int i = 0;
                 i < heights.Count;
                 i++)
            {
                float before =
                    _terrainHeightMap.Heights[i];

                float after =
                    heights[i];

                if (!float.IsFinite(
                        after))
                {
                    throw new InvalidDataException(
                        $"Imported terrain vertex {i:N0} is not finite.");
                }

                after =
                    Math.Clamp(
                        after,
                        _terrainHeightMap.EncodableMinHeight,
                        _terrainHeightMap.EncodableMaxHeight);

                if (MathF.Abs(
                        before -
                        after) <
                    0.000001f)
                {
                    continue;
                }

                changes.Add(
                    new TerrainHeightChange(
                        i,
                        before,
                        after));

                _terrainHeightMap.Heights[i] =
                    after;
            }

            if (changes.Count ==
                0)
            {
                return 0;
            }

            _terrainPreviewUndo.Push(
                new TerrainPreviewStroke(
                    changes));

            _terrainPreviewRedo.Clear();

            RefreshTerrainHeightBitmap();

            TerrainPreviewChanged?.Invoke(
                this,
                EventArgs.Empty);

            return changes.Count;
        }
    }
}
