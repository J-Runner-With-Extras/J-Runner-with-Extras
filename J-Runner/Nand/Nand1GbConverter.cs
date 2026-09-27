using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JRunner.Nand
{
    internal sealed class Nand1GbConversionReport
    {
        public int SourceValidEccPages;
        public int SourceInvalidEccPages;
        public int[] SourceBadBlocks;
        public Dictionary<int, int> AppliedRemaps;
        public int[] BadBlocksWithoutRemaps;
        public int TranslatedFileSystemMetadataPages;
        public int SynthesizedMetadataPages;
        public int OutputProgrammedPages;
        public int OutputInvalidEccPages;
        public byte SmcConfigVersion;
        public uint OldHeaderSize;
        public uint NewHeaderSize;
        public uint OldSysUpdateAddress;
        public uint NewSysUpdateAddress;
        public uint OldFileSystemBlockSize;
        public uint NewFileSystemBlockSize;
    }

    internal static class Nand1GbConverter
    {
        private const int DataSize = 0x200;
        private const int SpareSize = 0x10;
        private const int RawPageSize = 0x210;
        private const int SystemDataSize = 0x4000000;
        private const int SystemRawSize = 0x4200000;
        private const long Full256MbRawSize = 0x10800000L;
        private const long Full512MbRawSize = 0x21000000L;
        private const int SectorCount = SystemDataSize / DataSize;

        private const int SourcePagesPerBlock = 0x100;
        private const int TargetPagesPerBlock = 0x200;
        private const int SourceBlockSize = SourcePagesPerBlock * DataSize;
        private const int TargetBlockSize = TargetPagesPerBlock * DataSize;
        private const int SourceReserveStartBlock = 0x1E0;
        private const int TargetReserveStartBlock = 0xF0;

        private const int SourceFileSystemStart = 0x00B80000;
        private const int TargetFileSystemStart = 0x00B00000;
        private const int FileSystemSize = 0x03000000;
        private const int SourceConfigStart = 0x03B80000;
        private const int TargetConfigStart = 0x03B00000;
        private const int TargetSmcConfigAddress = 0x03BC0000;

        private const int HeaderSizeOffset = 0x0C;
        private const int HeaderSysUpdateOffset = 0x64;
        private const int HeaderFileSystemBlockSizeOffset = 0x70;
        private const uint EccPolynomial = 0x06954559;
        private const int EccCoveredBits = 0x1066;
        private static readonly uint[] EccTable = MakeEccTable();

        internal static Nand1GbConversionReport Convert(string inputPath, string outputPath, Action<int> progress)
        {
            string input = Path.GetFullPath(inputPath);
            string output = Path.GetFullPath(outputPath);
            if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Input and output paths must be different.");

            long inputLength = new FileInfo(input).Length;
            if (inputLength == 0x42000000L)
                throw new InvalidDataException("The source is already a full 1 GB NAND image.");
            if (inputLength != SystemRawSize && inputLength != Full256MbRawSize && inputLength != Full512MbRawSize)
            {
                throw new InvalidDataException(String.Format(
                    "Expected a raw 64, 256, or 512 MB NAND image (0x{0:X}, 0x{1:X}, or 0x{2:X} bytes); got 0x{3:X} bytes.",
                    SystemRawSize, Full256MbRawSize, Full512MbRawSize, inputLength));
            }

            BadBlock.NandGeometry sourceGeometry = BadBlock.GetGeometry(input);
            if (sourceGeometry.PagesPerBlock == TargetPagesPerBlock)
                throw new InvalidDataException("The source already uses 1 GB NAND geometry.");
            if (sourceGeometry.PagesPerBlock != SourcePagesPerBlock)
                throw new InvalidDataException("The source is not a 256/512 MB PSB/KSB big-block NAND image.");

            byte[] source = new byte[SystemRawSize];
            using (FileStream stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                ReadExactly(stream, source, 0, source.Length);

            if (source[0x4400] != 0xFF)
                throw new InvalidDataException("The source is not a PSB/KSB 256/512 MB big-block image.");

            Report(progress, 2);
            byte[] data = new byte[SystemDataSize];
            byte[] spare = new byte[SectorCount * SpareSize];
            int sourceValid = 0;
            int sourceInvalid = 0;

            for (int sector = 0; sector < SectorCount; sector++)
            {
                int rawOffset = sector * RawPageSize;
                int dataOffset = sector * DataSize;
                int spareOffset = sector * SpareSize;
                Buffer.BlockCopy(source, rawOffset, data, dataOffset, DataSize);
                Buffer.BlockCopy(source, rawOffset + DataSize, spare, spareOffset, SpareSize);
                if (!IsAll(source, rawOffset, RawPageSize, 0xFF))
                {
                    uint stored = ReadUInt32LittleEndian(source, rawOffset + 0x20C) & 0xFFFFFFC0U;
                    uint calculated = CalculateEcc(source, rawOffset);
                    if (stored == calculated) sourceValid++;
                    else sourceInvalid++;
                }
                if ((sector & 0x1FFF) == 0) Report(progress, 2 + (sector * 18 / SectorCount));
            }
            source = null;

            List<int> badBlocks = new List<int>();
            int sourceBlockCount = SectorCount / SourcePagesPerBlock;
            for (int block = 0; block < sourceBlockCount; block++)
            {
                int firstSpare = block * SourcePagesPerBlock * SpareSize;
                if (spare[firstSpare] != 0xFF) badBlocks.Add(block);
            }

            Dictionary<int, int> remaps = new Dictionary<int, int>();
            for (int reserve = SourceReserveStartBlock; reserve < sourceBlockCount; reserve++)
            {
                int firstSpare = reserve * SourcePagesPerBlock * SpareSize;
                if (spare[firstSpare] != 0xFF) continue;
                int original = spare[firstSpare + 1] | (spare[firstSpare + 2] << 8);
                if (original >= SourceReserveStartBlock || !badBlocks.Contains(original) || remaps.ContainsKey(original))
                    continue;

                Buffer.BlockCopy(data, reserve * SourceBlockSize, data, original * SourceBlockSize, SourceBlockSize);
                Buffer.BlockCopy(spare, reserve * SourcePagesPerBlock * SpareSize,
                    spare, original * SourcePagesPerBlock * SpareSize, SourcePagesPerBlock * SpareSize);
                remaps.Add(original, reserve);
            }

            int[] badWithoutRemaps = badBlocks.Where(block => !remaps.ContainsKey(block)).ToArray();

            // The standard 256/512 MB layout (0A/60/04) places the 48 MiB
            // filesystem at 0x00B80000. With 512-page erase blocks the equivalent
            // 1 GB layout is 05/30/04 and starts at 0x00B00000.
            byte[] fileSystemData = new byte[FileSystemSize];
            byte[] fileSystemSpare = new byte[(FileSystemSize / DataSize) * SpareSize];
            Buffer.BlockCopy(data, SourceFileSystemStart, fileSystemData, 0, fileSystemData.Length);
            Buffer.BlockCopy(spare, (SourceFileSystemStart / DataSize) * SpareSize,
                fileSystemSpare, 0, fileSystemSpare.Length);
            Buffer.BlockCopy(fileSystemData, 0, data, TargetFileSystemStart, fileSystemData.Length);
            Buffer.BlockCopy(fileSystemSpare, 0, spare, (TargetFileSystemStart / DataSize) * SpareSize,
                fileSystemSpare.Length);

            int translatedMetadata = 0;
            int targetFsFirstSector = TargetFileSystemStart / DataSize;
            int fileSystemSectorCount = FileSystemSize / DataSize;
            for (int index = 0; index < fileSystemSectorCount; index++)
            {
                int metadata = (targetFsFirstSector + index) * SpareSize;
                if (spare[metadata + 7] != 0x0A || spare[metadata + 8] != 0x60 || spare[metadata + 9] != 0x04)
                    continue;
                spare[metadata + 7] = 0x05;
                spare[metadata + 8] = 0x30;
                translatedMetadata++;
            }
            if (translatedMetadata == 0)
                throw new InvalidDataException("No 256/512 MB filesystem layout metadata (0A/60/04) was found.");

            // Expand each of the four 128 KiB configuration blocks into the first
            // half of a 256 KiB target block. The active SMC config moves to 0x03BC0000.
            byte[] configData = new byte[SourceBlockSize * 4];
            byte[] configSpare = new byte[SourcePagesPerBlock * SpareSize * 4];
            Buffer.BlockCopy(data, SourceConfigStart, configData, 0, configData.Length);
            Buffer.BlockCopy(spare, (SourceConfigStart / DataSize) * SpareSize, configSpare, 0, configSpare.Length);
            Fill(data, TargetConfigStart, TargetBlockSize * 4, 0xFF);
            Fill(spare, (TargetConfigStart / DataSize) * SpareSize, TargetPagesPerBlock * SpareSize * 4, 0xFF);
            for (int block = 0; block < 4; block++)
            {
                int targetData = TargetConfigStart + block * TargetBlockSize;
                Buffer.BlockCopy(configData, block * SourceBlockSize, data, targetData, SourceBlockSize);
                Buffer.BlockCopy(configSpare, block * SourcePagesPerBlock * SpareSize, spare,
                    (targetData / DataSize) * SpareSize, SourcePagesPerBlock * SpareSize);
            }

            Fill(data, TargetReserveStartBlock * TargetBlockSize,
                data.Length - (TargetReserveStartBlock * TargetBlockSize), 0xFF);
            Fill(spare, TargetReserveStartBlock * TargetPagesPerBlock * SpareSize,
                spare.Length - (TargetReserveStartBlock * TargetPagesPerBlock * SpareSize), 0xFF);

            byte smcConfigVersion = data[TargetSmcConfigAddress + 0x0E];
            if (smcConfigVersion != 5)
                throw new InvalidDataException(String.Format(
                    "Relocated SMC config has structure version {0}; Corona/Winchester requires 5.", smcConfigVersion));

            uint oldHeaderSize = ReadUInt32BigEndian(data, HeaderSizeOffset);
            uint oldSysUpdate = ReadUInt32BigEndian(data, HeaderSysUpdateOffset);
            uint oldFsBlockSize = ReadUInt32BigEndian(data, HeaderFileSystemBlockSizeOffset);
            if (oldFsBlockSize != SourceBlockSize)
                throw new InvalidDataException(String.Format(
                    "Unexpected flash-header filesystem block size 0x{0:X}; expected 0x{1:X}.",
                    oldFsBlockSize, SourceBlockSize));
            uint newHeaderSize = AlignUp(oldHeaderSize, (uint)TargetBlockSize);
            uint newSysUpdate = AlignUp(oldSysUpdate, (uint)TargetBlockSize);
            WriteUInt32BigEndian(data, HeaderSizeOffset, newHeaderSize);
            WriteUInt32BigEndian(data, HeaderSysUpdateOffset, newSysUpdate);
            WriteUInt32BigEndian(data, HeaderFileSystemBlockSizeOffset, (uint)TargetBlockSize);

            Report(progress, 35);
            byte[] outputSystem = new byte[SystemRawSize];
            Fill(outputSystem, 0, outputSystem.Length, 0xFF);
            int synthesizedMetadata = 0;
            int programmedPages = 0;
            for (int sector = 0; sector < SectorCount; sector++)
            {
                int dataOffset = sector * DataSize;
                int spareOffset = sector * SpareSize;
                if (IsAll(data, dataOffset, DataSize, 0xFF) && IsAll(spare, spareOffset, SpareSize, 0xFF))
                    continue;

                int rawOffset = sector * RawPageSize;
                Buffer.BlockCopy(data, dataOffset, outputSystem, rawOffset, DataSize);
                if (IsAll(spare, spareOffset, SpareSize, 0xFF))
                {
                    Array.Clear(outputSystem, rawOffset + DataSize, 13);
                    synthesizedMetadata++;
                }
                else
                {
                    Buffer.BlockCopy(spare, spareOffset, outputSystem, rawOffset + DataSize, 13);
                }

                int targetBlock = sector / TargetPagesPerBlock;
                outputSystem[rawOffset + DataSize] = 0xFF;
                outputSystem[rawOffset + DataSize + 1] = (byte)targetBlock;
                outputSystem[rawOffset + DataSize + 2] = (byte)(targetBlock >> 8);
                outputSystem[rawOffset + DataSize + 13] = 0;
                outputSystem[rawOffset + DataSize + 14] = 0;
                outputSystem[rawOffset + DataSize + 15] = 0;
                WriteEcc(outputSystem, rawOffset);
                programmedPages++;
                if ((sector & 0x1FFF) == 0) Report(progress, 35 + (sector * 30 / SectorCount));
            }

            int invalidOutput = 0;
            for (int sector = 0; sector < SectorCount; sector++)
            {
                int rawOffset = sector * RawPageSize;
                if (!IsAll(outputSystem, rawOffset, RawPageSize, 0xFF) &&
                    ((ReadUInt32LittleEndian(outputSystem, rawOffset + 0x20C) & 0xFFFFFFC0U) != CalculateEcc(outputSystem, rawOffset)))
                    invalidOutput++;
                if ((sector & 0x3FFF) == 0) Report(progress, 65 + (sector * 10 / SectorCount));
            }
            if (invalidOutput != 0)
                throw new InvalidDataException(String.Format("Converted system area has {0} ECC failures.", invalidOutput));

            string directory = Path.GetDirectoryName(output);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = output + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                    FileShare.None, 0x100000, FileOptions.SequentialScan))
                {
                    stream.Write(outputSystem, 0, outputSystem.Length);
                    stream.Flush();
                }

                if (File.Exists(output)) File.Delete(output);
                File.Move(temporary, output);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }

            Report(progress, 100);
            return new Nand1GbConversionReport
            {
                SourceValidEccPages = sourceValid,
                SourceInvalidEccPages = sourceInvalid,
                SourceBadBlocks = badBlocks.ToArray(),
                AppliedRemaps = remaps,
                BadBlocksWithoutRemaps = badWithoutRemaps,
                TranslatedFileSystemMetadataPages = translatedMetadata,
                SynthesizedMetadataPages = synthesizedMetadata,
                OutputProgrammedPages = programmedPages,
                OutputInvalidEccPages = invalidOutput,
                SmcConfigVersion = smcConfigVersion,
                OldHeaderSize = oldHeaderSize,
                NewHeaderSize = newHeaderSize,
                OldSysUpdateAddress = oldSysUpdate,
                NewSysUpdateAddress = newSysUpdate,
                OldFileSystemBlockSize = oldFsBlockSize,
                NewFileSystemBlockSize = (uint)TargetBlockSize
            };
        }

        private static void Report(Action<int> progress, int value)
        {
            if (progress != null) progress(Math.Max(0, Math.Min(100, value)));
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

        private static bool IsAll(byte[] data, int offset, int count, byte value)
        {
            int end = offset + count;
            for (int i = offset; i < end; i++)
                if (data[i] != value) return false;
            return true;
        }

        private static void Fill(byte[] data, int offset, int count, byte value)
        {
            int end = offset + count;
            for (int i = offset; i < end; i++) data[i] = value;
        }

        private static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static void WriteUInt32BigEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static uint[] MakeEccTable()
        {
            uint[] table = new uint[256];
            uint reflected = EccPolynomial >> 1;
            for (int input = 0; input < table.Length; input++)
            {
                uint value = (uint)input;
                for (int bit = 0; bit < 8; bit++)
                    value = (value >> 1) ^ (((value & 1) != 0) ? reflected : 0);
                table[input] = value;
            }
            return table;
        }

        private static uint CalculateEcc(byte[] page, int offset)
        {
            uint value = 0;
            int fullBytes = EccCoveredBits / 8;
            int tailBits = EccCoveredBits % 8;
            for (int index = 0; index < fullBytes; index++)
            {
                byte input = (byte)(page[offset + index] ^ 0xFF);
                value = (value >> 8) ^ EccTable[(byte)(value ^ input)];
            }

            uint word = (uint)(page[offset + fullBytes] ^ 0xFF);
            for (int bit = 0; bit < tailBits; bit++)
            {
                value ^= word & 1;
                word >>= 1;
                if ((value & 1) != 0) value ^= EccPolynomial;
                value >>= 1;
            }
            return (~value << 6) & 0xFFFFFFC0U;
        }

        private static void WriteEcc(byte[] page, int offset)
        {
            uint existing = ReadUInt32LittleEndian(page, offset + 0x20C);
            uint value = CalculateEcc(page, offset) | (existing & 0x3FU);
            page[offset + 0x20C] = (byte)value;
            page[offset + 0x20D] = (byte)(value >> 8);
            page[offset + 0x20E] = (byte)(value >> 16);
            page[offset + 0x20F] = (byte)(value >> 24);
        }
    }
}
