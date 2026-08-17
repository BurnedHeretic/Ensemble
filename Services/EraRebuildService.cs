using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Ensemble.Models;

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
            if (archive == null)
                throw new ArgumentNullException(
                    nameof(archive));

            if (replacementChunk == null)
                throw new ArgumentNullException(
                    nameof(replacementChunk));

            if (replacementFileData == null)
                throw new ArgumentNullException(
                    nameof(replacementFileData));

            if (!archive.IsEncrypted)
            {
                throw new NotSupportedException(
                    "This first ERA writer currently expects " +
                    "the normal encrypted Halo Wars archive.");
            }

            if (replacementChunk.CompressionMethod !=
                0)
            {
                throw new NotSupportedException(
                    "The first write-back implementation " +
                    "currently supports replacing Stored ERA chunks only.");
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

            int targetIndex =
                replacementChunk.Index;

            if (targetIndex <= 0 ||
                targetIndex >=
                archive.Chunks.Count)
            {
                throw new InvalidDataException(
                    "Invalid replacement ERA chunk index.");
            }

            byte[] originalTargetData =
                storedChunks[
                    targetIndex];

            // Verify our Tiger implementation against the
            // shipping ERA before trusting it for output.
            byte[] existingTiger128 =
                EraHashService.Tiger128(
                    originalTargetData);

            if (!existingTiger128.AsSpan()
                    .SequenceEqual(
                        replacementChunk
                            .CompressedTiger128))
            {
                throw new InvalidDataException(
                    "Tiger128 verification failed against " +
                    "the original ERA chunk. Ensemble will " +
                    "not rebuild the archive using an " +
                    "unverified hash implementation.");
            }

            ulong replacementId =
                EraHashService
                    .ComputeReplacementTiger64(
                        replacementChunk.Id,
                        originalTargetData,
                        replacementFileData);

            byte[] replacementTiger128 =
                EraHashService.Tiger128(
                    replacementFileData);

            storedChunks[
                targetIndex] =
                    replacementFileData;

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

                if (i ==
                    targetIndex)
                {
                    // ID = decompressed Tiger64.
                    WriteUInt64(
                        chunkTable,
                        p,
                        replacementId);

                    // Extra archive data starts at +24.
                    // +8 Date
                    // +4 Decompressed size
                    // +16 compressed Tiger128

                    WriteUInt32(
                        chunkTable,
                        p + 32,
                        checked(
                            (uint)
                            replacementFileData.Length));

                    Buffer.BlockCopy(
                        replacementTiger128,
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
    }
}