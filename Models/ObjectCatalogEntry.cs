using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Ensemble.Models
{
    internal enum ObjectCatalogLayer
    {
        Scenario,
        ArtObject,
        ImportedMesh
    }

    internal sealed class ObjectCatalogEntry :
        INotifyPropertyChanged
    {
        private ImageSource?
            _previewImage;

        private string
            _previewKind =
                string.Empty;

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

        /// <summary>
        /// Friendly player-facing name used by the browser.
        /// </summary>
        public string Name
        {
            get;
            init;
        } =
            string.Empty;

        /// <summary>
        /// Original editor / art-object name from the XMB. Kept separate so
        /// imports and UGX resolution never lose the exact source identifier.
        /// </summary>
        public string InternalName
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
            get =>
                _previewImage;

            set
            {
                if (ReferenceEquals(
                        _previewImage,
                        value))
                {
                    return;
                }

                _previewImage =
                    value;

                OnPropertyChanged();
            }
        }

        public string PreviewKind
        {
            get =>
                _previewKind;

            set
            {
                string safeValue =
                    value
                    ??
                    string.Empty;

                if (string.Equals(
                        _previewKind,
                        safeValue,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _previewKind =
                    safeValue;

                OnPropertyChanged();
            }
        }

        public bool CanPlace =>
            Layer != ObjectCatalogLayer.ImportedMesh
            ||
            ImportedMesh?.Extension.Equals(
                ".obj",
                StringComparison.OrdinalIgnoreCase) == true
            ||
            ImportedMesh?.Extension.Equals(
                ".fbx",
                StringComparison.OrdinalIgnoreCase) == true;

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

        public string InternalNameText =>
            string.IsNullOrWhiteSpace(
                InternalName)
                ? Type
                : InternalName;

        public string SourceEraName =>
            string.IsNullOrWhiteSpace(
                DonorEraPath)
                ? "LOCAL LIBRARY"
                : Path.GetFileName(
                    DonorEraPath);

        public string SourceDisplayText =>
            string.IsNullOrWhiteSpace(
                DonorEraPath)
                ? SourceFileName
                : SourceEraName +
                  "  //  " +
                  SourceFileName;

        public string SearchText =>
            (
                LayerName +
                " " +
                Name +
                " " +
                InternalName +
                " " +
                Type +
                " " +
                Id +
                " " +
                SourceFileName +
                " " +
                SourceEraName
            )
            .ToLowerInvariant();

        public event PropertyChangedEventHandler?
            PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName]
            string? propertyName =
                null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }

    internal sealed class ObjectCatalogLoadResult
    {
        public EraArchiveInfo? Archive
        {
            get;
            init;
        }

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
