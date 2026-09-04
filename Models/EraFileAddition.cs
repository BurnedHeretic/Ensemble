namespace Ensemble.Models
{
    /// <summary>
    /// Describes a brand-new file that will be appended to an ERA.
    ///
    /// Existing chunk indices are preserved; new files are appended
    /// after all existing chunks.
    /// </summary>
    public sealed class EraFileAddition
    {
        public required string FileName
        {
            get;
            init;
        }

        /// <summary>
        /// Decompressed file bytes.
        /// </summary>
        public required byte[] Data
        {
            get;
            init;
        }

        /// <summary>
        /// Halo Wars ERA compression method:
        /// 0 = Stored
        /// 1 = Raw Deflate
        /// 2 = Halo Wars Deflate Stream
        ///
        /// Stored is a good default for DDX resources; shipping
        /// pregameUI.era contains valid map-image DDX chunks using it.
        /// </summary>
        public byte CompressionMethod
        {
            get;
            init;
        } = 0;

        /// <summary>
        /// Chunk data alignment as log2(bytes).
        /// 2 = 4-byte alignment, matching shipping map-image DDX chunks.
        /// </summary>
        public byte AlignmentLog2
        {
            get;
            init;
        } = 2;

        public ushort ResourceFlags
        {
            get;
            init;
        } = 0;

        /// <summary>
        /// Optional shipping-style file timestamp. If null, Ensemble
        /// inherits the first non-zero file date already present in
        /// the source ERA.
        /// </summary>
        public ulong? Date
        {
            get;
            init;
        }
    }
}
