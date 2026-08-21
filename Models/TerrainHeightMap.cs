using System;
using System.Numerics;

namespace Ensemble.Models
{
    public sealed class TerrainHeightMap
    {
        public int Width
        {
            get;
            init;
        }

        public int Height
        {
            get;
            init;
        }

        public float TileScale
        {
            get;
            init;
        }

        public Vector3 WorldMin
        {
            get;
            init;
        }

        public Vector3 WorldMax
        {
            get;
            init;
        }

        public float MinHeight
        {
            get;
            init;
        }

        public float MaxHeight
        {
            get;
            init;
        }

        public float[] Heights
        {
            get;
            init;
        } = Array.Empty<float>();

        public float WorldWidth =>
            Math.Max(
                0,
                Width - 1) *
            TileScale;

        public float WorldDepth =>
            Math.Max(
                0,
                Height - 1) *
            TileScale;
    }
}