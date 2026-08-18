namespace Ensemble.Models
{
    public sealed class ScenarioMap
    {
        public string Name { get; set; } = string.Empty;

        public string Terrain { get; set; } = string.Empty;

        public float MinX { get; set; }

        public float MinZ { get; set; }

        public float MaxX { get; set; }

        public float MaxZ { get; set; }

        public List<ScenarioObject> Objects { get; } = new();

        public List<ScenarioPlayerStart> PlayerStarts { get; } = new();

        public List<ScenarioSphere> Spheres { get; } = new();

        public List<ScenarioPath> Paths { get; } = new();
    }
}