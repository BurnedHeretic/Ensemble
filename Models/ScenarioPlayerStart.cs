using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioPlayerStart
    {
        public int Player { get; set; }

        public int Number { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 Forward { get; set; }

        public bool DefaultCamera { get; set; }

        public float CameraYaw { get; set; }

        public float CameraPitch { get; set; }

        public float CameraZoom { get; set; }
    }
}