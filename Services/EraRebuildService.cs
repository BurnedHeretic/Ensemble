using Ensemble.Models;
using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    public static class EraRebuildService
    {
        private const int BaseChunkHeaderSize =
            24;

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

        public static byte[] BuildModifiedEra(
                EraArchiveInfo archive,
                IReadOnlyDictionary<int, byte[]>
                replacementFiles)
        {
            if (archive == null)
                throw new ArgumentNullException(
                    nameof(archive));

            if (replacementFiles == null)
            {
                throw new ArgumentNullException(
                    nameof(replacementFiles));
            }

            if (replacementFiles.Count ==
                0)
            {
                throw new ArgumentException(
                    "At least one ERA replacement file is required.",
                    nameof(replacementFiles));
            }

            if (!archive.IsEncrypted)
            {
                throw new NotSupportedException(
                    "This first ERA writer currently expects " +
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
            // Read raw stored chunk data.
            //
            // IMPORTANT:
            // We preserve the existing compressed representation
            // of every unchanged archive file.
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

            Dictionary<int, ulong>
                replacementIds = new();

            Dictionary<int, byte[]>
                replacementTiger128 =
                    new();

            Dictionary<int, int>
                replacementDecompressedSizes =
                    new();


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


                // -----------------------------------------------------
                // Verify compressed Tiger128 against shipping archive.
                // -----------------------------------------------------

                byte[] existingTiger128 =
                    EraHashService.Tiger128(
                        originalStoredData);

                if (!existingTiger128.AsSpan()
                        .SequenceEqual(
                            chunk.CompressedTiger128))
                {
                    throw new InvalidDataException(
                        $"Tiger128 verification failed for ERA " +
                        $"chunk {targetIndex} ({chunk.FileName}).");
                }


                // -----------------------------------------------------
                // ID is Tiger64 of DECOMPRESSED file data.
                //
                // This matters now because terrain chunks may be
                // compressed even though the old scenario path happened
                // to use Stored data.
                // -----------------------------------------------------

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

            // -----------------------------------------------------
            // Rebuild plaintext archive
            // -----------------------------------------------------

            int dataStart =
                checked(
                    (int)archive.HeaderSize +
                    chunkTableSize);

            using MemoryStream output =
                new MemoryStream();

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

                if (replacementIds.TryGetValue(i,
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

            // -----------------------------------------------------
            // Halo Wars BECFArchiver behaviour:
            //
            // It ALWAYS writes 4095 zero padding bytes after the
            // final chunk, then aligns the completed archive to
            // the next 4096-byte boundary.
            //
            // This is not equivalent to simply aligning the end
            // of the final chunk.
            // -----------------------------------------------------

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

            // Restore the archive header and rebuilt chunk table.
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

            // The original archive's signature bytes remain
            // preserved in the header for this first DE test.

            return
                EraCryptoService.EncryptAll(
                    plaintext);
        }

        private static void Align(
            MemoryStream stream,
            int alignment)
        {
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
                        $"compression method {chunk.CompressionMethod}.")
            };
        }
    }
}