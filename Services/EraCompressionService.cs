using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Ensemble.Services
{
    internal static class EraCompressionService
    {
        private const uint DeflateStreamSignature =
            0xCC34EEAD;

        private const uint DeflateStreamEndMagic =
            0xA5D91776;

        private const int DeflateStreamHeaderSize =
            36;

        /// <summary>
        /// Decompresses Ensemble Studios' BDeflateStream format.
        ///
        /// Layout:
        ///
        /// 36-byte BDeflateStreamHeader
        /// Raw DEFLATE data
        /// 4-byte end-of-stream magic
        /// </summary>
        public static byte[] DecompressDeflateStream(
            ReadOnlySpan<byte> data,
            uint expectedDecompressedSize)
        {
            if (data.Length <
                DeflateStreamHeaderSize + sizeof(uint))
            {
                throw new InvalidDataException(
                    "Deflate Stream data is too small.");
            }

            uint signature =
                ReadUInt32(data, 0);

            uint headerAdler32 =
                ReadUInt32(data, 4);

            uint headerType =
                ReadUInt32(data, 8);

            ulong sourceBytes =
                ReadUInt64(data, 12);

            ulong destinationBytes =
                ReadUInt64(data, 20);

            uint sourceAdler32 =
                ReadUInt32(data, 28);

            uint destinationAdler32 =
                ReadUInt32(data, 32);

            if (signature !=
                DeflateStreamSignature)
            {
                throw new InvalidDataException(
                    $"Invalid Halo Wars Deflate Stream signature. " +
                    $"Expected 0x{DeflateStreamSignature:X8}, " +
                    $"found 0x{signature:X8}.");
            }

            ValidateHeaderAdler32(
                headerAdler32,
                headerType,
                sourceBytes,
                destinationBytes,
                sourceAdler32,
                destinationAdler32);

            if (sourceBytes > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Decompressed stream is too large.");
            }

            if (destinationBytes > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Compressed stream is too large.");
            }

            int compressedSize =
                checked((int)destinationBytes);

            int compressedOffset =
                DeflateStreamHeaderSize;

            int endMagicOffset =
                checked(
                    compressedOffset +
                    compressedSize);

            if (endMagicOffset + sizeof(uint) >
                data.Length)
            {
                throw new InvalidDataException(
                    "Deflate Stream is truncated.");
            }

            uint endMagic =
                ReadUInt32(
                    data,
                    endMagicOffset);

            if (endMagic !=
                DeflateStreamEndMagic)
            {
                throw new InvalidDataException(
                    $"Invalid Deflate Stream end marker. " +
                    $"Expected 0x{DeflateStreamEndMagic:X8}, " +
                    $"found 0x{endMagic:X8}.");
            }

            ReadOnlySpan<byte> compressedSpan =
                data.Slice(
                    compressedOffset,
                    compressedSize);

            uint actualCompressedAdler =
                Adler32(compressedSpan);

            if (actualCompressedAdler !=
                destinationAdler32)
            {
                throw new InvalidDataException(
                    "Compressed filename data failed its Adler32 check.");
            }

            byte[] compressedBytes =
                compressedSpan.ToArray();

            byte[] decompressedBytes;

            using (
                MemoryStream input =
                    new MemoryStream(
                        compressedBytes,
                        writable: false))
            using (
                DeflateStream deflate =
                    new DeflateStream(
                        input,
                        CompressionMode.Decompress))
            using (
                MemoryStream output =
                    new MemoryStream(
                        checked((int)sourceBytes)))
            {
                deflate.CopyTo(output);

                decompressedBytes =
                    output.ToArray();
            }

            if ((ulong)decompressedBytes.Length !=
                sourceBytes)
            {
                throw new InvalidDataException(
                    $"Deflate Stream decompressed to " +
                    $"{decompressedBytes.Length:N0} bytes, " +
                    $"but its header specifies " +
                    $"{sourceBytes:N0} bytes.");
            }

            if (expectedDecompressedSize != 0 &&
                decompressedBytes.Length !=
                expectedDecompressedSize)
            {
                throw new InvalidDataException(
                    $"Filename table decompressed to " +
                    $"{decompressedBytes.Length:N0} bytes, " +
                    $"but the ERA chunk specifies " +
                    $"{expectedDecompressedSize:N0} bytes.");
            }

            uint actualSourceAdler =
                Adler32(decompressedBytes);

            if (actualSourceAdler !=
                sourceAdler32)
            {
                throw new InvalidDataException(
                    "Decompressed filename data failed its Adler32 check.");
            }

            return decompressedBytes;
        }

        private static void ValidateHeaderAdler32(
            uint expectedAdler32,
            uint headerType,
            ulong sourceBytes,
            ulong destinationBytes,
            uint sourceAdler32,
            uint destinationAdler32)
        {
            // This is deliberately NOT the serialized
            // order of the header.
            //
            // Ensemble's BDeflateStreamHeader::computeAdler32()
            // calculates the checksum over the in-memory
            // big-endian structure beginning at mSrcBytes:
            //
            // mSrcBytes
            // mSrcAdler32
            // mDstBytes
            // mDstAdler32
            // mHeaderType

            Span<byte> headerData =
                stackalloc byte[28];

            BinaryPrimitives.WriteUInt64BigEndian(
                headerData.Slice(0, 8),
                sourceBytes);

            BinaryPrimitives.WriteUInt32BigEndian(
                headerData.Slice(8, 4),
                sourceAdler32);

            BinaryPrimitives.WriteUInt64BigEndian(
                headerData.Slice(12, 8),
                destinationBytes);

            BinaryPrimitives.WriteUInt32BigEndian(
                headerData.Slice(20, 4),
                destinationAdler32);

            BinaryPrimitives.WriteUInt32BigEndian(
                headerData.Slice(24, 4),
                headerType);

            uint actual =
                Adler32(headerData);

            if (actual != expectedAdler32)
            {
                throw new InvalidDataException(
                    $"Deflate Stream header failed its Adler32 check. " +
                    $"Expected 0x{expectedAdler32:X8}, " +
                    $"calculated 0x{actual:X8}.");
            }
        }

        public static uint Adler32(
            ReadOnlySpan<byte> data)
        {
            const uint ModAdler =
                65521;

            uint a = 1;
            uint b = 0;

            foreach (byte value in data)
            {
                a += value;

                if (a >= ModAdler)
                    a -= ModAdler;

                b += a;

                if (b >= ModAdler)
                    b %= ModAdler;
            }

            return
                (b << 16) | a;
        }

        private static uint ReadUInt32(
            ReadOnlySpan<byte> data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt32BigEndian(
                        data.Slice(
                            offset,
                            4));
        }

        private static ulong ReadUInt64(
            ReadOnlySpan<byte> data,
            int offset)
        {
            return
                BinaryPrimitives
                    .ReadUInt64BigEndian(
                        data.Slice(
                            offset,
                            8));
        }

        public static byte[] CompressDeflateStream(
            ReadOnlySpan<byte> data)
        {
            byte[] source =
                data.ToArray();

            byte[] compressed;

            using (MemoryStream compressedStream =
                   new MemoryStream())
            {
                using (DeflateStream deflate =
                       new DeflateStream(
                           compressedStream,
                           CompressionLevel.Optimal,
                           leaveOpen: true))
                {
                    deflate.Write(
                        source,
                        0,
                        source.Length);
                }

                compressed =
                    compressedStream.ToArray();
            }

            uint sourceAdler =
                Adler32(source);

            uint compressedAdler =
                Adler32(compressed);

            const uint signature =
                0xCC34EEAD;

            const uint headerType =
                0;

            const uint endMagic =
                0xA5D91776;

            ulong sourceBytes =
                (ulong)source.Length;

            ulong compressedBytes =
                (ulong)compressed.Length;

            // Ensemble calculates the header Adler32 over the
            // big-endian in-memory fields beginning at mSrcBytes:
            //
            // SrcBytes
            // SrcAdler32
            // DstBytes
            // DstAdler32
            // HeaderType

            Span<byte> adlerData =
                stackalloc byte[28];

            BinaryPrimitives.WriteUInt64BigEndian(
                adlerData.Slice(0, 8),
                sourceBytes);

            BinaryPrimitives.WriteUInt32BigEndian(
                adlerData.Slice(8, 4),
                sourceAdler);

            BinaryPrimitives.WriteUInt64BigEndian(
                adlerData.Slice(12, 8),
                compressedBytes);

            BinaryPrimitives.WriteUInt32BigEndian(
                adlerData.Slice(20, 4),
                compressedAdler);

            BinaryPrimitives.WriteUInt32BigEndian(
                adlerData.Slice(24, 4),
                headerType);

            uint headerAdler =
                Adler32(adlerData);

            byte[] result =
                new byte[
                    36 +
                    compressed.Length +
                    4];

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(0, 4),
                signature);

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(4, 4),
                headerAdler);

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(8, 4),
                headerType);

            BinaryPrimitives.WriteUInt64BigEndian(
                result.AsSpan(12, 8),
                sourceBytes);

            BinaryPrimitives.WriteUInt64BigEndian(
                result.AsSpan(20, 8),
                compressedBytes);

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(28, 4),
                sourceAdler);

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(32, 4),
                compressedAdler);

            Buffer.BlockCopy(
                compressed,
                0,
                result,
                36,
                compressed.Length);

            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(
                    36 + compressed.Length,
                    4),
                endMagic);

            return result;
        }
    }
}