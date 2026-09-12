namespace Ensemble.Models
{
    internal sealed class ImportedMeshEntry
    {
        public string Id
        {
            get;
            init;
        } =
            Guid.NewGuid()
                .ToString(
                    "N");

        public string DisplayName
        {
            get;
            init;
        } =
            string.Empty;

        public string OriginalSourcePath
        {
            get;
            init;
        } =
            string.Empty;

        public string LibraryFilePath
        {
            get;
            init;
        } =
            string.Empty;

        public string FileName
        {
            get;
            init;
        } =
            string.Empty;

        public string Extension
        {
            get;
            init;
        } =
            string.Empty;

        public DateTime ImportedUtc
        {
            get;
            init;
        } =
            DateTime.UtcNow;
    }
}
