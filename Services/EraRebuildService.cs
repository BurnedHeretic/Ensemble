using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Ensemble.Services
{
    public static class EraRebuildService
    {
        private const int BaseChunkHeaderSize =
            24;


        // =========================================================
        // SINGLE FILE REPLACEMENT
        // =========================================================

        public static byte[] BuildModifiedEra(
            EraArchiveInfo archive,
            EraChunkInfo replacementChunk,
            byte[] replacementFileData)
        {
            return BuildModifiedEra(
                archive,
                new Dictionary<int, byte[]>
                {
                    [replacementChunk.Index] =
                        replacementFileData
                });
        }


        // =========================================================
        // MULTIPLE FILE REPLACEMENTS
        // =========================================================

        public static byte[] BuildModifiedEra(
            EraArchiveInfo archive,
            IReadOnlyDictionary<int, byte[]>
                replacementFiles)
        {
            return BuildModifiedEra(
                archive,
                replacementFiles,
                new Dictionary<int, string>());
        }


        // =========================================================
        // FILENAME-ONLY REBUILD
        //
        // Used for creating a custom scenario alias such as:
        //
        // blood_gulch.scn.xmb
        //      ↓
        // ensemble_blood_gulch.scn.xmb
        //
        // without changing the contents of the file itself.
        // =========================================================

        public static byte[] BuildRenamedEra(
            EraArchiveInfo archive,
            IReadOnlyDictionary<int, string>
                fileRenames)
        {
            return BuildModifiedEra(
                archive,
                new Dictionary<int, byte[]>(),
                fileRenames);
        }


        // =========================================================
        // MULTIPLE FILE REPLACEMENTS + FILENAME RENAMES
        // =========================================================

        public static byte[] BuildModifiedEra(
            EraArchiveInfo archive,
            IReadOnlyDictionary<int, byte[]>
                replacementFiles,
            IReadOnlyDictionary<int, string>
                fileRenames)
        {
            if (archive == null)
            {
                throw new ArgumentNullException(
                    nameof(archive));
            }

            if (replacementFiles == null)
            {
                throw new ArgumentNullException(
                    nameof(replacementFiles));
            }

            if (fileRenames == null)
            {
                throw new ArgumentNullException(
                    nameof(fileRenames));
            }

            if (replacementFiles.Count ==
                    0 &&
                fileRenames.Count ==
                    0)
            {
                throw new ArgumentException(
                    "At least one ERA replacement or " +
                    "filename change is required.");
            }

            if (!archive.IsEncrypted)
            {
                throw new NotSupportedException(
                    "This ERA writer currently expects " +
                    "the normal encrypted Halo Wars archive.");
            }


            int chunkHeaderSize =
                checked(
                    BaseChunkHeaderSize +
                    archive.ChunkExtraDataSize);

            int chunkTableSize =
                checked(
                    archive.Chunks.Count *
                    chunkHeaderSize);


            using FileStream originalStream =
                new FileStream(
                    archive.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);


            // -----------------------------------------------------
            // Decrypt original archive header and chunk table.
            // -----------------------------------------------------

            byte[] header =
                EraCryptoService.DecryptRange(
                    originalStream,
                    0,
                    checked(
                        (int)archive.HeaderSize));

            byte[] chunkTable =
                EraCryptoService.DecryptRange(
                    originalStream,
                    archive.HeaderSize,
                    chunkTableSize);


            // -----------------------------------------------------
            // Read raw STORED chunk data.
            //
            // IMPORTANT:
            //
            // Every unchanged file keeps its original compressed
            // representation byte-for-byte.
            // -----------------------------------------------------

            List<byte[]> storedChunks =
                new List<byte[]>(
                    archive.Chunks.Count);


            foreach (EraChunkInfo chunk
                     in archive.Chunks)
            {
                byte[] data =
                    EraCryptoService.DecryptRange(
                        originalStream,
                        chunk.Offset,
                        checked(
                            (int)chunk.CompressedSize));

                storedChunks.Add(
                    data);
            }


            // -----------------------------------------------------
            // Metadata which must be changed for modified chunks.
            // -----------------------------------------------------

            Dictionary<int, ulong>
                replacementIds =
                    new();

            Dictionary<int, byte[]>
                replacementTiger128 =
                    new();

            Dictionary<int, int>
                replacementDecompressedSizes =
                    new();


            // =====================================================
            // OPTIONAL ERA FILENAME RENAMES
            // =====================================================
            //
            // Chunk 0 contains the compressed filename table.
            //
            // Each normal chunk stores a 24-bit offset into that
            // decompressed filename table at bytes 52-54 of its
            // extended ERA chunk header.
            //
            // Rather than disturbing existing names/offsets,
            // Ensemble appends new names to the existing table
            // and redirects renamed chunks to those new strings.
            // =====================================================

            if (fileRenames.Count >
                0)
            {
                if (archive.ChunkExtraDataSize <
                    32)
                {
                    throw new InvalidDataException(
                        "ERA chunk metadata is too small " +
                        "to contain filename offsets.");
                }


                if (archive.Chunks.Count ==
                    0)
                {
                    throw new InvalidDataException(
                        "ERA contains no filename-table chunk.");
                }


                EraChunkInfo filenameChunk =
                    archive.Chunks[0];


                if (filenameChunk.CompressionMethod !=
                    2)
                {
                    throw new InvalidDataException(
                        "ERA filename table does not use " +
                        "the expected Deflate Stream compression.");
                }


                byte[] originalFilenameData =
                    EraCompressionService
                        .DecompressDeflateStream(
                            storedChunks[0],
                            filenameChunk.DecompressedSize);


                List<byte> newFilenameData =
                    new List<byte>(
                        originalFilenameData);


                HashSet<string> finalNames =
                    new HashSet<string>(
                        archive.Chunks
                            .Skip(1)
                            .Select(
                                x =>
                                    x.FileName),
                        StringComparer.OrdinalIgnoreCase);


                foreach (
                    KeyValuePair<int, string> rename
                    in fileRenames)
                {
                    int targetIndex =
                        rename.Key;


                    if (targetIndex <=
                            0 ||
                        targetIndex >=
                            archive.Chunks.Count)
                    {
                        throw new InvalidDataException(
                            $"Invalid ERA rename chunk index: " +
                            $"{targetIndex}.");
                    }


                    EraChunkInfo targetChunk =
                        archive.Chunks[
                            targetIndex];


                    string newName =
                        NormalizeArchiveFilename(
                            rename.Value);


                    if (string.Equals(
                            targetChunk.FileName,
                            newName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }


                    if (finalNames.Contains(
                            newName))
                    {
                        throw new InvalidDataException(
                            "The custom ERA would contain " +
                            "duplicate archive filenames.\n\n" +
                            newName);
                    }


                    finalNames.Remove(
                        targetChunk.FileName);

                    finalNames.Add(
                        newName);


                    int newNameOffset =
                        newFilenameData.Count;


                    if (newNameOffset >
                        0x00FFFFFF)
                    {
                        throw new InvalidDataException(
                            "ERA filename table exceeded its " +
                            "24-bit offset limit.");
                    }


                    byte[] nameBytes =
                        Encoding.ASCII.GetBytes(
                            newName);


                    newFilenameData.AddRange(
                        nameBytes);

                    newFilenameData.Add(
                        0);


                    int p =
                        checked(
                            targetIndex *
                            chunkHeaderSize);


                    // -------------------------------------------------
                    // NameOffset is a 24-bit BIG-ENDIAN value.
                    //
                    // Existing reader:
                    //
                    // byte 52 << 16
                    // byte 53 << 8
                    // byte 54
                    // -------------------------------------------------

                    chunkTable[
                        p + 52] =
                            (byte)(
                                (newNameOffset >>
                                 16) &
                                0xFF);

                    chunkTable[
                        p + 53] =
                            (byte)(
                                (newNameOffset >>
                                 8) &
                                0xFF);

                    chunkTable[
                        p + 54] =
                            (byte)(
                                newNameOffset &
                                0xFF);
                }


                byte[] rebuiltFilenameData =
                    newFilenameData
                        .ToArray();


                // -------------------------------------------------
                // Verify shipping filename-table Tiger128
                // before replacing it.
                // -------------------------------------------------

                byte[] originalTiger128 =
                    EraHashService.Tiger128(
                        storedChunks[0]);


                if (!originalTiger128
                        .AsSpan()
                        .SequenceEqual(
                            filenameChunk
                                .CompressedTiger128))
                {
                    throw new InvalidDataException(
                        "ERA filename-table Tiger128 " +
                        "verification failed.");
                }


                // -------------------------------------------------
                // ERA chunk ID:
                //
                // Tiger64 of DECOMPRESSED contents.
                // -------------------------------------------------

                ulong newFilenameId =
                    EraHashService
                        .ComputeReplacementTiger64(
                            filenameChunk.Id,
                            originalFilenameData,
                            rebuiltFilenameData);


                byte[] compressedFilenameData =
                    EraCompressionService
                        .CompressDeflateStream(
                            rebuiltFilenameData);


                replacementIds[0] =
                    newFilenameId;


                replacementTiger128[0] =
                    EraHashService.Tiger128(
                        compressedFilenameData);


                replacementDecompressedSizes[0] =
                    rebuiltFilenameData.Length;


                storedChunks[0] =
                    compressedFilenameData;
            }


            // =====================================================
            // FILE CONTENT REPLACEMENTS
            // =====================================================

            foreach (
                KeyValuePair<int, byte[]> replacement
                in replacementFiles)
            {
                int targetIndex =
                    replacement.Key;


                if (targetIndex <=
                        0 ||
                    targetIndex >=
                        archive.Chunks.Count)
                {
                    throw new InvalidDataException(
                        $"Invalid replacement ERA chunk index: " +
                        $"{targetIndex}.");
                }


                EraChunkInfo chunk =
                    archive.Chunks[
                        targetIndex];


                byte[] replacementDecompressedData =
                    replacement.Value
                    ?? throw new InvalidDataException(
                        $"Replacement chunk {targetIndex} " +
                        "contains no data.");


                byte[] originalStoredData =
                    storedChunks[
                        targetIndex];


                // -------------------------------------------------
                // Verify compressed Tiger128 against current ERA.
                // -------------------------------------------------

                byte[] existingTiger128 =
                    EraHashService.Tiger128(
                        originalStoredData);


                if (!existingTiger128
                        .AsSpan()
                        .SequenceEqual(
                            chunk.CompressedTiger128))
                {
                    throw new InvalidDataException(
                        $"Tiger128 verification failed for ERA " +
                        $"chunk {targetIndex} " +
                        $"({chunk.FileName}).");
                }


                // -------------------------------------------------
                // Chunk ID = Tiger64 of DECOMPRESSED file data.
                // -------------------------------------------------

                byte[] originalDecompressedData =
                    EraExtractionService.ExtractChunk(
                        archive,
                        chunk);


                ulong replacementId =
                    EraHashService
                        .ComputeReplacementTiger64(
                            chunk.Id,
                            originalDecompressedData,
                            replacementDecompressedData);


                // -------------------------------------------------
                // Recompress using the SAME compression method
                // as the original chunk.
                // -------------------------------------------------

                byte[] replacementStoredData =
                    EncodeReplacementChunk(
                        chunk,
                        replacementDecompressedData);


                byte[] newTiger128 =
                    EraHashService.Tiger128(
                        replacementStoredData);


                replacementIds[
                    targetIndex] =
                        replacementId;


                replacementTiger128[
                    targetIndex] =
                        newTiger128;


                replacementDecompressedSizes[
                    targetIndex] =
                        replacementDecompressedData.Length;


                storedChunks[
                    targetIndex] =
                        replacementStoredData;
            }


            // =====================================================
            // REBUILD PLAINTEXT ARCHIVE
            // =====================================================

            int dataStart =
                checked(
                    (int)archive.HeaderSize +
                    chunkTableSize);


            using MemoryStream output =
                new MemoryStream();


            // Reserve space for archive header + chunk table.
            output.Write(
                new byte[dataStart]);


            for (int i = 0;
                 i < archive.Chunks.Count;
                 i++)
            {
                EraChunkInfo chunk =
                    archive.Chunks[i];


                int alignment =
                    checked(
                        1 <<
                        chunk.AlignmentLog2);


                Align(
                    output,
                    alignment);


                if (output.Position >
                    uint.MaxValue)
                {
                    throw new InvalidDataException(
                        "ERA exceeded the supported size.");
                }


                uint newOffset =
                    checked(
                        (uint)output.Position);


                byte[] data =
                    storedChunks[i];


                output.Write(
                    data,
                    0,
                    data.Length);


                int p =
                    checked(
                        i *
                        chunkHeaderSize);


                // -------------------------------------------------
                // New location
                // -------------------------------------------------

                WriteUInt32(
                    chunkTable,
                    p + 8,
                    newOffset);


                // -------------------------------------------------
                // New compressed size
                // -------------------------------------------------

                WriteUInt32(
                    chunkTable,
                    p + 12,
                    checked(
                        (uint)data.Length));


                // -------------------------------------------------
                // Adler32 of STORED data
                // -------------------------------------------------

                WriteUInt32(
                    chunkTable,
                    p + 16,
                    EraCompressionService
                        .Adler32(
                            data));


                // -------------------------------------------------
                // Modified file metadata
                // -------------------------------------------------

                if (replacementIds.TryGetValue(
                        i,
                        out ulong replacementId))
                {
                    // Tiger64 / chunk ID
                    WriteUInt64(
                        chunkTable,
                        p,
                        replacementId);


                    // Decompressed size
                    WriteUInt32(
                        chunkTable,
                        p + 32,
                        checked(
                            (uint)
                            replacementDecompressedSizes[
                                i]));


                    // Tiger128 of compressed data
                    Buffer.BlockCopy(
                        replacementTiger128[
                            i],
                        0,
                        chunkTable,
                        p + 36,
                        16);
                }
            }


            // =====================================================
            // FINAL ERA GUARD PADDING
            // =====================================================
            //
            // Halo Wars BECFArchiver ALWAYS writes 4095 zero
            // bytes after the final chunk, then aligns the
            // completed archive to 4096.
            //
            // This behaviour was required for rebuilt ERAs to
            // match the structure expected by Halo Wars DE.
            // =====================================================

            byte[] trailingGuardPadding =
                new byte[4095];


            output.Write(
                trailingGuardPadding,
                0,
                trailingGuardPadding.Length);


            Align(
                output,
                4096);


            if (output.Length >
                uint.MaxValue)
            {
                throw new InvalidDataException(
                    "ERA exceeded 4 GB.");
            }


            byte[] plaintext =
                output.ToArray();


            // =====================================================
            // RESTORE HEADER + REBUILT CHUNK TABLE
            // =====================================================

            Buffer.BlockCopy(
                header,
                0,
                plaintext,
                0,
                header.Length);


            Buffer.BlockCopy(
                chunkTable,
                0,
                plaintext,
                checked(
                    (int)archive.HeaderSize),
                chunkTable.Length);


            // -----------------------------------------------------
            // Update archive FileSize.
            // -----------------------------------------------------

            WriteUInt32(
                plaintext,
                12,
                checked(
                    (uint)plaintext.Length));


            // -----------------------------------------------------
            // Recalculate archive header Adler32.
            // -----------------------------------------------------

            WriteUInt32(
                plaintext,
                8,
                0);


            uint headerAdler =
                EraCompressionService.Adler32(
                    plaintext.AsSpan(
                        12,
                        checked(
                            (int)archive.HeaderSize -
                            12)));


            WriteUInt32(
                plaintext,
                8,
                headerAdler);


            // -----------------------------------------------------
            // The original archive signature bytes remain present
            // in the header.
            //
            // Modified archives rely on Ensemble's Halo Wars EXE
            // signature-check patch.
            // -----------------------------------------------------

            return
                EraCryptoService.EncryptAll(
                    plaintext);
        }


        // =========================================================
        // ARCHIVE FILENAME VALIDATION
        // =========================================================

        private static string NormalizeArchiveFilename(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                throw new ArgumentException(
                    "Archive filename cannot be empty.");
            }


            string result =
                value
                    .Replace(
                        '/',
                        '\\')
                    .TrimStart(
                        '\\');


            if (result.Contains(
                    '\0'))
            {
                throw new InvalidDataException(
                    "Archive filename contains " +
                    "a null character.");
            }


            if (result.Length >
                512)
            {
                throw new InvalidDataException(
                    "Archive filename exceeds " +
                    "512 characters.");
            }


            foreach (char c
                     in result)
            {
                if (c >
                    0x7F)
                {
                    throw new InvalidDataException(
                        "Halo Wars ERA filenames " +
                        "must be ASCII.");
                }
            }


            return result;
        }


        // =========================================================
        // ALIGNMENT
        // =========================================================

        private static void Align(
            MemoryStream stream,
            int alignment)
        {
            if (alignment <=
                0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alignment));
            }


            long remainder =
                stream.Position %
                alignment;


            if (remainder ==
                0)
            {
                return;
            }


            int padding =
                checked(
                    (int)(
                        alignment -
                        remainder));


            stream.Write(
                new byte[padding]);
        }


        // =========================================================
        // BIG-ENDIAN WRITERS
        // =========================================================

        private static void WriteUInt32(
            byte[] data,
            int offset,
            uint value)
        {
            BinaryPrimitives
                .WriteUInt32BigEndian(
                    data.AsSpan(
                        offset,
                        4),
                    value);
        }


        private static void WriteUInt64(
            byte[] data,
            int offset,
            ulong value)
        {
            BinaryPrimitives
                .WriteUInt64BigEndian(
                    data.AsSpan(
                        offset,
                        8),
                    value);
        }


        // =========================================================
        // ERA CHUNK COMPRESSION
        // =========================================================

        private static byte[] EncodeReplacementChunk(
            EraChunkInfo chunk,
            byte[] decompressedData)
        {
            return chunk.CompressionMethod switch
            {
                // Stored
                0 =>
                    decompressedData,

                // Raw Deflate
                1 =>
                    EraCompressionService
                        .CompressDeflateRaw(
                            decompressedData),

                // Halo Wars Deflate Stream
                2 =>
                    EraCompressionService
                        .CompressDeflateStream(
                            decompressedData),

                _ =>
                    throw new NotSupportedException(
                        $"ERA chunk {chunk.Index} uses unsupported " +
                        $"compression method " +
                        $"{chunk.CompressionMethod}.")
            };
        }
    }
}