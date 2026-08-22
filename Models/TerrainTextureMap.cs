using System;

namespace Ensemble.Models
{
    public sealed class TerrainTextureMap
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

        public int MipCount
        {
            get;
            init;
        }

        public byte[] BgraPixels
        {
            get;
            init;
        } = Array.Empty<byte>();
    }
}