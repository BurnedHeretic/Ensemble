using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioSphere
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public Vector3 Position { get; set; }

        public float Radius { get; set; }
    }
}