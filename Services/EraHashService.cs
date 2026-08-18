using Org.BouncyCastle.Crypto.Digests;
using System.Buffers.Binary;
using System.IO;

namespace Ensemble.Services
{
    internal static class EraHashService
    {
        /// <summary>
        /// Calculates a Tiger-192 hash in the byte layout
        /// Halo Wars expects.
        ///
        /// Bouncy Castle outputs each Tiger QWORD little-endian.
        /// Halo Wars BTigerHash stores the three QWORDs
        /// big-endian.
        /// </summary>
        public static byte[] Tiger192(
            ReadOnlySpan<byte> data)
        {
            TigerDigest digest =
                new TigerDigest();

            byte[] input =
                data.ToArray();

            digest.BlockUpdate(
                input,
                0,
                input.Length);

            byte[] bouncyResult =
                new byte[
                    digest.GetDigestSize()];

            digest.DoFinal(
                bouncyResult,
                0);

            if (bouncyResult.Length != 24)
            {
                throw new InvalidOperationException(
                    $"Unexpected Tiger digest size: " +
                    $"{bouncyResult.Length}.");
            }

            byte[] haloWarsResult =
                new byte[24];

            // -------------------------------------------------
            // Bouncy Castle:
            //
            // QWORD 0 = little endian
            // QWORD 1 = little endian
            // QWORD 2 = little endian
            //
            // Halo Wars BTigerHash:
            //
            // QWORD 0 = big endian
            // QWORD 1 = big endian
            // QWORD 2 = big endian
            //
            // Convert each individual 8-byte word.
            // -------------------------------------------------

            for (int qword = 0;
                 qword < 3;
                 qword++)
            {
                int offset =
                    qword * 8;

                ulong value =
                    BinaryPrimitives
                        .ReadUInt64LittleEndian(
                            bouncyResult.AsSpan(
                                offset,
                                8));

                BinaryPrimitives
                    .WriteUInt64BigEndian(
                        haloWarsResult.AsSpan(
                            offset,
                            8),
                        value);
            }

            return haloWarsResult;
        }

        /// <summary>
        /// Halo Wars stores the first 128 bits of the
        /// engine-format Tiger hash for compressed chunk data.
        /// </summary>
        public static byte[] Tiger128(
            ReadOnlySpan<byte> data)
        {
            byte[] tiger192 =
                Tiger192(
                    data);

            byte[] result =
                new byte[16];

            Buffer.BlockCopy(
                tiger192,
                0,
                result,
                0,
                16);

            return result;
        }

        /// <summary>
        /// Halo Wars ERA chunk ID is the first big-endian
        /// QWORD of the Tiger hash of the decompressed data.
        /// </summary>
        public static ulong Tiger64(
            ReadOnlySpan<byte> data)
        {
            byte[] tiger192 =
                Tiger192(
                    data);

            return BinaryPrimitives
                .ReadUInt64BigEndian(
                    tiger192.AsSpan(
                        0,
                        8));
        }

        public static ulong ComputeReplacementTiger64(
            ulong existingId,
            ReadOnlySpan<byte> existingData,
            ReadOnlySpan<byte> replacementData)
        {
            // -------------------------------------------------
            // First verify that our Tiger implementation now
            // reproduces the shipping ERA chunk ID.
            // -------------------------------------------------

            ulong calculatedExistingId =
                Tiger64(
                    existingData);

            if (calculatedExistingId !=
                existingId)
            {
                throw new InvalidDataException(
                    "Tiger64 verification failed against " +
                    "the original Halo Wars ERA chunk.\n\n" +

                    $"ERA ID:       0x{existingId:X16}\n" +
                    $"Calculated:   0x{calculatedExistingId:X16}\n\n" +

                    "Ensemble will not generate a replacement " +
                    "chunk until the original hash is reproduced.");
            }

            return Tiger64(
                replacementData);
        }
    }
}