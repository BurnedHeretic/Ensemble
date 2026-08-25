namespace Ensemble.Models
{
    public sealed class TerrainSimulationMap
    {
        public int Width
        {
            get;
            init;
        }

        public int PaddedWidth
        {
            get;
            init;
        }

        public float HeightTileScale
        {
            get;
            init;
        }

        public float[] Heights
        {
            get;
            init;
        } =
            Array.Empty<float>();

        public string StorageDescription
        {
            get;
            init;
        } =
            string.Empty;

        public float ReferenceMatchError
        {
            get;
            init;
        }
    }
}