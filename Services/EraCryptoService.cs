using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ensemble.Services
{
    internal static class EraCryptoService
    {
        public const int BlockSize = 64;

        private const ulong DefaultIv =
            0x15EF0AF334248FE2UL;

        private const string EraPassword =
            "3zDdptN*rV=qOkRbE*NAuWM6";

        private static readonly uint[] TeaDecipherSums =
        {
            0xE3779B90,
            0x454021D7,
            0xA708A81E,
            0x08D12E65,
            0x6A99B4AC,
            0xCC623AF3,
            0x2E2AC13A,
            0x8FF34781,
            0xF1BBCDC8,
            0x5384540F,
            0xB54CDA56,
            0x1715609D,
            0x78DDE6E4,
            0xDAA66D2B,
            0x3C6EF372,
            0x9E3779B9
        };

        public static byte[] DecryptFirstBlock(
            ReadOnlySpan<byte> encrypted)
        {
            if (encrypted.Length < BlockSize)
            {
                throw new InvalidDataException(
                    $"ERA data must contain at least {BlockSize} bytes.");
            }

            (ulong key1, ulong key2, ulong key3) =
                CreateKeys(EraPassword);

            byte[] output =
                new byte[BlockSize];

            DecryptBlock(
                encrypted.Slice(0, BlockSize),
                output,
                key1,
                key2,
                key3,
                0);

            return output;
        }

        /// <summary>
        /// Reads and decrypts an arbitrary range from an encrypted ERA.
        /// The underlying encryption operates on independent 64-byte blocks.
        /// </summary>
        public static byte[] DecryptRange(
            Stream stream,
            long offset,
            int length)
        {
            if (!stream.CanSeek)
            {
                throw new ArgumentException(
                    "The source stream must be seekable.",
                    nameof(stream));
            }

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0)
                return Array.Empty<byte>();

            long alignedStart =
                (offset / BlockSize) * BlockSize;

            long requestedEnd =
                offset + length;

            long alignedEnd =
                ((requestedEnd + BlockSize - 1) /
                 BlockSize) * BlockSize;

            long bytesNeededLong =
                alignedEnd - alignedStart;

            if (bytesNeededLong > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Requested ERA range is too large.");
            }

            int bytesNeeded =
                (int)bytesNeededLong;

            byte[] encrypted =
                new byte[bytesNeeded];

            stream.Position =
                alignedStart;

            int totalRead = 0;

            while (totalRead < encrypted.Length)
            {
                int read =
                    stream.Read(
                        encrypted,
                        totalRead,
                        encrypted.Length - totalRead);

                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead < encrypted.Length)
            {
                throw new EndOfStreamException(
                    "Unexpected end of encrypted ERA data.");
            }

            byte[] decrypted =
                new byte[encrypted.Length];

            (ulong key1, ulong key2, ulong key3) =
                CreateKeys(EraPassword);

            ulong startingBlock =
                (ulong)(alignedStart / BlockSize);

            int blockCount =
                encrypted.Length / BlockSize;

            for (int i = 0; i < blockCount; i++)
            {
                int blockOffset =
                    i * BlockSize;

                ulong counter64 =
                    startingBlock + (ulong)i;

                if (counter64 > uint.MaxValue)
                {
                    throw new InvalidDataException(
                        "ERA encryption counter exceeded supported range.");
                }

                DecryptBlock(
                    encrypted.AsSpan(
                        blockOffset,
                        BlockSize),

                    decrypted.AsSpan(
                        blockOffset,
                        BlockSize),

                    key1,
                    key2,
                    key3,
                    (uint)counter64);
            }

            int copyOffset =
                (int)(offset - alignedStart);

            byte[] result =
                new byte[length];

            Buffer.BlockCopy(
                decrypted,
                copyOffset,
                result,
                0,
                length);

            return result;
        }

        private static (
            ulong Key1,
            ulong Key2,
            ulong Key3)
            CreateKeys(string keyPhrase)
        {
            byte[] phrase =
                Encoding.ASCII.GetBytes(keyPhrase);

            byte[] firstInput =
                new byte[4 + phrase.Length + 4 + 4];

            int offset = 0;

            BinaryPrimitives.WriteUInt32BigEndian(
                firstInput.AsSpan(offset, 4),
                0xA4800C14);

            offset += 4;

            phrase.CopyTo(
                firstInput.AsSpan(
                    offset,
                    phrase.Length));

            offset += phrase.Length;

            BinaryPrimitives.WriteUInt32BigEndian(
                firstInput.AsSpan(offset, 4),
                0x5AF4A9F1);

            offset += 4;

            BinaryPrimitives.WriteUInt32BigEndian(
                firstInput.AsSpan(offset, 4),
                0xCA6884EC);

            byte[] hash1;

            using (SHA1 sha1 = SHA1.Create())
            {
                hash1 =
                    sha1.ComputeHash(firstInput);
            }

            byte[] secondInput =
                new byte[4 + hash1.Length + 4];

            offset = 0;

            BinaryPrimitives.WriteUInt32BigEndian(
                secondInput.AsSpan(offset, 4),
                0xCB92EAEB);

            offset += 4;

            hash1.CopyTo(
                secondInput.AsSpan(
                    offset,
                    hash1.Length));

            offset += hash1.Length;

            BinaryPrimitives.WriteUInt32BigEndian(
                secondInput.AsSpan(offset, 4),
                0x1D919BF8);

            byte[] hash2;

            using (SHA1 sha1 = SHA1.Create())
            {
                hash2 =
                    sha1.ComputeHash(secondInput);
            }

            uint h2_0 = ReadShaWord(hash2, 0);
            uint h2_1 = ReadShaWord(hash2, 1);
            uint h2_2 = ReadShaWord(hash2, 2);
            uint h2_3 = ReadShaWord(hash2, 3);
            uint h2_4 = ReadShaWord(hash2, 4);

            uint h1_0 = ReadShaWord(hash1, 0);

            ulong key1 =
                (ulong)h2_0 |
                ((ulong)h2_1 << 32);

            ulong key2 =
                (ulong)h2_2 |
                ((ulong)h2_3 << 32);

            ulong key3 =
                (ulong)h2_4 |
                ((ulong)h1_0 << 32);

            return (key1, key2, key3);
        }

        private static uint ReadShaWord(
            byte[] hash,
            int index)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(
                hash.AsSpan(index * 4, 4));
        }

        public static byte[] EncryptAll(
    ReadOnlySpan<byte> plaintext)
        {
            if ((plaintext.Length %
                 BlockSize) != 0)
            {
                throw new InvalidDataException(
                    "ERA plaintext size must be aligned " +
                    "to the 64-byte encryption block.");
            }

            (
                ulong key1,
                ulong key2,
                ulong key3
            ) =
                CreateKeys(
                    EraPassword);

            byte[] result =
                new byte[
                    plaintext.Length];

            int blocks =
                plaintext.Length /
                BlockSize;

            for (int i = 0;
                 i < blocks;
                 i++)
            {
                int offset =
                    i *
                    BlockSize;

                EncryptBlock(
                    plaintext.Slice(
                        offset,
                        BlockSize),

                    result.AsSpan(
                        offset,
                        BlockSize),

                    key1,
                    key2,
                    key3,

                    checked(
                        (uint)i));
            }

            return result;
        }

        private static void EncryptBlock(
    ReadOnlySpan<byte> source,
    Span<byte> destination,
    ulong key1,
    ulong key2,
    ulong key3,
    uint counter)
        {
            ulong v0 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(0, 8));

            ulong v1 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(8, 8));

            ulong v2 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(16, 8));

            ulong v3 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(24, 8));

            ulong v4 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(32, 8));

            ulong v5 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(40, 8));

            ulong v6 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(48, 8));

            ulong v7 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(56, 8));

            unchecked
            {
                counter +=
                    (uint)(
                        DefaultIv >>
                        10);

                if (counter == 0)
                    counter++;

                counter =
                    Lfsr3(counter);

                v0 ^=
                    (ulong)counter +
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v1 ^=
                    (ulong)counter -
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v2 ^=
                    (ulong)counter +
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v3 ^=
                    (ulong)counter -
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v4 ^=
                    (ulong)counter +
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v5 ^=
                    (ulong)counter -
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v6 ^=
                    (ulong)counter +
                    DefaultIv;

                counter =
                    Lfsr3(counter);

                v7 ^=
                    (ulong)counter -
                    DefaultIv;
            }

            ulong w0 =
                TeaEncipher16(
                    v0,
                    key2,
                    key1);

            ulong w1 =
                TeaEncipher16(
                    v1,
                    key2,
                    key1);

            ulong w2 =
                TeaEncipher16(
                    v2,
                    key2,
                    key1);

            ulong w3 =
                TeaEncipher16(
                    v3,
                    key2,
                    key1);

            BlockDisperse(
                ref w0,
                ref w1,
                ref w2,
                ref w3);

            BlockDisperse(
                ref w0,
                ref w1,
                ref w2,
                ref w3);

            BlockDisperse(
                ref w0,
                ref w1,
                ref w2,
                ref w3);

            unchecked
            {
                v4 ^=
                    w3;

                v5 -=
                    w2;

                v6 ^=
                    w1;

                v7 +=
                    w0;
            }

            ulong w4 =
                TeaEncipher16(
                    v4,
                    key2,
                    key3);

            ulong w5 =
                TeaEncipher16(
                    v5,
                    key2,
                    key3);

            ulong w6 =
                TeaEncipher16(
                    v6,
                    key2,
                    key3);

            ulong w7 =
                TeaEncipher16(
                    v7,
                    key2,
                    key3);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            unchecked
            {
                w0 +=
                    w7;

                w1 ^=
                    w6;

                w2 -=
                    w5;

                w3 ^=
                    w4;
            }

            v0 =
                TeaEncipher16(
                    w0,
                    key1,
                    key2);

            v1 =
                TeaEncipher16(
                    w1,
                    key1,
                    key2);

            v2 =
                TeaEncipher16(
                    w2,
                    key1,
                    key2);

            v3 =
                TeaEncipher16(
                    w3,
                    key1,
                    key2);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            BlockDisperse(
                ref w4,
                ref w5,
                ref w6,
                ref w7);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(0, 8),
                v0);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(8, 8),
                v1);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(16, 8),
                v2);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(24, 8),
                v3);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(32, 8),
                w4);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(40, 8),
                w5);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(48, 8),
                w6);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(56, 8),
                w7);
        }

        private static ulong TeaEncipher16(
    ulong value,
    ulong key0,
    ulong key1)
        {
            uint a =
                (uint)key0;

            uint b =
                (uint)(
                    key0 >>
                    32);

            uint c =
                (uint)key1;

            uint d =
                (uint)(
                    key1 >>
                    32);

            uint y =
                (uint)value;

            uint z =
                (uint)(
                    value >>
                    32);

            uint sum =
                0;

            const uint delta =
                0x9E3779B9;

            unchecked
            {
                for (int i = 0;
                     i < 16;
                     i++)
                {
                    sum +=
                        delta;

                    y +=
                        (z << 4) +
                        (a ^ z) +
                        (sum ^
                         (z >> 5)) +
                        b;

                    z +=
                        (y << 4) +
                        (c ^ y) +
                        (sum ^
                         (y >> 5)) +
                        d;
                }
            }

            return
                (ulong)y |
                ((ulong)z <<
                 32);
        }

        private static void BlockDisperse(
            ref ulong x,
            ref ulong y,
            ref ulong z,
            ref ulong w)
        {
            x ^=
                y;

            y ^=
                z;

            z ^=
                w;

            w ^=
                x;
        }

        private static void DecryptBlock(
            ReadOnlySpan<byte> source,
            Span<byte> destination,
            ulong key1,
            ulong key2,
            ulong key3,
            uint counter)
        {
            ulong v0 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(0, 8));

            ulong v1 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(8, 8));

            ulong v2 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(16, 8));

            ulong v3 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(24, 8));

            ulong v4 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(32, 8));

            ulong v5 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(40, 8));

            ulong v6 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(48, 8));

            ulong v7 =
                BinaryPrimitives.ReadUInt64BigEndian(
                    source.Slice(56, 8));

            BlockContract(ref v4, ref v5, ref v6, ref v7);
            BlockContract(ref v4, ref v5, ref v6, ref v7);
            BlockContract(ref v4, ref v5, ref v6, ref v7);

            ulong w0 =
                TeaDecipher16(v0, key1, key2);

            ulong w1 =
                TeaDecipher16(v1, key1, key2);

            ulong w2 =
                TeaDecipher16(v2, key1, key2);

            ulong w3 =
                TeaDecipher16(v3, key1, key2);

            unchecked
            {
                w0 -= v7;
                w1 ^= v6;
                w2 += v5;
                w3 ^= v4;
            }

            BlockContract(ref v4, ref v5, ref v6, ref v7);
            BlockContract(ref v4, ref v5, ref v6, ref v7);
            BlockContract(ref v4, ref v5, ref v6, ref v7);

            ulong w4 =
                TeaDecipher16(v4, key2, key3);

            ulong w5 =
                TeaDecipher16(v5, key2, key3);

            ulong w6 =
                TeaDecipher16(v6, key2, key3);

            ulong w7 =
                TeaDecipher16(v7, key2, key3);

            unchecked
            {
                w4 ^= w3;
                w5 += w2;
                w6 ^= w1;
                w7 -= w0;
            }

            BlockContract(ref w0, ref w1, ref w2, ref w3);
            BlockContract(ref w0, ref w1, ref w2, ref w3);
            BlockContract(ref w0, ref w1, ref w2, ref w3);

            v0 =
                TeaDecipher16(w0, key2, key1);

            v1 =
                TeaDecipher16(w1, key2, key1);

            v2 =
                TeaDecipher16(w2, key2, key1);

            v3 =
                TeaDecipher16(w3, key2, key1);

            unchecked
            {
                counter +=
                    (uint)(DefaultIv >> 10);

                if (counter == 0)
                    counter++;

                counter = Lfsr3(counter);
                v0 ^= (ulong)counter + DefaultIv;

                counter = Lfsr3(counter);
                v1 ^= (ulong)counter - DefaultIv;

                counter = Lfsr3(counter);
                v2 ^= (ulong)counter + DefaultIv;

                counter = Lfsr3(counter);
                v3 ^= (ulong)counter - DefaultIv;

                counter = Lfsr3(counter);
                w4 ^= (ulong)counter + DefaultIv;

                counter = Lfsr3(counter);
                w5 ^= (ulong)counter - DefaultIv;

                counter = Lfsr3(counter);
                w6 ^= (ulong)counter + DefaultIv;

                counter = Lfsr3(counter);
                w7 ^= (ulong)counter - DefaultIv;
            }

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(0, 8),
                v0);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(8, 8),
                v1);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(16, 8),
                v2);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(24, 8),
                v3);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(32, 8),
                w4);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(40, 8),
                w5);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(48, 8),
                w6);

            BinaryPrimitives.WriteUInt64BigEndian(
                destination.Slice(56, 8),
                w7);
        }

        private static ulong TeaDecipher16(
            ulong value,
            ulong key0,
            ulong key1)
        {
            uint a = (uint)key0;
            uint b = (uint)(key0 >> 32);

            uint c = (uint)key1;
            uint d = (uint)(key1 >> 32);

            uint y = (uint)value;
            uint z = (uint)(value >> 32);

            unchecked
            {
                foreach (uint sum in TeaDecipherSums)
                {
                    z -=
                        (y << 4) +
                        (c ^ y) +
                        (sum ^ (y >> 5)) +
                        d;

                    y -=
                        (z << 4) +
                        (a ^ z) +
                        (sum ^ (z >> 5)) +
                        b;
                }
            }

            return
                (ulong)y |
                ((ulong)z << 32);
        }

        private static void BlockContract(
            ref ulong x,
            ref ulong y,
            ref ulong z,
            ref ulong w)
        {
            w ^= x;
            z ^= w;
            y ^= z;
            x ^= y;
        }

        private static uint Lfsr3(uint value)
        {
            unchecked
            {
                value ^= value << 17;
                value ^= value >> 13;
                value ^= value << 5;
            }

            return value;
        }
    }
}