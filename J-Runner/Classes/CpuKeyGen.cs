using System;
using System.Collections;
using System.Security.Cryptography;

namespace JRunner
{
    public static class CpuKeyGen
    {
   
        private static readonly int[] PopCountTable = new int[256]
        {
            0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4,
            1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5,
            1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7,
            1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7,
            2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6,
            3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7,
            3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7,
            4, 5, 5, 6, 5, 6, 6, 7, 5, 6, 6, 7, 6, 7, 7, 8
        };
        private static int PopCount(byte value)
        {
            return PopCountTable[value];
        }
        public static string GenerateKey()
        {
            var rng = RandomNumberGenerator.Create();
            byte[] key = new byte[16];
            byte[] generatedKey;

            do
            {
                rng.GetNonZeroBytes(key);
            }
            while (!VerifyKey(key, out generatedKey));

            return BitConverter.ToString(generatedKey).Replace("-", string.Empty);
        }

        private static bool VerifyKey(byte[] key, out byte[] generatedKey)
        {
            generatedKey = null;

            // Calculate Hamming weight using efficient bit counting
            int hamming = 0;
            for (int i = 0; i < 13; i++)
            {
                hamming += PopCount(key[i]);
            }

            // Check bits 0 and 1 of byte 13 using bitwise operations
            hamming += (key[13] & 0b00000001) + ((key[13] & 0b00000010) >> 1);

            if (hamming != 53) return false;

            generatedKey = CalculateCPUKeyECD(key);
            return true;
        }

        private static byte[] CalculateCPUKeyECD(byte[] key)
        {
            byte[] ecd = new byte[0x10];
            Buffer.BlockCopy(key, 0, ecd, 0, 0x10);

            uint acc1 = 0, acc2 = 0;

            // Process bits 0 to 105 (0x6A iterations)
            for (int cnt = 0; cnt < 0x6A; cnt++)
            {
                int byteIndex = cnt >> 3;
                int bitIndex = cnt & 7;
                byte b = ecd[byteIndex];
                uint bit = (uint)((b >> bitIndex) & 1);

                acc1 ^= bit;
                acc1 ^= (acc1 & 1) * 0x360325U;
                acc2 ^= bit;

                acc1 >>= 1;
            }

            // Process bits 106 to 126 (21 iterations)
            for (int cnt = 0x6A; cnt < 0x7F; cnt++)
            {
                int byteIndex = cnt >> 3;
                int bitIndex = cnt & 7;
                byte b = ecd[byteIndex];
                uint bit = (uint)((b >> bitIndex) & 1);

                uint lsb = acc1 & 1;
                ecd[byteIndex] ^= (byte)(((bit ^ lsb) & 1) << bitIndex);
                acc2 ^= lsb;

                acc1 >>= 1;
            }

            // Process bit 127
            {
                uint bit = (uint)((ecd[15] >> 7) & 1);
                ecd[15] ^= (byte)(((bit ^ (acc2 & 1)) & 1) << 7);
            }

            return ecd;
        }
    }
}
