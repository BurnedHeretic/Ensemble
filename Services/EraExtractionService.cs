using System;
using System.IO;
using System.IO.Compression;
using Ensemble.Models;

namespace Ensemble.Services
{
    public static class EraExtractionService
    {
        public static byte[] ExtractChunk(
            EraArchiveInfo archive,
            EraChunkInfo chunk)
        {
            if (chunk.Index == 0)
            {
                throw new InvalidOperationException(
                    "Chunk 0 is the internal filename table and is not a normal archive file.");
            }

            using FileStream stream = new FileStream(
                archive.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            byte[] storedData = ReadStoredChunkData(
                stream,
                archive.IsEncrypted,
                chunk);

            ValidateCompressedData(
                chunk,
                storedData);

            byte[] result;

            switch (chunk.CompressionMethod)
            {
                case 0:
                    result = ExtractStored(
                        chunk,
                        storedData);
                    break;

                case 1:
                    result = ExtractDeflateRaw(
                        chunk,
                        storedData);
                    break;

                case 2:
                    result =
                        EraCompressionService.DecompressDeflateStream(
                            storedData,
                            chunk.DecompressedSize);
                    break;

                default:
                    throw new InvalidDataException(
                        $"Chunk {chunk.Index} uses unsupported " +
                        $"compression method {chunk.CompressionMethod}.");
            }

            ValidateDecompressedSize(
                chunk,
                result);

            return result;
        }

        public static string ExtractFile(
            EraArchiveInfo archive,
            EraChunkInfo chunk,
            string destinationFile)
        {
            byte[] data =
                ExtractChunk(
                    archive,
                    chunk);

            string? directory =
                Path.GetDirectoryName(
                    destinationFile);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllBytes(
                destinationFile,
                data);

            return destinationFile;
        }

        public static int ExtractAll(
            EraArchiveInfo archive,
            string destinationDirectory)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException(
                    "Destination directory cannot be empty.",
                    nameof(destinationDirectory));
            }

            string root =
                Path.GetFullPath(
                    destinationDirectory);

            Directory.CreateDirectory(
                root);

            int extracted = 0;

            for (int i = 1;
                 i < archive.Chunks.Count;
                 i++)
            {
                EraChunkInfo chunk =
                    archive.Chunks[i];

                if (string.IsNullOrWhiteSpace(
                    chunk.FileName))
                {
                    throw new InvalidDataException(
                        $"Chunk {chunk.Index} does not have a filename.");
                }

                string destination =
                    GetSafeDestinationPath(
                        root,
                        chunk.FileName);

                ExtractFile(
                    archive,
                    chunk,
                    destination);

                extracted++;
            }

            return extracted;
        }

        private static byte[] ReadStoredChunkData(
            FileStream stream,
            bool encrypted,
            EraChunkInfo chunk)
        {
            if (chunk.CompressedSize > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Chunk {chunk.Index} is too large.");
            }

            int size =
                checked(
                    (int)chunk.CompressedSize);

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

        private static byte[] ExtractStored(
            EraChunkInfo chunk,
            byte[] storedData)
        {
            if (storedData.Length !=
                chunk.DecompressedSize)
            {
                throw new InvalidDataException(
                    $"Stored chunk {chunk.Index} has size " +
                    $"{storedData.Length:N0}, but expected " +
                    $"{chunk.DecompressedSize:N0}.");
            }

            return storedData;
        }

        private static byte[] ExtractDeflateRaw(
            EraChunkInfo chunk,
            byte[] compressedData)
        {
            using MemoryStream input =
                new MemoryStream(
                    compressedData,
                    writable: false);

            using DeflateStream inflate =
                new DeflateStream(
                    input,
                    CompressionMode.Decompress,
                    leaveOpen: false);

            int initialCapacity = 0;

            if (chunk.DecompressedSize <= int.MaxValue)
            {
                initialCapacity =
                    checked(
                        (int)chunk.DecompressedSize);
            }

            using MemoryStream output =
                initialCapacity > 0
                    ? new MemoryStream(initialCapacity)
                    : new MemoryStream();

            inflate.CopyTo(
                output);

            return output.ToArray();
        }

        private static void ValidateCompressedData(
            EraChunkInfo chunk,
            byte[] data)
        {
            uint actualAdler32 =
                EraCompressionService.Adler32(
                    data);

            if (actualAdler32 !=
                chunk.Adler32)
            {
                throw new InvalidDataException(
                    $"Chunk {chunk.Index} failed its compressed " +
                    $"Adler32 check.\n\n" +
                    $"File: {chunk.FileName}\n" +
                    $"Expected: 0x{chunk.Adler32:X8}\n" +
                    $"Actual:   0x{actualAdler32:X8}");
            }
        }

        private static void ValidateDecompressedSize(
            EraChunkInfo chunk,
            byte[] data)
        {
            if (data.LongLength !=
                chunk.DecompressedSize)
            {
                throw new InvalidDataException(
                    $"Chunk {chunk.Index} decompressed to " +
                    $"{data.LongLength:N0} bytes, but the ERA " +
                    $"specifies {chunk.DecompressedSize:N0} bytes.\n\n" +
                    $"File: {chunk.FileName}");
            }
        }

        private static string GetSafeDestinationPath(
            string root,
            string archivePath)
        {
            string relative =
                archivePath
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar);

            relative =
                relative.TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        root,
                        relative));

            string rootWithSeparator =
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsafe path inside ERA: {archivePath}");
            }

            return fullPath;
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer)
        {
            int total = 0;

            while (total <
                   buffer.Length)
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
    }
}