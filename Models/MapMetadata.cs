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

        /// <summary>
        /// Maximum supported players for the custom map.
        /// Halo Wars skirmish scenarios support 2, 4 or 6 players.
        /// A value of 0 means "infer from scenario/manifest" for older maps.
        /// </summary>
        public int PlayerCount
        {
            get;
            set;
        }

        public MapMetadata Clone()
        {
            return new MapMetadata
            {
                FormatVersion =
                    FormatVersion,

                DisplayName =
                    DisplayName,

                Description =
                    Description,

                PlayerCount =
                    PlayerCount
            };
        }
    }
}
