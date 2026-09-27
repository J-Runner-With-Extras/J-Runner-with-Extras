using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JRunner.Nand
{
    class BadBlock
    {
        private const int PageDataLength = 0x200;
        private const int PageRawLength = 0x210;
        private const int SystemRawLength = 0x4200000;
        private const int Full256MbRawLength = 0x10800000;
        private const int Full512MbRawLength = 0x21000000;
        private const int Full1024MbRawLength = 0x42000000;

        internal struct NandGeometry
        {
            public readonly int PagesPerBlock;
            public readonly int BlockSize;
            public readonly int SystemBlockCount;
            public readonly int ReserveStart;
            public readonly int ReserveCount;

            public NandGeometry(int pagesPerBlock, int systemBlockCount)
            {
                PagesPerBlock = pagesPerBlock;
                BlockSize = pagesPerBlock * PageRawLength;
                SystemBlockCount = systemBlockCount;
                ReserveCount = pagesPerBlock == 512 ? 0x10 : 0x20;
                ReserveStart = systemBlockCount - ReserveCount;
            }

            public bool IsBigBlock { get { return PagesPerBlock > 32; } }
        }

        private static NandGeometry GeometryFromPages(int pagesPerBlock)
        {
            // Small-block remapping stays in the first 16MB window for both
            // 16MB and 64MB XSB images. Big-block images use the complete
            // 64MB system region, so their block count follows page geometry.
            int systemBlocks = pagesPerBlock == 32
                ? 0x400
                : SystemRawLength / (pagesPerBlock * PageRawLength);
            return new NandGeometry(pagesPerBlock, systemBlocks);
        }

        private static bool Has1024MbGeometryMarker(byte[] data, int length)
        {
            if (data == null) return false;
            int limit = Math.Min(length, data.Length);
            for (int spare = PageDataLength; spare + 0x10 <= limit; spare += PageRawLength)
            {
                if ((data[spare + 0x0C] & 0x3F) == 0x2C &&
                    data[spare + 7] == 0x05 &&
                    data[spare + 8] == 0x30 &&
                    data[spare + 9] == 0x04)
                    return true;
            }
            return false;
        }

        private static bool FileHas1024MbGeometryMarker(string filename)
        {
            byte[] buffer = new byte[0x21000];
            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long remaining = Math.Min(stream.Length, SystemRawLength);
                while (remaining > 0)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = stream.Read(buffer, 0, wanted);
                    if (read <= 0) break;
                    if (Has1024MbGeometryMarker(buffer, read)) return true;
                    remaining -= read;
                }
            }
            return false;
        }

        private static int DetectPagesPerBlock(long sourceLength, byte[] header, Func<bool> has1024MbMarker)
        {
            if (sourceLength == Full1024MbRawLength) return 512;
            if (sourceLength == Full256MbRawLength || sourceLength == Full512MbRawLength) return 256;
            if (sourceLength < SystemRawLength) return 32;
            if (header != null && header.Length > 0x205 && header[0x205] == 0xFF) return 32;
            if (has1024MbMarker != null && has1024MbMarker()) return 512;

            SfcxConfig config;
            if (sourceLength == SystemRawLength &&
                SfcxConfig.TryParse(variables.flashconfig, out config) &&
                config.IsSupported && config.TotalSizeMb == 1024)
                return (int)config.PagesPerBlock;

            return 256;
        }

        internal static NandGeometry GetGeometry(string filename)
        {
            if (String.IsNullOrEmpty(filename) || !File.Exists(filename))
                return GeometryFromPages(32);

            FileInfo file = new FileInfo(filename);
            byte[] header = new byte[Math.Min(0x420, (int)Math.Min(file.Length, 0x420))];
            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int read = 0;
                while (read < header.Length)
                {
                    int current = stream.Read(header, read, header.Length - read);
                    if (current <= 0) break;
                    read += current;
                }
            }

            int pagesPerBlock = DetectPagesPerBlock(file.Length, header, () => FileHas1024MbGeometryMarker(filename));
            return GeometryFromPages(pagesPerBlock);
        }

        internal static NandGeometry GetGeometry(byte[] image)
        {
            if (image == null || image.Length == 0)
                return GeometryFromPages(32);

            int pagesPerBlock = DetectPagesPerBlock(
                image.Length,
                image,
                () => Has1024MbGeometryMarker(image, Math.Min(image.Length, SystemRawLength)));
            return GeometryFromPages(pagesPerBlock);
        }

        private static NandGeometry GeometryFromBlock(byte[] block, bool bigBlockHint)
        {
            if (block != null)
            {
                if (block.Length == 512 * PageRawLength) return GeometryFromPages(512);
                if (block.Length == 256 * PageRawLength) return GeometryFromPages(256);
                if (block.Length == 32 * PageRawLength) return GeometryFromPages(32);
            }
            return GeometryFromPages(bigBlockHint ? 256 : 32);
        }

        private static bool ShouldLog(bool stealth)
        {
            return !stealth || variables.debugMode;
        }

        private static bool HasSpareData(byte[] image)
        {
            return image != null && image.Length > 0x415 &&
                   (image[0x205] == 0xFF || image[0x415] == 0xFF || image[0x200] == 0xFF);
        }

        private static bool HasRemappedBlock(List<int> remappedBlocks)
        {
            return remappedBlocks != null && remappedBlocks.Any(block => block >= 0);
        }

        // Spare layouts:
        //   0: XSB
        //   1: PSB/KSB small-block
        //   2: PSB/KSB big-block (256/512/1024MB physical NAND)
        private static bool CheckIfBadBlock(byte[] block, int blockNumber, NandGeometry geometry, bool stealth)
        {
            if (block == null || block.Length != geometry.BlockSize)
            {
                if (variables.debugMode)
                    Console.WriteLine("Wrong block size: {0}", block == null ? "null" : "0x" + block.Length.ToString("X"));
                return false;
            }

            int markerOffset = geometry.IsBigBlock ? 0x200 : 0x205;
            bool firstPage = true;
            for (int page = 0; page + PageDataLength <= block.Length; page += PageRawLength)
            {
                bool emptySpare = Oper.allsame(Oper.returnportion(block, page + PageDataLength, 0x10), 0x00);
                bool badMarker = block[page + markerOffset] != 0xFF && (!geometry.IsBigBlock || firstPage);
                if (emptySpare || badMarker)
                {
                    if (ShouldLog(stealth) && variables.debugMode)
                        Console.WriteLine("Bad Block ID @ 0x{0:X4} [Offset: 0x{1:X}]", blockNumber, blockNumber * geometry.BlockSize);
                    return true;
                }
                firstPage = false;
            }
            return false;
        }

        public static bool checkifbadblock(byte[] block, int blocknumber, bool bigblock = false, bool stealth = false)
        {
            return CheckIfBadBlock(block, blocknumber, GeometryFromBlock(block, bigblock), stealth);
        }

        internal static bool checkifbadblock(byte[] block, int blocknumber, NandGeometry geometry, bool stealth)
        {
            return CheckIfBadBlock(block, blocknumber, geometry, stealth);
        }

        private static List<int> CheckIfRemapped(byte[] reserved, List<int> badBlocks, NandGeometry geometry, bool stealth)
        {
            List<int> remapped = Enumerable.Repeat(-1, badBlocks.Count).ToList();
            if (badBlocks.Count == 0 || reserved == null) return remapped;

            bool found = false;
            int matched = 0;
            int markerOffset = geometry.IsBigBlock ? 0x200 : 0x205;
            int idOffset = geometry.IsBigBlock ? 0x201 : 0x200;

            try
            {
                for (int slot = geometry.ReserveCount - 1; slot > 0 && matched < badBlocks.Count; slot--)
                {
                    int blockOffset = geometry.BlockSize * slot;
                    bool populated =
                        !Oper.allsame(Oper.returnportion(reserved, blockOffset + geometry.BlockSize - 0x10, 0x10), 0xFF) ||
                        !Oper.allsame(Oper.returnportion(reserved, blockOffset + PageDataLength, 0x10), 0xFF);
                    if (!populated) continue;

                    string endId = Oper.ByteArrayToString(
                        Oper.returnportion(reserved, blockOffset + geometry.BlockSize - (PageRawLength - idOffset), 4)
                        .Reverse().ToArray());
                    string startId = Oper.ByteArrayToString(
                        Oper.returnportion(reserved, blockOffset + idOffset, 4)
                        .Reverse().ToArray());

                    if (endId.Substring(6, 2) == "00") endId = endId.Remove(6).Insert(0, "00");
                    if (startId.Substring(6, 2) == "00") startId = startId.Remove(6).Insert(0, "00");

                    int endMarker = blockOffset + geometry.BlockSize - (PageRawLength - markerOffset);
                    int startMarker = blockOffset + markerOffset;
                    if (endMarker < reserved.Length && startMarker < reserved.Length &&
                        reserved[endMarker] != 0xFF && reserved[startMarker] != 0xFF)
                        continue;

                    for (int index = 0; index < badBlocks.Count; index++)
                    {
                        if (badBlocks[index] < 0 || remapped[index] >= 0) continue;
                        string badBlockId = badBlocks[index].ToString("X8");
                        if (!endId.Contains(badBlockId) && !startId.Contains(badBlockId)) continue;

                        found = true;
                        remapped[index] = slot;
                        matched++;
                        if (ShouldLog(stealth))
                        {
                            int physicalBlock = geometry.ReserveStart + slot;
                            Console.WriteLine("Bad Block ID  {0:X4} Found @ 0x{1:X4} [Offset: 0x{2:X}]",
                                badBlocks[index], physicalBlock, geometry.BlockSize * physicalBlock);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                if (variables.debugMode) Console.WriteLine(ex.ToString());
            }

            if (!found && variables.debugMode) Console.WriteLine("Can't fix it. Remapped Blocks don't exist.");
            return remapped;
        }

        public static List<int> checkifremapped(byte[] reserved, List<int> badblocks, bool bigblock = false, bool stealth = false)
        {
            NandGeometry geometry = GeometryFromPages(bigblock ? 256 : 32);
            return CheckIfRemapped(reserved, badblocks, geometry, stealth);
        }

        internal static List<int> checkifremapped(byte[] reserved, List<int> badblocks, NandGeometry geometry, bool stealth)
        {
            return CheckIfRemapped(reserved, badblocks, geometry, stealth);
        }

        private static byte[] RemapBadBlocks(byte[] image, byte[] reserved, List<int> badBlocks,
            List<int> remappedBlocks, NandGeometry geometry, bool stealth, bool reserveIsSeparate)
        {
            if (image == null || badBlocks == null || remappedBlocks == null) return image;
            int count = Math.Min(badBlocks.Count, remappedBlocks.Count);

            for (int index = 0; index < count; index++)
            {
                int slot = remappedBlocks[index];
                if (slot < 0) continue;

                int sourceOffset = reserveIsSeparate
                    ? slot * geometry.BlockSize
                    : (geometry.ReserveStart + slot) * geometry.BlockSize;
                int destinationOffset = badBlocks[index] * geometry.BlockSize;
                byte[] source = reserveIsSeparate ? reserved : image;

                if (source == null || sourceOffset < 0 || destinationOffset < 0 ||
                    sourceOffset + geometry.BlockSize > source.Length ||
                    destinationOffset + geometry.BlockSize > image.Length)
                    continue;

                Buffer.BlockCopy(source, sourceOffset, image, destinationOffset, geometry.BlockSize);
                if (!reserveIsSeparate)
                {
                    for (int i = 0; i < geometry.BlockSize; i++) image[sourceOffset + i] = 0xFF;
                }

                if (ShouldLog(stealth))
                {
                    int physicalBlock = geometry.ReserveStart + slot;
                    Console.WriteLine("Block ID {0:X4} [Offset: 0x{1:X}] remapped to Block ID {2:X4} [Offset: 0x{3:X}]",
                        physicalBlock, geometry.BlockSize * physicalBlock,
                        badBlocks[index], destinationOffset);
                }
            }

            if (ShouldLog(stealth)) Console.WriteLine("Bad Blocks Remapped");
            return image;
        }

        public static byte[] remapbadblocks(byte[] image, List<int> badblocks, List<int> remappedblocks,
            bool bigblock = false, bool stealth = false)
        {
            NandGeometry geometry = GeometryFromPages(bigblock ? 256 : 32);
            return RemapBadBlocks(image, null, badblocks, remappedblocks, geometry, stealth, false);
        }

        public static byte[] remapbadblocks(byte[] image, byte[] reserved, List<int> badblocks,
            List<int> remappedblocks, bool bigblock = false, bool stealth = false)
        {
            NandGeometry geometry = GeometryFromPages(bigblock ? 256 : 32);
            return RemapBadBlocks(image, reserved, badblocks, remappedblocks, geometry, stealth, true);
        }

        private static List<int> FindBadBlocks(byte[] image, int blocksToCheck, NandGeometry geometry,
            bool stealth, out bool tooMany, out bool badBlockInXell)
        {
            List<int> badBlocks = new List<int>();
            tooMany = false;
            badBlockInXell = false;
            int count = Math.Min(blocksToCheck, image.Length / geometry.BlockSize);

            for (int blockNumber = 0; blockNumber < count; blockNumber++)
            {
                byte[] block = Oper.returnportion(image, blockNumber * geometry.BlockSize, geometry.BlockSize);
                if (CheckIfBadBlock(block, blockNumber, geometry, stealth))
                {
                    badBlocks.Add(blockNumber);
                    if (blockNumber < 0x50) badBlockInXell = true;
                }

                if (badBlocks.Count >= geometry.ReserveCount)
                {
                    Console.WriteLine("Too Many Bad Blocks");
                    tooMany = true;
                    break;
                }
            }
            return badBlocks;
        }

        private static byte[] ReservedFromImage(byte[] image, NandGeometry geometry)
        {
            int offset = geometry.ReserveStart * geometry.BlockSize;
            int length = geometry.ReserveCount * geometry.BlockSize;
            if (offset < 0 || offset + length > image.Length) return new byte[0];
            return Oper.returnportion(image, offset, length);
        }

        public static byte[] find_bad_blocks_b(string filename, bool stealth)
        {
            if (ShouldLog(stealth)) Console.WriteLine("");
            long imageSize = 0;
            FileInfo file = new FileInfo(filename);
            NandGeometry geometry = GetGeometry(filename);
            byte[] image = Oper.openfile(filename, ref imageSize, file.Length >= SystemRawLength ? SystemRawLength : 0);

            if (!Nand.hasecc_v2(ref image))
            {
                if (variables.debugMode) Console.WriteLine("Can't check for bad blocks, no spare data, possibly eMMC");
                return image;
            }

            if (variables.debugMode)
                Console.WriteLine("-B- Image Size: 0x{0:X} | Block Size: 0x{1:X} | File Size: 0x{2:X}",
                    image.Length, geometry.BlockSize, file.Length);

            bool tooMany;
            bool badBlockInXell;
            List<int> badBlocks = FindBadBlocks(image, image.Length / geometry.BlockSize, geometry, false,
                out tooMany, out badBlockInXell);
            if (tooMany) return image;

            if (ShouldLog(stealth)) Console.WriteLine("");
            if (badBlocks.Count == 0)
            {
                if (ShouldLog(stealth)) Console.WriteLine("Bad Blocks Don't Exist");
                return image;
            }

            byte[] reserved = ReservedFromImage(image, geometry);
            List<int> remappedBlocks = CheckIfRemapped(reserved, badBlocks, geometry, false);
            if (!HasRemappedBlock(remappedBlocks))
            {
                if (ShouldLog(stealth)) Console.WriteLine("Can't fix it. Remapped Blocks don't exist.");
                return image;
            }

            image = RemapBadBlocks(image, null, badBlocks, remappedBlocks, geometry, false, false);
            if (ShouldLog(stealth)) Console.WriteLine("");
            return image;
        }

        /// <summary>Find and remap bad blocks in the requested leading block count.</summary>
        public static byte[] find_bad_blocks_X(string filename, int howmany)
        {
            long imageSize = 0;
            FileInfo file = new FileInfo(filename);
            NandGeometry geometry = GetGeometry(filename);
            byte[] image = Oper.openfile(filename, ref imageSize, geometry.BlockSize * howmany);

            if (file.Length == 0x140000 || file.Length == 0x14A000) return image;
            if (!HasSpareData(image))
            {
                if (variables.debugMode) Console.WriteLine("Can't check for bad blocks. No spare data. Possibly eMMC");
                return image;
            }

            if (variables.debugMode)
                Console.WriteLine("-XS- Image Size: 0x{0:X} | Block Size: 0x{1:X} | File Size: 0x{2:X}",
                    image.Length, geometry.BlockSize, file.Length);

            bool tooMany;
            bool badBlockInXell;
            List<int> badBlocks = FindBadBlocks(image, howmany, geometry, true, out tooMany, out badBlockInXell);
            if (tooMany || badBlocks.Count == 0) return image;

            byte[] reserved = Oper.openfilefromoffset(filename, ref imageSize,
                geometry.ReserveCount * geometry.BlockSize,
                geometry.ReserveStart * geometry.BlockSize);
            List<int> remappedBlocks = CheckIfRemapped(reserved, badBlocks, geometry, true);
            if (!HasRemappedBlock(remappedBlocks))
            {
                if (variables.debugMode) Console.WriteLine("Can't fix it. Remapped Blocks don't exist.");
                return image;
            }

            return RemapBadBlocks(image, reserved, badBlocks, remappedBlocks, geometry, true, true);
        }

        public static byte[] find_bad_blocks_X(byte[] image, int howmany)
        {
            if (image == null) return null;
            NandGeometry geometry = GetGeometry(image);
            if (image.Length >= SystemRawLength) image = Oper.returnportion(image, 0, SystemRawLength);

            if (!HasSpareData(image))
            {
                if (variables.debugMode) Console.WriteLine("Can't check for bad blocks. No spare data. Possibly eMMC");
                return image;
            }

            if (variables.debugMode)
                Console.WriteLine("-XB- Image Size: 0x{0:X} | Block Size: 0x{1:X}", image.Length, geometry.BlockSize);

            bool tooMany;
            bool badBlockInXell;
            List<int> badBlocks = FindBadBlocks(image, howmany, geometry, false, out tooMany, out badBlockInXell);
            if (tooMany || badBlocks.Count == 0) return image;

            byte[] reserved = ReservedFromImage(image, geometry);
            List<int> remappedBlocks = CheckIfRemapped(reserved, badBlocks, geometry, false);
            if (!HasRemappedBlock(remappedBlocks))
            {
                if (variables.debugMode) Console.WriteLine("Can't fix it. Remapped Blocks don't exist.");
                return image;
            }

            return RemapBadBlocks(image, null, badBlocks, remappedBlocks, geometry, false, false);
        }

        public static byte[] openRemappedImage(string filename, int length, List<int> badBlocks,
            List<int> remappedBlocks, bool hasEcc)
        {
            long imageSize = 0;
            byte[] image = Oper.openfile(filename, ref imageSize, length);
            if (!hasEcc) return image;

            NandGeometry geometry = GetGeometry(filename);
            byte[] reserved = Oper.openfilefromoffset(filename, ref imageSize,
                geometry.ReserveCount * geometry.BlockSize,
                geometry.ReserveStart * geometry.BlockSize);
            return RemapBadBlocks(image, reserved, badBlocks, remappedBlocks, geometry, true, true);
        }
    }
}
