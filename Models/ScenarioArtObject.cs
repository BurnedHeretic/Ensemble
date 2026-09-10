using System.Numerics;

namespace Ensemble.Models
{
    public sealed class ScenarioArtObject
    {
        public int Id
        {
            get;
            set;
        }


        public int SourceObjectId
        {
            get;
            set;
        }


        public string EditorName
        {
            get;
            set;
        } =
            string.Empty;


        public string OriginalEditorName
        {
            get;
            set;
        } =
            string.Empty;


        public string Type
        {
            get;
            set;
        } =
            string.Empty;


        public Vector3 Position
        {
            get;
            set;
        }


        public Vector3 Forward
        {
            get;
            set;
        }


        public Vector3 Right
        {
            get;
            set;
        }


        public int Group
        {
            get;
            set;
        }


        public int VisualVariationIndex
        {
            get;
            set;
        }


        public List<string> Flags
        {
            get;
        } =
            new();


        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(
                        EditorName))
                {
                    return EditorName;
                }


                if (!string.IsNullOrWhiteSpace(
                        Type))
                {
                    return Type;
                }


                return $"ArtObject {Id}";
            }
        }
    }
}