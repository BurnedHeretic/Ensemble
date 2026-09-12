using System.Numerics;
using System.Windows.Media;

namespace Ensemble.Models
{
    internal enum ObjectCatalogLayer
    {
        Scenario,
        ArtObject,
        ImportedMesh
    }

    internal sealed class ObjectCatalogEntry
    {
        public ObjectCatalogLayer Layer
        {
            get;
            init;
        }

        public string DonorEraPath
        {
            get;
            init;
        } =
            string.Empty;

        public string SourceFileName
        {
            get;
            init;
        } =
            string.Empty;

        public int Id
        {
            get;
            init;
        }

        public string Name
        {
            get;
            init;
        } =
            string.Empty;

        public string Type
        {
            get;
            init;
        } =
            string.Empty;

        public Vector3 Position
        {
            get;
            init;
        }

        public byte[] SourceXmbData
        {
            get;
            init;
        } =
            Array.Empty<byte>();

        public ScenarioObject? ScenarioObject
        {
            get;
            init;
        }

        public ScenarioArtObject? ArtObject
        {
            get;
            init;
        }

        public ImportedMeshEntry? ImportedMesh
        {
            get;
            init;
        }

        public ImageSource? PreviewImage
        {
            get;
            set;
        }

        public bool CanPlace =>
            Layer !=
            ObjectCatalogLayer.ImportedMesh;

        public string LayerName =>
            Layer switch
            {
                ObjectCatalogLayer.ArtObject =>
                    "SC2 ART",

                ObjectCatalogLayer.ImportedMesh =>
                    "CUSTOM MESH",

                _ =>
                    "SCN GAMEPLAY"
            };

        public string PositionText =>
            Layer ==
                ObjectCatalogLayer.ImportedMesh
                ? "Local mesh library"
                : $"{Position.X:0.##}, " +
                  $"{Position.Y:0.##}, " +
                  $"{Position.Z:0.##}";

        public string SearchText =>
            (
                LayerName +
                " " +
                Name +
                " " +
                Type +
                " " +
                Id +
                " " +
                SourceFileName
            )
            .ToLowerInvariant();
    }

    internal sealed class ObjectCatalogLoadResult
    {
        public List<ObjectCatalogEntry> Entries
        {
            get;
            init;
        } =
            new();

        public List<string> Warnings
        {
            get;
            init;
        } =
            new();
    }
}
