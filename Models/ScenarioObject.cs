using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioObject
    {
        public int Id { get; set; }

        public bool IsSquad { get; set; }

        public int Player { get; set; }

        public bool IsNewObject
        {
            get;
            set;
        }

        public int SourceObjectId
        {
            get;
            set;
        }

        public float TintValue { get; set; }

        public string EditorName { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public Vector3 Position { get; set; }

        public Vector3 Forward { get; set; }

        public Vector3 Right { get; set; }

        public int Group { get; set; }

        public int VisualVariationIndex { get; set; }

        public List<string> Flags { get; } = new();

        public string Category
        {
            get
            {
                string type = Type.ToLowerInvariant();

                if (type.Contains("game_base_socket"))
                    return "Base";

                if (type.Contains("reactor"))
                    return "Reactor";

                if (type.Contains("supply"))
                    return "Supply";

                if (type.Contains("teleporter"))
                    return "Teleporter";

                if (type.Contains("sniperplatform"))
                    return "Sniper Platform";

                if (type.Contains("creep"))
                    return "Creep";

                if (type.Contains("crate"))
                    return "Crate";

                if (type.Contains("rebelmarker"))
                    return "Rebel Marker";

                return "Object";
            }
        }
    }
}