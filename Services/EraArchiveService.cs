using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ensemble.Models;

namespace Ensemble.Services
{
    public static class EraArchiveService
    {
        private const uint EcfMagic =
            0xDABA7737;

        private const uint ArchiveId =
            0x17FDBA9C;

        private const uint ArchiveMagic =
            0x05ABDBD8;

        private const int BaseChunkHeaderSize =
            24;

        public static EraArchiveInfo Open(
            string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(
                    "ERA file path cannot be empty.",
                    nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "The ERA file could not be found.",
                    filePath);
            }

            using FileStream stream =
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            if (stream.Length <
                EraCryptoService.BlockSize)
            {
                throw new InvalidDataException(
                    "The selected file is too small to be a Halo Wars ERA.");
            }

            byte[] rawHeader =
                new byte[EraCryptoService.BlockSize];

            ReadExactly(
                stream,
                rawHeader);

            uint rawMagic =
                BinaryPrimitives.ReadUInt32BigEndian(
                    rawHeader.AsSpan(0, 4));

            bool encrypted;

            byte[] header;

            if (rawMagic == EcfMagic)
            {
                encrypted = false;
                header = rawHeader;
            }
            else
            {
                encrypted = true;

                header =
                    EraCryptoService
                        .DecryptFirstBlock(rawHeader);
            }

            uint magic =
                ReadUInt32(header, 0);

            uint headerSize =
                ReadUInt32(header, 4);

            uint headerAdler32 =
                ReadUInt32(header, 8);

            uint archiveFileSize =
                ReadUInt32(header, 12);

            ushort numChunks =
                ReadUInt16(header, 16);

            ushort flags =
                ReadUInt16(header, 18);

            uint archiveId =
                ReadUInt32(header, 20);

            ushort chunkExtraDataSize =
                ReadUInt16(header, 24);

            uint archiveHeaderMagic =
                ReadUInt32(header, 32);

            uint signatureSize =
                ReadUInt32(header, 36);

            bool valid =
                magic == EcfMagic &&
                archiveId == ArchiveId &&
                archiveHeaderMagic == ArchiveMagic;

            if (!valid)
            {
                throw new InvalidDataException(
                    "The file could not be identified as a valid Halo Wars ERA archive.");
            }

            List<EraChunkInfo> chunks = 
                ReadChunkTable(
                    stream,
                    encrypted,
                    headerSize,
                    numChunks,
                    chunkExtraDataSize);

            ReadFilenameTable(
                stream,
                encrypted,
                chunks);

            return new EraArchiveInfo
            {
                FilePath =
                    Path.GetFullPath(filePath),

                FileSize =
                    stream.Length,

                IsEncrypted =
                    encrypted,

                IsValidEra =
                    valid,

                RawHeaderBytes =
                    rawHeader,

                DecryptedHeaderBytes =
                    header,

                Magic =
                    magic,

                HeaderSize =
                    headerSize,

                HeaderAdler32 =
                    headerAdler32,

                ArchiveFileSize =
                    archiveFileSize,

                NumChunks =
                    numChunks,

                Flags =
                    flags,

                ArchiveId =
                    archiveId,

                ChunkExtraDataSize =
                    chunkExtraDataSize,

                ArchiveHeaderMagic =
                    archiveHeaderMagic,

                SignatureSize =
                    signatureSize,

                Chunks =
                    chunks
            };
        }

        private static List<EraChunkInfo>
            ReadChunkTable(
                FileStream stream,
                bool encrypted,
                uint headerSize,
                ushort numChunks,
                ushort chunkExtraDataSize)
        {
            int chunkHeaderSize =
                checked(
                    BaseChunkHeaderSize +
                    chunkExtraDataSize);

            if (chunkHeaderSize < 24)
            {
                throw new InvalidDataException(
                    "Invalid ERA chunk header size.");
            }

            int tableSize =
                checked(
                    chunkHeaderSize *
                    numChunks);

            byte[] tableData;

            if (encrypted)
            {
                tableData =
                    EraCryptoService.DecryptRange(
                        stream,
                        headerSize,
                        tableSize);
            }
            else
            {
                stream.Position =
                    headerSize;

                tableData =
                    new byte[tableSize];

                ReadExactly(
                    stream,
                    tableData);
            }

            List<EraChunkInfo> chunks =
                new List<EraChunkInfo>(
                    numChunks);

            for (int i = 0;
                 i < numChunks;
                 i++)
            {
                int p =
                    i * chunkHeaderSize;

                ulong id =
                    ReadUInt64(tableData, p + 0);

                uint offset =
                    ReadUInt32(tableData, p + 8);

                uint compressedSize =
                    ReadUInt32(tableData, p + 12);

                uint adler32 =
                    ReadUInt32(tableData, p + 16);

                byte flags =
                    tableData[p + 20];

                byte alignmentLog2 =
                    tableData[p + 21];

                ushort resourceFlags =
                    ReadUInt16(tableData, p + 22);

                ulong date = 0;

                uint decompressedSize =
                    compressedSize;

                byte[] tiger =
                    new byte[16];

                uint nameOffset = 0;

                // Halo Wars ERA archives use
                // 32 bytes of chunk-specific data.
                if (chunkExtraDataSize >= 32)
                {
                    date =
                        ReadUInt64(
                            tableData,
                            p + 24);

                    decompressedSize =
                        ReadUInt32(
                            tableData,
                            p + 32);

                    Buffer.BlockCopy(
                        tableData,
                        p + 36,
                        tiger,
                        0,
                        16);

                    nameOffset =
                        ((uint)tableData[p + 52] << 16) |
                        ((uint)tableData[p + 53] << 8) |
                        tableData[p + 54];
                }

                chunks.Add(
                    new EraChunkInfo
                    {
                        Index =
                            i,

                        Id =
                            id,

                        Offset =
                            offset,

                        CompressedSize =
                            compressedSize,

                        Adler32 =
                            adler32,

                        Flags =
                            flags,

                        AlignmentLog2 =
                            alignmentLog2,

                        ResourceFlags =
                            resourceFlags,

                        Date =
                            date,

                        DecompressedSize =
                            decompressedSize,

                        CompressedTiger128 =
                            tiger,

                        NameOffset =
                            nameOffset
                    });
            }

            ValidateChunkTable(
                chunks,
                stream.Length);

            return chunks;
        }

