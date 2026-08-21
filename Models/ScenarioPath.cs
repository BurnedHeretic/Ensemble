using System.Collections.Generic;
using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioPath
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public Vector3 Position { get; set; }

        public List<Vector3> Points { get; } =
            new();

        public List<Vector3> OriginalPoints { get; } =
            new();

        public bool HasPointChanges
        {
            get
            {
                if (Points.Count !=
                    OriginalPoints.Count)
                {
                    return true;
                }

                for (int i = 0;
                     i < Points.Count;
                     i++)
                {
                    if (Vector3.DistanceSquared(
                            Points[i],
                            OriginalPoints[i]) >
                        0.00000001f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void AcceptPointChangesAsBaseline()
        {
            OriginalPoints.Clear();

            OriginalPoints.AddRange(
                Points);
        }
    }
}