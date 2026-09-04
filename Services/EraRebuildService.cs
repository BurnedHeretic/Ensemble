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
        // REPLACEMENTS + RENAMES
        //
        // Kept for all existing Ensemble callers.
        // =========================================================

        public static byte[] BuildModifiedEra(
            EraArchiveInfo archive,
            IReadOnlyDictionary<int, byte[]>
                replacementFiles,
            IReadOnlyDictionary<int, string>
                fileRenames)
        {
            return BuildModifiedEra(
                archive,
                replacementFiles,
                fileRenames,
                Array.Empty<EraFileAddition>());
        }


        // =========================================================
        // REPLACEMENTS + RENAMES + BRAND-NEW FILES
        //
        // New files are appended to the chunk table so every
        // existing chunk index remains stable. This is important
        // because MainWindow's save verification tracks scenario /
        // terrain chunks by their current indices.
        // =========================================================

        public static byte[] BuildModifiedEra(
            EraArchiveInfo archive,
            IReadOnlyDictionary<int, byte[]>
                replacementFiles,
            IReadOnlyDictionary<int, string>
                fileRenames,
            IReadOnlyList<EraFileAddition>
                fileAdditions)
        {
            ArgumentNullException.ThrowIfNull(
                archive);

            ArgumentNullException.ThrowIfNull(
                replacementFiles);

            ArgumentNullException.ThrowIfNull(
                fileRenames);

            ArgumentNullException.ThrowIfNull(
                fileAdditions);


            if (replacementFiles.Count ==
                    0 &&
                fileRenames.Count ==
                    0 &&
                fileAdditions.Count ==
                    0)
            {
                throw new ArgumentException(
                    "At least one ERA replacement, filename change " +
                    "or new file is required.");
            }


            if (!archive.IsEncrypted)
            {
                throw new NotSupportedException(
                    "This ERA writer currently expects " +
                    "the normal encrypted Halo Wars archive.");
            }


            if (archive.ChunkExtraDataSize <
                32)
            {
                throw new InvalidDataException(
                    "ERA chunk metadata is too small to contain " +
                    "Halo Wars filename/hash metadata.");
            }


            int originalChunkCount =
                archive.Chunks.Count;


            int finalChunkCount =
                checked(
                    originalChunkCount +
                    fileAdditions.Count);


            if (finalChunkCount >
                ushort.MaxValue)
            {
                throw new InvalidDataException(
                    "ERA chunk count exceeded the 16-bit archive limit.");
            }


            int chunkHeaderSize =
                checked(
                    BaseChunkHeaderSize +
                    archive.ChunkExtraDataSize);


            int originalChunkTableSize =
                checked(
                    originalChunkCount *
                    chunkHeaderSize);


            int finalChunkTableSize =
                checked(
                    finalChunkCount *
                    chunkHeaderSize);


            // =====================================================
            // VALIDATE / NORMALIZE NEW FILES
            // =====================================================

            List<string> normalizedAdditionNames =
                new List<string>(
                    fileAdditions.Count);


            foreach (EraFileAddition addition
                     in fileAdditions)
            {
                if (addition.Data ==
                    null)
                {
                    throw new InvalidDataException(
                        "An ERA file addition contains no data.");
                }


                if (addition.CompressionMethod is not
                    (0 or 1 or 2))
                {
                    throw new InvalidDataException(
                        "New ERA files support compression methods " +
                        "0 (Stored), 1 (Raw Deflate), or " +
                        "2 (Deflate Stream).");
                }


                if (addition.AlignmentLog2 >=
                    31)
                {
                    throw new InvalidDataException(
                        "New ERA file alignment is too large.");
                }


                normalizedAdditionNames.Add(
                    NormalizeArchiveFilename(
                        addition.FileName));
            }


            using FileStream originalStream =
                new FileStream(
                    archive.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);


            // -----------------------------------------------------
            // Decrypt original archive header and ORIGINAL table.
            // -----------------------------------------------------

            byte[] header =
                EraCryptoService.DecryptRange(
                    originalStream,
                    0,
                    checked(
                        (int)archive.HeaderSize));


            byte[] originalChunkTable =
                EraCryptoService.DecryptRange(
                    originalStream,
                    archive.HeaderSize,
                    originalChunkTableSize);


            // New table is zero-initialized after the old table.
            // Added chunk headers will be written into that region.
            byte[] chunkTable =
                new byte[
                    finalChunkTableSize];


            Buffer.BlockCopy(
                originalChunkTable,
                0,
                chunkTable,
                0,
                originalChunkTable.Length);


            // Update NumChunks inside the decrypted archive header.
            WriteUInt16(
                header,
                16,
                checked(
                    (ushort)finalChunkCount));


            // -----------------------------------------------------
            // Read raw STORED bytes for all existing chunks.
            // -----------------------------------------------------

            List<byte[]> storedChunks =
                new List<byte[]>(
                    finalChunkCount);


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
            // Metadata changed for existing modified chunks.
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


            // Name offsets for new appended chunks.
            List<int> additionNameOffsets =
                new List<int>(
                    fileAdditions.Count);


            // =====================================================
            // FILENAME TABLE
            //
            // Any rename OR addition modifies chunk 0.
            // =====================================================

            if (fileRenames.Count >
                    0 ||
                fileAdditions.Count >
                    0)
            {
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


                // -------------------------------------------------
                // Existing-file renames
                // -------------------------------------------------

                foreach (
                    KeyValuePair<int, string> rename
                    in fileRenames)
                {
                    int targetIndex =
                        rename.Key;


                    if (targetIndex <=
                            0 ||
                        targetIndex >=
                            originalChunkCount)
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
                        AppendFilename(
                            newFilenameData,
                            newName);


                    WriteNameOffset(
                        chunkTable,
                        checked(
                            targetIndex *
                            chunkHeaderSize),
                        newNameOffset);
                }


                // -------------------------------------------------
                // Brand-new files
                // -------------------------------------------------

                for (int i = 0;
                     i < fileAdditions.Count;
                     i++)
                {
                    string newName =
                        normalizedAdditionNames[
                            i];


                    if (!finalNames.Add(
                            newName))
                    {
                        throw new InvalidDataException(
                            "The custom ERA would contain " +
                            "duplicate archive filenames.\n\n" +
                            newName);
                    }


                    int newNameOffset =
                        AppendFilename(
                            newFilenameData,
                            newName);


                    additionNameOffsets.Add(
                        newNameOffset);
                }


                byte[] rebuiltFilenameData =
                    newFilenameData
                        .ToArray();


                // -------------------------------------------------
                // Verify the original shipping filename chunk.
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
            // EXISTING FILE CONTENT REPLACEMENTS
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
                        originalChunkCount)
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


                byte[] replacementStoredData =
                    EncodeReplacementChunk(
                        chunk,
                        replacementDecompressedData);


                replacementIds[
                    targetIndex] =
                        replacementId;


                replacementTiger128[
                    targetIndex] =
                        EraHashService.Tiger128(
                            replacementStoredData);


                replacementDecompressedSizes[
                    targetIndex] =
                        replacementDecompressedData.Length;


                storedChunks[
                    targetIndex] =
                        replacementStoredData;
            }


            // =====================================================
            // BRAND-NEW FILE CHUNK HEADERS
            // =====================================================

            ulong inheritedFileDate =
                archive.Chunks
                    .Skip(1)
                    .Select(
                        x =>
                            x.Date)
                    .FirstOrDefault(
                        x =>
                            x != 0);


            for (int i = 0;
                 i < fileAdditions.Count;
                 i++)
            {
                EraFileAddition addition =
                    fileAdditions[
                        i];


                byte[] decompressedData =
                    addition.Data;


                byte[] storedData =
                    EncodeAddedChunk(
                        addition.CompressionMethod,
                        decompressedData);


                int chunkIndex =
                    checked(
                        originalChunkCount +
                        i);


                int p =
                    checked(
                        chunkIndex *
                        chunkHeaderSize);


                ulong chunkId =
                    EraHashService.Tiger64(
                        decompressedData);


                byte[] compressedTiger128 =
                    EraHashService.Tiger128(
                        storedData);


                // Chunk ID
                WriteUInt64(
                    chunkTable,
                    p,
                    chunkId);


                // Offset is assigned during the common data-writing pass.


                // Compressed size
                WriteUInt32(
                    chunkTable,
                    p + 12,
                    checked(
                        (uint)storedData.Length));


                // Adler32 of stored/compressed bytes
                WriteUInt32(
                    chunkTable,
                    p + 16,
                    EraCompressionService
                        .Adler32(
                            storedData));


                // Compression flags
                chunkTable[
                    p + 20] =
                        addition.CompressionMethod;


                // Alignment
                chunkTable[
                    p + 21] =
                        addition.AlignmentLog2;


                // Resource flags
                WriteUInt16(
                    chunkTable,
                    p + 22,
                    addition.ResourceFlags);


                // File timestamp
                WriteUInt64(
                    chunkTable,
                    p + 24,
                    addition.Date
                    ?? inheritedFileDate);


                // Decompressed size
                WriteUInt32(
                    chunkTable,
                    p + 32,
                    checked(
                        (uint)decompressedData.Length));


                // Tiger128 of stored/compressed bytes
                Buffer.BlockCopy(
                    compressedTiger128,
                    0,
                    chunkTable,
                    p + 36,
                    16);


                // Filename offset
                WriteNameOffset(
                    chunkTable,
                    p,
                    additionNameOffsets[
                        i]);


                // Final byte in the 32-byte extension is unused
                // in the DE archives inspected so far.
                chunkTable[
                    p + 55] =
                        0;


                storedChunks.Add(
                    storedData);
            }


            if (storedChunks.Count !=
                finalChunkCount)
            {
                throw new InvalidDataException(
                    "Internal ERA rebuild chunk count mismatch.");
            }


            // =====================================================
            // REBUILD PLAINTEXT ARCHIVE
            // =====================================================

            int dataStart =
                checked(
                    (int)archive.HeaderSize +
                    finalChunkTableSize);


            using MemoryStream output =
                new MemoryStream();


            output.Write(
                new byte[
                    dataStart]);


            for (int i = 0;
                 i < finalChunkCount;
                 i++)
            {
                byte alignmentLog2 =
                    i <
                    originalChunkCount
                        ? archive.Chunks[
                            i]
                            .AlignmentLog2
                        : fileAdditions[
                            i -
                            originalChunkCount]
                            .AlignmentLog2;


                int alignment =
                    checked(
                        1 <<
                        alignmentLog2);


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
                    storedChunks[
                        i];


                output.Write(
                    data,
                    0,
                    data.Length);


                int p =
                    checked(
                        i *
                        chunkHeaderSize);


                WriteUInt32(
                    chunkTable,
                    p + 8,
                    newOffset);


                WriteUInt32(
                    chunkTable,
                    p + 12,
                    checked(
                        (uint)data.Length));


                WriteUInt32(
                    chunkTable,
                    p + 16,
                    EraCompressionService
                        .Adler32(
                            data));


                // Existing modified chunks need updated content
                // identity metadata. New chunks already have it.
                if (replacementIds.TryGetValue(
                        i,
                        out ulong replacementId))
                {
                    WriteUInt64(
                        chunkTable,
                        p,
                        replacementId);


                    WriteUInt32(
                        chunkTable,
                        p + 32,
                        checked(
                            (uint)
                            replacementDecompressedSizes[
                                i]));


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

            byte[] trailingGuardPadding =
                new byte[
                    4095];


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


            // Update archive FileSize.
            WriteUInt32(
                plaintext,
                12,
                checked(
                    (uint)plaintext.Length));


            // Recalculate archive header Adler32.
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


            // Original shipping signature bytes remain in the
            // archive header. Modified archives rely on Ensemble's
            // xgameFinal signature-check bypass.
            return
                EraCryptoService.EncryptAll(
                    plaintext);
        }


        // =========================================================
        // FILENAME HELPERS
        // =========================================================

        private static int AppendFilename(
            List<byte> filenameData,
            string name)
        {
            int offset =
                filenameData.Count;


            if (offset >
                0x00FFFFFF)
            {
                throw new InvalidDataException(
                    "ERA filename table exceeded its " +
                    "24-bit offset limit.");
            }


            byte[] bytes =
                Encoding.ASCII.GetBytes(
                    name);


            filenameData.AddRange(
                bytes);


            filenameData.Add(
                0);


            return offset;
        }


        private static void WriteNameOffset(
            byte[] chunkTable,
            int chunkHeaderOffset,
            int nameOffset)
        {
            if (nameOffset <
                    0 ||
                nameOffset >
                    0x00FFFFFF)
            {
                throw new InvalidDataException(
                    "ERA filename offset exceeded its 24-bit limit.");
            }


            chunkTable[
                chunkHeaderOffset + 52] =
                    (byte)(
                        (nameOffset >>
                         16) &
                        0xFF);


            chunkTable[
                chunkHeaderOffset + 53] =
                    (byte)(
                        (nameOffset >>
                         8) &
                        0xFF);


            chunkTable[
                chunkHeaderOffset + 54] =
                    (byte)(
                        nameOffset &
                        0xFF);
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
                new byte[
                    padding]);
        }


        // =========================================================
        // BIG-ENDIAN WRITERS
        // =========================================================

        private static void WriteUInt16(
            byte[] data,
            int offset,
            ushort value)
        {
            BinaryPrimitives
                .WriteUInt16BigEndian(
                    data.AsSpan(
                        offset,
                        2),
                    value);
        }


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
                0 =>
                    decompressedData,

                1 =>
                    EraCompressionService
                        .CompressDeflateRaw(
                            decompressedData),

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


        private static byte[] EncodeAddedChunk(
            byte compressionMethod,
            byte[] decompressedData)
        {
            return compressionMethod switch
            {
                0 =>
                    decompressedData,

                1 =>
                    EraCompressionService
                        .CompressDeflateRaw(
                            decompressedData),

                2 =>
                    EraCompressionService
                        .CompressDeflateStream(
                            decompressedData),

                _ =>
                    throw new NotSupportedException(
                        $"Unsupported ERA file-addition compression " +
                        $"method {compressionMethod}.")
            };
        }
    }
}
