namespace Ensemble.Models
{
    public sealed class MapMetadata
    {
        public int FormatVersion
        {
            get;
            set;
        } = 1;


        public string DisplayName
        {
            get;
            set;
        } = string.Empty;


        public string Description
        {
            get;
            set;
        } = string.Empty;


        public MapMetadata Clone()
        {
            return new MapMetadata
            {
                FormatVersion =
                    FormatVersion,

                DisplayName =
                    DisplayName,

                Description =
                    Description
            };
        }
    }
}