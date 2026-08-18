using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioPath
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public Vector3 Position { get; set; }

        public List<Vector3> Points { get; } = new();
    }
}