        private static void ReadFilenameTable(
    FileStream stream,
    bool encrypted,
    List<EraChunkInfo> chunks)
        {
            if (chunks.Count < 2)
            {
                throw new InvalidDataException(
                    "ERA archive does not contain a filename table.");
            }

            EraChunkInfo filenameChunk =
                chunks[0];

            if (filenameChunk.CompressionMethod != 2)
            {
                throw new InvalidDataException(
                    $"Chunk 0 should use Deflate Stream compression, " +
                    $"but uses {filenameChunk.CompressionName}.");
            }

            byte[] compressedData =
                ReadChunkData(
                    stream,
                    encrypted,
                    filenameChunk);

            byte[] filenameData =
                EraCompressionService
                    .DecompressDeflateStream(
                        compressedData,
                        filenameChunk.DecompressedSize);

            for (int i = 1;
                 i < chunks.Count;
                 i++)
            {
                EraChunkInfo chunk =
                    chunks[i];

                if (chunk.NameOffset >=
                    filenameData.Length)
                {
                    throw new InvalidDataException(
                        $"Chunk {chunk.Index} has an invalid " +
                        $"filename offset: {chunk.NameOffset:N0}.");
                }

                int start =
                    checked((int)chunk.NameOffset);

                int end =
                    start;

                while (
                    end < filenameData.Length &&
                    filenameData[end] != 0)
                {
                    end++;
                }

                if (end >= filenameData.Length)
                {
                    throw new InvalidDataException(
                        $"Chunk {chunk.Index} filename is not " +
                        $"null terminated.");
                }

                int length =
                    end - start;

                if (length > 512)
                {
                    throw new InvalidDataException(
                        $"Chunk {chunk.Index} contains a filename " +
                        $"longer than the supported 512-byte limit.");
                }

                chunk.FileName =
                    Encoding.ASCII.GetString(
                        filenameData,
                        start,
                        length);
            }
        }

        private static byte[] ReadChunkData(
            FileStream stream,
            bool encrypted,
            EraChunkInfo chunk)
        {
            if (chunk.CompressedSize >
                int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Chunk {chunk.Index} is too large.");
            }

            int size =
                checked((int)chunk.CompressedSize);

            if (encrypted)
            {
                return
                    EraCryptoService.DecryptRange(
                        stream,
                        chunk.Offset,
                        size);
            }

            stream.Position =
                chunk.Offset;

            byte[] data =
                new byte[size];

            ReadExactly(
                stream,
                data);

            return data;
        }

        private static void ValidateChunkTable(
            List<EraChunkInfo> chunks,
            long archiveLength)
        {
            for (int i = 0;
                 i < chunks.Count;
                 i++)
            {
                EraChunkInfo chunk =
                    chunks[i];

                long end =
                    (long)chunk.Offset +
                    chunk.CompressedSize;

                if (chunk.Offset >= archiveLength ||
                    end > archiveLength)
                {
                    throw new InvalidDataException(
                        $"Chunk {i} points outside the ERA archive.");
                }
            }
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer)
        {
            int total = 0;

            while (total < buffer.Length)
            {
                int read =
                    stream.Read(
                        buffer,
                        total,
                        buffer.Length - total);

                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "Unexpected end of ERA archive.");
                }

                total += read;
            }
        }

        private static uint ReadUInt32(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt32BigEndian(
                        data.AsSpan(
                            offset,
                            4));
        }

        private static ushort ReadUInt16(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt16BigEndian(
                        data.AsSpan(
                            offset,
                            2));
        }

        private static ulong ReadUInt64(
            byte[] data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt64BigEndian(
                        data.AsSpan(
                            offset,
                            8));
        }
    }
}