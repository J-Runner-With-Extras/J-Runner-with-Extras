using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JRunner.Nand
{
    internal enum NandImageKind
    {
        Unknown,
        SmallBlock16,
        SmallBlock64,
        BigOnSmall16,
        BigOnSmall64,
        BigBlockSystem,
        BigBlock256,
        BigBlock512,
        BigBlock1024,
        Emmc4Gb
    }

    internal enum NandConversionTarget
    {
        SmallBlock16,
        SmallBlock64,
        BigOnSmall16,
        BigOnSmall64,
        BigBlock256,
        BigBlock512,
        BigBlock1024,
        Emmc4Gb
    }

    internal sealed class NandImageInfo
    {
        public NandImageKind Kind;
        public long FileLength;
        public int SpareLayout;
        public int PagesPerBlock;
        public string Description;

        public bool IsSmallController
        {
            get
            {
                return Kind == NandImageKind.SmallBlock16 || Kind == NandImageKind.SmallBlock64 ||
                    Kind == NandImageKind.BigOnSmall16 || Kind == NandImageKind.BigOnSmall64;
            }
        }

        public bool IsBigBlock
        {
            get
            {
                return Kind == NandImageKind.BigBlockSystem || Kind == NandImageKind.BigBlock256 ||
                    Kind == NandImageKind.BigBlock512 || Kind == NandImageKind.BigBlock1024;
            }
        }
    }

    internal sealed class NandConversionResult
    {
        public NandImageInfo Source;
        public NandConversionTarget Target;
        public long OutputLength;
        public string Detail;
        public NandImageConverter.Nand1GbConversionReport GeometryReport;
    }

    internal static class NandImageConverter
    {
        private const int DataSize = 0x200;
        private const int RawPageSize = 0x210;
        private const int SpareSize = 0x10;
        private const int SmallDataSize = 0x1000000;
        private const int LargeSmallControllerDataSize = 0x4000000;
        private const int SmallRawSize = 0x1080000;
        private const int SystemRawSize = 0x4200000;
        private const long Full256RawSize = 0x10800000L;
        private const long Full512RawSize = 0x21000000L;
        private const long Full1024RawSize = 0x42000000L;
        private const int EmmcSystemSize = 0x3000000;
        private const int EmmcAnchor1Offset = 0x2FE8000;
        private const int EmmcAnchor2Offset = 0x2FEC000;
        private const int EmmcConfigDataOffset = 0x2FF0000;
        private const int EmmcRootBlock = 0x0BF9;
        private const int BosRootBlock = 0x03AA;
        private const int SmallConfigDataOffset = 0x0F70000;
        private const int LargeSmallConfigDataOffset = 0x03DF0000;
        private const int ConfigDataLength = 0x10000;
        private const int LegacyBigFileSystemStart = 0x00B80000;
        private const int LegacyBigConfigStart = 0x03B80000;
        private const int LegacyBigBlockDataSize = 0x20000;
        private const int LegacyBigPagesPerBlock = 0x100;
        // CD consumes these as logical data offsets from the ECC-stripped NAND
        // aperture.  xeBuild's Trinity physical-big-block output keeps the two
        // update candidates at 0xC0000 and 0xD0000; erase-block geometry does
        // not scale the header's 0x10000 candidate stride.
        private const int LegacyBigSystemUpdate = 0x0C0000;
        private const int LegacyBigPatchSlotSize = 0x010000;
        private const int OneGbSystemUpdate = 0x0C0000;
        private const int OneGbPatchSlotSize = 0x010000;
        private const int SmallBlockDataSize = 0x4000;
        private const int SmallFileSystemWindow = 0x1000000;
        private const int HeaderSizeOffset = 0x0C;
        private const int HeaderSysUpdateOffset = 0x64;
        private const int HeaderSysUpdateCountOffset = 0x68;
        private const int HeaderSysUpdateSizeOffset = 0x70;
        private const int XellImageSize = 0x40000;
        private const string BigBlockType2Label = "Big-block Type 2 (2KB/128KB)";
        private const string BigBlockType3Label = "Big-block Type 3 (4KB/256KB)";
        private static readonly byte[] XellHeader =
            { 0x48, 0x00, 0x00, 0x20, 0x48, 0x00, 0x00, 0xEC,
              0x48, 0x00, 0x00, 0x00, 0x48, 0x00, 0x00, 0x00 };
        private const uint EccPolynomial = 0x06954559;
        private const int EccCoveredBits = 0x1066;
        private static readonly uint[] EccTable = MakeEccTable();

        internal static NandImageInfo Detect(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("The input NAND file does not exist.", path);

            long length = new FileInfo(path).Length;
            if (length == EmmcSystemSize)
            {
                return NewInfo(NandImageKind.Emmc4Gb, length, -1, 0,
                    "4 GB eMMC/MMC (48 MB system partition, no ECC)");
            }

            if (length != SmallRawSize && length != SystemRawSize && length != Full256RawSize &&
                length != Full512RawSize && length != Full1024RawSize)
            {
                throw new InvalidDataException(String.Format(
                    "Unsupported NAND length 0x{0:X}. Expected a 16/64 MB system image, a full 256/512/1024 MB raw image, or a 48 MB eMMC system partition.", length));
            }

            byte[] header = new byte[0x4420];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                ReadExactly(stream, header, 0, header.Length);
            if (!Nand.hasecc(header))
                throw new InvalidDataException("The image has a raw-NAND length but no valid ECC/spare layout was detected.");

            int layout = Nand.identifylayout(Slice(header, 0x4400, SpareSize));
            // A conversion source may intentionally use different geometry than
            // the NAND currently connected to the programmer. Detect the file
            // from its length and filesystem metadata, never from live hardware.
            BadBlock.NandGeometry geometry = BadBlock.GetGeometry(path, false);
            int pagesPerBlock = geometry.PagesPerBlock;

            if (length == SmallRawSize)
            {
                if (layout == 0)
                    return NewInfo(NandImageKind.SmallBlock16, length, layout, 32, "16 MB small-block (XSB)");
                if (layout == 1)
                    return NewInfo(NandImageKind.BigOnSmall16, length, layout, 32, "16 MB big-on-small (PSB/KSB)");
                throw new InvalidDataException("A 16 MB image cannot use big-block spare layout 2.");
            }

            if (pagesPerBlock == 32)
            {
                if (layout == 0)
                    return NewInfo(NandImageKind.SmallBlock64, length, layout, 32, "64 MB small-block (XSB)");
                if (layout == 1)
                    return NewInfo(NandImageKind.BigOnSmall64, length, layout, 32, "64 MB big-on-small (PSB/KSB)");
                throw new InvalidDataException("Small-block geometry contains an invalid spare layout.");
            }

            if (layout != 2)
                throw new InvalidDataException("Big-block geometry does not contain PSB/KSB layout-2 spare data.");
            if (length == Full1024RawSize || pagesPerBlock == 512)
                return NewInfo(NandImageKind.BigBlock1024, length, layout, 512,
                    "1024 MB " + BigBlockType3Label + SystemSuffix(length));
            if (length == Full256RawSize)
                return NewInfo(NandImageKind.BigBlock256, length, layout, 256,
                    "256 MB " + BigBlockType2Label + " (full dump)");
            if (length == Full512RawSize)
                return NewInfo(NandImageKind.BigBlock512, length, layout, 256,
                    "512 MB " + BigBlockType2Label + " (full dump)");
            return NewInfo(NandImageKind.BigBlockSystem, length, layout, 256,
                "256/512 MB " + BigBlockType2Label +
                " (64 MB system area; physical size is not encoded)");
        }

        internal static IList<NandConversionTarget> GetTargets(string consoleFamily)
        {
            List<NandConversionTarget> result = new List<NandConversionTarget>();
            string family = (consoleFamily ?? String.Empty).Trim().ToLowerInvariant();
            if (family == "xenon" || family == "zephyr" || family == "falcon")
            {
                result.Add(NandConversionTarget.SmallBlock16);
                result.Add(NandConversionTarget.SmallBlock64);
            }
            else if (family == "jasper")
            {
                result.Add(NandConversionTarget.BigOnSmall16);
                result.Add(NandConversionTarget.BigOnSmall64);
                result.Add(NandConversionTarget.BigBlock256);
                result.Add(NandConversionTarget.BigBlock512);
                result.Add(NandConversionTarget.BigBlock1024);
            }
            else if (family == "trinity")
            {
                result.Add(NandConversionTarget.BigOnSmall16);
                result.Add(NandConversionTarget.BigOnSmall64);
                result.Add(NandConversionTarget.BigBlock256);
                result.Add(NandConversionTarget.BigBlock512);
                result.Add(NandConversionTarget.BigBlock1024);
            }
            else if (family == "corona" || family == "winchester")
            {
                result.Add(NandConversionTarget.BigOnSmall16);
                result.Add(NandConversionTarget.BigOnSmall64);
                result.Add(NandConversionTarget.BigBlock256);
                result.Add(NandConversionTarget.BigBlock512);
                result.Add(NandConversionTarget.BigBlock1024);
                result.Add(NandConversionTarget.Emmc4Gb);
            }
            return result;
        }

        internal static string TargetDescription(NandConversionTarget target)
        {
            switch (target)
            {
                case NandConversionTarget.SmallBlock16: return "16 MB small-block (XSB)";
                case NandConversionTarget.SmallBlock64: return "64 MB small-block (XSB)";
                case NandConversionTarget.BigOnSmall16: return "16 MB big-on-small (PSB/KSB)";
                case NandConversionTarget.BigOnSmall64: return "64 MB big-on-small (PSB/KSB)";
                case NandConversionTarget.BigBlock256:
                    return "256 MB " + BigBlockType2Label + " (64 MB system output)";
                case NandConversionTarget.BigBlock512:
                    return "512 MB " + BigBlockType2Label + " (64 MB system output)";
                case NandConversionTarget.BigBlock1024:
                    return "1024 MB " + BigBlockType3Label + " (64 MB system output)";
                case NandConversionTarget.Emmc4Gb: return "4 GB eMMC/MMC (48 MB system partition)";
                default: return target.ToString();
            }
        }

        internal static bool TryGetTargetForFlashConfig(string flashConfig,
            out NandConversionTarget target)
        {
            target = NandConversionTarget.BigOnSmall16;
            SfcxConfig config;
            if (!SfcxConfig.TryParse(flashConfig, out config) || config.IsNoDevice)
                return false;

            if (config.IsEmmc)
            {
                target = NandConversionTarget.Emmc4Gb;
                return true;
            }
            if (!config.IsSupported) return false;

            if (config.IsBigBlock)
            {
                if (config.TotalSizeMb == 256) target = NandConversionTarget.BigBlock256;
                else if (config.TotalSizeMb == 512) target = NandConversionTarget.BigBlock512;
                else if (config.TotalSizeMb == 1024) target = NandConversionTarget.BigBlock1024;
                else return false;
                return true;
            }

            if (config.ControllerType == SfcxControllerType.Xsb)
            {
                if (config.TotalSizeMb == 16) target = NandConversionTarget.SmallBlock16;
                else if (config.TotalSizeMb == 64) target = NandConversionTarget.SmallBlock64;
                else return false;
                return true;
            }

            if (config.TotalSizeMb == 16) target = NandConversionTarget.BigOnSmall16;
            else if (config.TotalSizeMb == 64) target = NandConversionTarget.BigOnSmall64;
            else return false;
            return true;
        }

        internal static NandConversionResult Convert(string inputPath, string outputPath,
            NandConversionTarget target, Action<int> progress)
        {
            string input = Path.GetFullPath(inputPath);
            string output = Path.GetFullPath(outputPath);
            if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Input and output paths must be different.");

            NandImageInfo source = Detect(input);
            NandConversionResult result = new NandConversionResult { Source = source, Target = target };

            if (RequiresBigOnSmallBridge(source, target))
            {
                ConvertThroughBigOnSmall(input, output, target, progress);
                result.Detail = String.Format(
                    "Converted {0} to {1} through a temporary 64 MB PSB/KSB big-on-small image. " +
                    "Storage geometry and filesystem layout were rebuilt; console-specific boot payloads were preserved.",
                    source.Description, TargetDescription(target));
                result.OutputLength = new FileInfo(output).Length;
                return result;
            }

            if (IsSmallTarget(target))
            {
                bool targetIsBigOnSmall = target == NandConversionTarget.BigOnSmall16 ||
                    target == NandConversionTarget.BigOnSmall64;
                if (source.IsBigBlock && targetIsBigOnSmall)
                {
                    int targetDataSize = target == NandConversionTarget.BigOnSmall16
                        ? SmallDataSize : LargeSmallControllerDataSize;
                    ConvertBigBlockToBigOnSmall(input, output, source, targetDataSize, progress);
                    result.Detail = "Converted physical big-block geometry to PSB/KSB big-on-small geometry.";
                }
                else if (source.Kind == NandImageKind.Emmc4Gb && targetIsBigOnSmall)
                {
                    int targetDataSize = target == NandConversionTarget.BigOnSmall16
                        ? SmallDataSize : LargeSmallControllerDataSize;
                    ConvertEmmcToBigOnSmall(input, output, targetDataSize, progress);
                    result.Detail = "Extracted and rebuilt the eMMC filesystem as PSB/KSB big-on-small raw NAND.";
                }
                else if (!source.IsSmallController)
                    throw Incompatible(source, target,
                        "Cross-controller conversion requires rebuilding the NAND for the selected console; it cannot be done safely by changing ECC geometry alone.");
                else
                {
                    int targetLayout = target == NandConversionTarget.SmallBlock16 || target == NandConversionTarget.SmallBlock64 ? 0 : 1;
                    int targetDataSize = target == NandConversionTarget.SmallBlock16 || target == NandConversionTarget.BigOnSmall16
                        ? SmallDataSize : LargeSmallControllerDataSize;
                    ConvertSmallController(input, output, source, targetLayout, targetDataSize, progress);
                    result.Detail = "Converted small-block geometry and spare metadata layout.";
                }
            }
            else if (target == NandConversionTarget.Emmc4Gb)
            {
                if (source.Kind == NandImageKind.BigOnSmall16 || source.Kind == NandImageKind.BigOnSmall64)
                {
                    ConvertBigOnSmallToEmmc(input, output, source, progress);
                    result.Detail = "Extracted and rebuilt the PSB/KSB filesystem as a 48 MB eMMC system partition.";
                }
                else if (source.Kind != NandImageKind.Emmc4Gb)
                    throw Incompatible(source, target,
                        "Only PSB/KSB big-on-small NAND can be rebuilt directly as eMMC. Other controller geometries require a console-specific xeBuild rebuild.");
                else
                {
                    CopyPrefixAtomic(input, output, EmmcSystemSize, progress);
                    result.Detail = "Copied the complete 48 MB eMMC system partition.";
                }
            }
            else
            {
                if (source.Kind == NandImageKind.BigOnSmall16 || source.Kind == NandImageKind.BigOnSmall64)
                {
                    ConvertBigOnSmallToBigBlock(input, output, source, target, progress);
                    result.Detail = target == NandConversionTarget.BigBlock1024
                        ? "Converted PSB/KSB big-on-small geometry to 1024 MB " + BigBlockType3Label + "."
                        : "Converted PSB/KSB big-on-small geometry to " + BigBlockType2Label + ".";
                }
                else if (!source.IsBigBlock)
                    throw Incompatible(source, target,
                        "Small-block/eMMC to physical big-block conversion requires a console rebuild, not only a geometry conversion.");
                else if (target == NandConversionTarget.BigBlock1024 && source.PagesPerBlock != 512)
                {
                    result.GeometryReport = OneGbGeometryConverter.ConvertFromLegacyBigBlock(input, output, progress);
                    result.Detail = "Converted 256/512 MB " + BigBlockType2Label +
                        " to 1024 MB " + BigBlockType3Label + ".";
                }
                else if (target != NandConversionTarget.BigBlock1024 && source.PagesPerBlock == 512)
                {
                    result.GeometryReport = OneGbGeometryConverter.ConvertToLegacyBigBlock(input, output, progress);
                    result.Detail = "Converted 1024 MB " + BigBlockType3Label +
                        " to 256/512 MB " + BigBlockType2Label + ".";
                }
                else
                {
                    CopyPrefixAtomic(input, output, SystemRawSize, progress);
                    result.Detail = target == NandConversionTarget.BigBlock1024
                        ? "Copied the 1024 MB " + BigBlockType3Label + " 64 MB system area."
                        : "Copied the " + BigBlockType2Label +
                            " 64 MB system area; 256 and 512 MB use the same system geometry.";
                }
            }

            result.OutputLength = new FileInfo(output).Length;
            return result;
        }

        private static InvalidOperationException Incompatible(NandImageInfo source, NandConversionTarget target, string detail)
        {
            return new InvalidOperationException(String.Format("Cannot convert {0} to {1}. {2}",
                source.Description, TargetDescription(target), detail));
        }

        private static bool IsSmallTarget(NandConversionTarget target)
        {
            return target == NandConversionTarget.SmallBlock16 || target == NandConversionTarget.SmallBlock64 ||
                target == NandConversionTarget.BigOnSmall16 || target == NandConversionTarget.BigOnSmall64;
        }

        private static bool IsXsbTarget(NandConversionTarget target)
        {
            return target == NandConversionTarget.SmallBlock16 || target == NandConversionTarget.SmallBlock64;
        }

        private static bool IsPhysicalBigBlockTarget(NandConversionTarget target)
        {
            return target == NandConversionTarget.BigBlock256 || target == NandConversionTarget.BigBlock512 ||
                target == NandConversionTarget.BigBlock1024;
        }

        private static bool IsXsbSource(NandImageInfo source)
        {
            return source.Kind == NandImageKind.SmallBlock16 || source.Kind == NandImageKind.SmallBlock64;
        }

        private static bool RequiresBigOnSmallBridge(NandImageInfo source, NandConversionTarget target)
        {
            if (target == NandConversionTarget.Emmc4Gb)
                return IsXsbSource(source) || source.IsBigBlock;
            if (IsPhysicalBigBlockTarget(target))
                return IsXsbSource(source) || source.Kind == NandImageKind.Emmc4Gb;
            if (IsXsbTarget(target))
                return source.IsBigBlock || source.Kind == NandImageKind.Emmc4Gb;
            return false;
        }

        private static void ConvertThroughBigOnSmall(string input, string output,
            NandConversionTarget target, Action<int> progress)
        {
            string directory = Path.GetDirectoryName(output);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string bridge = output + "." + Guid.NewGuid().ToString("N") + ".bos64.tmp";
            try
            {
                Convert(input, bridge, NandConversionTarget.BigOnSmall64,
                    progress == null ? null : new Action<int>(delegate(int value)
                    {
                        Report(progress, value / 2);
                    }));
                Convert(bridge, output, target,
                    progress == null ? null : new Action<int>(delegate(int value)
                    {
                        Report(progress, 50 + value / 2);
                    }));
            }
            finally
            {
                if (File.Exists(bridge)) File.Delete(bridge);
            }
        }

        private sealed class FlashFileEntry
        {
            public byte[] DirectoryEntry;
            public string Name;
            public int StartBlock;
            public int Length;
            public byte[] Data;
        }

        private sealed class FlashFileSystem
        {
            public int RootBlock;
            public int Sequence;
            public int[] BlockMap;
            public readonly List<FlashFileEntry> Files = new List<FlashFileEntry>();
        }

        private static void ConvertEmmcToBigOnSmall(string input, string output, int targetDataSize,
            Action<int> progress)
        {
            byte[] sourceData = File.ReadAllBytes(input);
            if (sourceData.Length != EmmcSystemSize)
                throw new InvalidDataException("The eMMC input is not a complete 48 MB system partition.");

            int anchorVersion;
            int rootBlock = ReadEmmcRoot(sourceData, out anchorVersion);
            FlashFileSystem fileSystem = ReadFileSystem(sourceData, rootBlock, anchorVersion);
            ExtractFilePayloads(sourceData, fileSystem);
            Report(progress, 20);

            byte[] targetData = new byte[targetDataSize];
            Fill(targetData, 0, targetData.Length, 0xFF);
            int targetConfig = targetDataSize == SmallDataSize
                ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
            bool[] reserved = NewReservedMap(targetDataSize / SmallBlockDataSize);
            ReserveRange(reserved, BosRootBlock, 1);
            ReserveRange(reserved, targetConfig / SmallBlockDataSize,
                ConfigDataLength / SmallBlockDataSize);
            int firstFileBlock = FirstFileBlock(fileSystem);
            ReserveRange(reserved, 0, firstFileBlock);
            Buffer.BlockCopy(sourceData, 0, targetData, 0,
                Math.Min(firstFileBlock * SmallBlockDataSize, targetData.Length));
            Buffer.BlockCopy(sourceData, EmmcConfigDataOffset, targetData, targetConfig, ConfigDataLength);

            RebuildFileSystem(targetData, fileSystem, firstFileBlock, BosRootBlock, reserved);
            byte[] targetSpare = BuildBosMetadata(targetData, fileSystem, BosRootBlock,
                Math.Max(1, anchorVersion));
            Report(progress, 65);
            WriteRawImage(output, targetData, targetSpare, 32, 65, progress);
        }

        private static void ConvertBigOnSmallToEmmc(string input, string output, NandImageInfo source,
            Action<int> progress)
        {
            int sourceDataSize = source.Kind == NandImageKind.BigOnSmall16
                ? SmallDataSize : LargeSmallControllerDataSize;
            byte[] sourceSpare;
            byte[] sourceData = ReadBosLogicalImage(input, sourceDataSize, out sourceSpare, progress);
            int sequence;
            int rootBlock = FindLatestBosFileSystemRoot(sourceSpare, out sequence);
            FlashFileSystem fileSystem = ReadFileSystem(sourceData, rootBlock, sequence);
            ExtractFilePayloads(sourceData, fileSystem);
            Report(progress, 30);

            byte[] targetData = new byte[EmmcSystemSize];
            Fill(targetData, 0, targetData.Length, 0xFF);
            bool[] reserved = NewReservedMap(EmmcSystemSize / SmallBlockDataSize);
            ReserveRange(reserved, EmmcRootBlock, 1);
            ReserveRange(reserved, EmmcAnchor1Offset / SmallBlockDataSize, 2);
            ReserveRange(reserved, EmmcConfigDataOffset / SmallBlockDataSize,
                ConfigDataLength / SmallBlockDataSize);
            int firstFileBlock = FirstFileBlock(fileSystem);
            ReserveRange(reserved, 0, firstFileBlock);
            Buffer.BlockCopy(sourceData, 0, targetData, 0,
                Math.Min(firstFileBlock * SmallBlockDataSize, targetData.Length));
            int sourceConfig = sourceDataSize == SmallDataSize
                ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
            Buffer.BlockCopy(sourceData, sourceConfig, targetData, EmmcConfigDataOffset, ConfigDataLength);

            RebuildFileSystem(targetData, fileSystem, firstFileBlock, EmmcRootBlock, reserved);
            WriteEmmcAnchor(targetData, EmmcAnchor1Offset, fileSystem, EmmcRootBlock,
                NormalizeAnchorVersion(sequence));
            WriteEmmcAnchor(targetData, EmmcAnchor2Offset, fileSystem, EmmcRootBlock,
                NormalizeAnchorVersion(sequence + 1));
            Report(progress, 90);
            WriteAtomic(output, targetData);
            Report(progress, 100);
        }

        private static byte[] ReadBosLogicalImage(string input, int dataSize, out byte[] spare,
            Action<int> progress)
        {
            byte[] remappedRaw = BadBlock.find_bad_blocks_b(input, true);
            int pages = dataSize / DataSize;
            if (remappedRaw == null || remappedRaw.Length < pages * RawPageSize)
                throw new InvalidDataException("The PSB/KSB source could not be read or remapped.");
            byte[] data = new byte[dataSize];
            spare = new byte[pages * SpareSize];
            for (int page = 0; page < pages; page++)
            {
                int raw = page * RawPageSize;
                Buffer.BlockCopy(remappedRaw, raw, data, page * DataSize, DataSize);
                Buffer.BlockCopy(remappedRaw, raw + DataSize, spare, page * SpareSize, SpareSize);
                if ((page & 0x1FFF) == 0) Report(progress, page * 15 / pages);
            }
            return data;
        }

        private static int ReadEmmcRoot(byte[] image, out int version)
        {
            int root1, version1, root2, version2;
            bool valid1 = ReadEmmcAnchor(image, EmmcAnchor1Offset, out root1, out version1);
            bool valid2 = ReadEmmcAnchor(image, EmmcAnchor2Offset, out root2, out version2);
            if (!valid1 && !valid2)
                throw new InvalidDataException("Neither eMMC filesystem anchor has a valid SHA-1 digest.");
            if (valid2 && (!valid1 || version2 > version1))
            {
                version = version2;
                return root2;
            }
            version = version1;
            return root1;
        }

        private static bool ReadEmmcAnchor(byte[] image, int offset, out int root, out int version)
        {
            root = 0;
            version = 0;
            if (offset < 0 || offset + DataSize > image.Length) return false;
            byte[] digest;
            using (SHA1 sha1 = SHA1.Create())
                digest = sha1.ComputeHash(image, offset + 0x14, 0x1EC);
            for (int i = 0; i < digest.Length; i++)
                if (digest[i] != image[offset + i]) return false;
            version = image[offset + 0x1B];
            root = ReadUInt16BigEndian(image, offset + 0x1C);
            return root > 0 && root < 0x1000;
        }

        private static FlashFileSystem ReadFileSystem(byte[] image, int rootBlock, int sequence)
        {
            int rootOffset = checked(rootBlock * SmallBlockDataSize);
            if (rootOffset < 0 || rootOffset + SmallBlockDataSize > image.Length)
                throw new InvalidDataException(String.Format(
                    "Filesystem root block 0x{0:X} is outside the source image.", rootBlock));
            FlashFileSystem result = new FlashFileSystem
            {
                RootBlock = rootBlock,
                Sequence = sequence,
                BlockMap = new int[0x1000]
            };
            int mapIndex = 0;
            for (int page = 0; page < 32; page += 2)
            {
                int pageOffset = rootOffset + page * DataSize;
                for (int item = 0; item < DataSize / 2; item++)
                    result.BlockMap[mapIndex++] = ReadUInt16BigEndian(image, pageOffset + item * 2);
            }
            for (int page = 1; page < 32; page += 2)
            {
                int pageOffset = rootOffset + page * DataSize;
                for (int item = 0; item < DataSize / 0x20; item++)
                {
                    int entryOffset = pageOffset + item * 0x20;
                    if (image[entryOffset] == 0 ||
                        FSFile.IsDeletedDirectoryEntry(image[entryOffset])) continue;
                    byte[] rawEntry = Slice(image, entryOffset, 0x20);
                    int nameLength = 0;
                    while (nameLength < 0x16 && rawEntry[nameLength] != 0) nameLength++;
                    string name = Encoding.ASCII.GetString(rawEntry, 0, nameLength);
                    int start = ReadUInt16BigEndian(rawEntry, 0x16);
                    uint fileLength = ReadUInt32BigEndian(rawEntry, 0x18);
                    if (fileLength > Int32.MaxValue)
                        throw new InvalidDataException("A filesystem file is too large to convert.");
                    result.Files.Add(new FlashFileEntry
                    {
                        DirectoryEntry = rawEntry,
                        Name = name,
                        StartBlock = start,
                        Length = (int)fileLength
                    });
                }
            }
            if (result.Files.Count == 0)
                throw new InvalidDataException("The selected filesystem root contains no files.");
            return result;
        }

        private static void ExtractFilePayloads(byte[] image, FlashFileSystem fileSystem)
        {
            foreach (FlashFileEntry file in fileSystem.Files)
            {
                file.Data = new byte[file.Length];
                int block = file.StartBlock;
                int copied = 0;
                HashSet<int> visited = new HashSet<int>();
                while (copied < file.Length)
                {
                    if (block < 0 || block >= fileSystem.BlockMap.Length ||
                        block * SmallBlockDataSize >= image.Length || !visited.Add(block))
                        throw new InvalidDataException(String.Format(
                            "The block chain for {0} is invalid at block 0x{1:X}.", file.Name, block));
                    int count = Math.Min(SmallBlockDataSize, file.Length - copied);
                    Buffer.BlockCopy(image, block * SmallBlockDataSize, file.Data, copied, count);
                    copied += count;
                    if (copied >= file.Length) break;
                    int next = fileSystem.BlockMap[block] & 0x1FFF;
                    if (next >= 0x1FF0)
                        throw new InvalidDataException(String.Format(
                            "The block chain for {0} ends before its directory length.", file.Name));
                    block = next;
                }
            }
        }

        private static void RebuildFileSystem(byte[] targetData, FlashFileSystem fileSystem,
            int firstFileBlock, int rootBlock, bool[] reserved)
        {
            int[] map = new int[0x1000];
            for (int i = 0; i < map.Length; i++) map[i] = 0x1FFE;
            for (int i = 0; i < reserved.Length && i < map.Length; i++)
                if (reserved[i]) map[i] = 0x1FFB;

            int cursor = firstFileBlock;
            foreach (FlashFileEntry file in fileSystem.Files)
            {
                int blocks = (file.Length + SmallBlockDataSize - 1) / SmallBlockDataSize;
                if (blocks == 0) blocks = 1;
                List<int> allocated = new List<int>(blocks);
                while (allocated.Count < blocks)
                {
                    while (cursor < reserved.Length && reserved[cursor]) cursor++;
                    if (cursor >= reserved.Length)
                        throw new InvalidDataException(String.Format(
                            "The rebuilt filesystem needs more space than this target provides. " +
                            "{0} cannot be placed; select the 64 MB big-on-small target.", file.Name));
                    allocated.Add(cursor);
                    reserved[cursor] = true;
                    cursor++;
                }
                file.StartBlock = allocated[0];
                WriteUInt16BigEndian(file.DirectoryEntry, 0x16, file.StartBlock);
                int copied = 0;
                for (int index = 0; index < allocated.Count; index++)
                {
                    int block = allocated[index];
                    int offset = block * SmallBlockDataSize;
                    Fill(targetData, offset, SmallBlockDataSize, 0);
                    int count = Math.Min(SmallBlockDataSize, file.Length - copied);
                    if (count > 0) Buffer.BlockCopy(file.Data, copied, targetData, offset, count);
                    copied += count;
                    map[block] = index + 1 < allocated.Count ? allocated[index + 1] : 0x1FFF;
                }
            }
            map[rootBlock] = 0x1FFB;
            byte[] root = new byte[SmallBlockDataSize];
            int mapIndex = 0;
            for (int page = 0; page < 32; page += 2)
            {
                int pageOffset = page * DataSize;
                for (int item = 0; item < DataSize / 2; item++)
                    WriteUInt16BigEndian(root, pageOffset + item * 2, map[mapIndex++]);
            }
            int directoryIndex = 0;
            foreach (FlashFileEntry file in fileSystem.Files)
            {
                if (directoryIndex >= 0x100)
                    throw new InvalidDataException("The filesystem has more than 256 directory entries.");
                int page = 1 + 2 * (directoryIndex / 16);
                int offset = page * DataSize + (directoryIndex % 16) * 0x20;
                Buffer.BlockCopy(file.DirectoryEntry, 0, root, offset, 0x20);
                directoryIndex++;
            }
            Buffer.BlockCopy(root, 0, targetData, rootBlock * SmallBlockDataSize, root.Length);
            fileSystem.BlockMap = map;
            fileSystem.RootBlock = rootBlock;
        }

        private static byte[] BuildBosMetadata(byte[] data, FlashFileSystem fileSystem,
            int rootBlock, int sequence)
        {
            int pages = data.Length / DataSize;
            byte[] spare = new byte[pages * SpareSize];
            Fill(spare, 0, spare.Length, 0xFF);
            SynthesizeBosMetadata(spare, 0, pages);
            foreach (FlashFileEntry file in fileSystem.Files)
            {
                int block = file.StartBlock;
                int remaining = file.Length;
                int type = RawFileType(file.Name);
                HashSet<int> visited = new HashSet<int>();
                while (remaining > 0 && block < fileSystem.BlockMap.Length && visited.Add(block))
                {
                    int usedPages = Math.Min(32, (remaining + DataSize - 1) / DataSize);
                    for (int page = 0; page < usedPages; page++)
                    {
                        int metadata = (block * 32 + page) * SpareSize;
                        spare[metadata + 12] = (byte)type;
                        spare[metadata + 7] = (byte)file.Length;
                        spare[metadata + 8] = (byte)(file.Length >> 8);
                        spare[metadata + 9] = (byte)(32 - usedPages);
                    }
                    remaining -= Math.Min(remaining, SmallBlockDataSize);
                    if (remaining > 0) block = fileSystem.BlockMap[block] & 0x1FFF;
                }
            }
            for (int page = 0; page < 32; page++)
            {
                int metadata = (rootBlock * 32 + page) * SpareSize;
                spare[metadata] = (byte)sequence;
                spare[metadata + 3] = (byte)(sequence >> 8);
                spare[metadata + 4] = (byte)(sequence >> 16);
                spare[metadata + 12] = 0x30;
            }
            return spare;
        }

        private static int RawFileType(string name)
        {
            if (!String.IsNullOrEmpty(name) && name.Length == 11 &&
                name.StartsWith("Mobile", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                char letter = Char.ToUpperInvariant(name[6]);
                if (letter >= 'B' && letter <= 'J') return 0x31 + letter - 'B';
            }
            if (String.Equals(name, "Statistics.settings", StringComparison.OrdinalIgnoreCase)) return 0x3A;
            if (String.Equals(name, "Manufacturing.data", StringComparison.OrdinalIgnoreCase)) return 0x3B;
            return 0x2A;
        }

        private static void WriteEmmcAnchor(byte[] image, int offset, FlashFileSystem fileSystem, int rootBlock, int version)
        {
            Fill(image, offset, SmallBlockDataSize, 0);
            image[offset + 0x1B] = (byte)version;
            WriteUInt16BigEndian(image, offset + 0x1C, rootBlock);
            string[] names = new string[11];
            for (int i = 0; i < 9; i++) names[i] = "Mobile" + (char)('B' + i) + ".dat";
            names[9] = "Statistics.settings";
            names[10] = "Manufacturing.data";
            for (int i = 0; i < names.Length; i++)
            {
                FlashFileEntry match = fileSystem.Files.Find(delegate(FlashFileEntry item)
                {
                    return String.Equals(item.Name, names[i], StringComparison.OrdinalIgnoreCase);
                });
                if (match == null) continue;
                WriteUInt16BigEndian(image, offset + 0x20 + i * 4, match.StartBlock);
                WriteUInt16BigEndian(image, offset + 0x22 + i * 4,
                    match.Length < SmallBlockDataSize ? match.Length : 0);
            }
            byte[] digest;
            using (SHA1 sha1 = SHA1.Create())
                digest = sha1.ComputeHash(image, offset + 0x14, 0x1EC);
            Buffer.BlockCopy(digest, 0, image, offset, digest.Length);
        }

        private static int FindLatestBosFileSystemRoot(byte[] spare, out int sequence)
        {
            int found = -1;
            sequence = -1;
            int blockCount = Math.Min(0x1000, spare.Length / (32 * SpareSize));
            for (int block = 0; block < blockCount; block++)
            {
                int metadata = block * 32 * SpareSize;
                if ((spare[metadata + 12] & 0x3F) != 0x30) continue;
                int candidate = (spare[metadata + 4] << 16) |
                    (spare[metadata + 3] << 8) | spare[metadata];
                if (candidate > sequence)
                {
                    sequence = candidate;
                    found = block;
                }
            }
            if (found < 0) throw new InvalidDataException("No PSB/KSB filesystem root was found.");
            return found;
        }

        private static int FirstFileBlock(FlashFileSystem fileSystem)
        {
            int result = Int32.MaxValue;
            foreach (FlashFileEntry file in fileSystem.Files)
                if (file.StartBlock > 0 && file.StartBlock < result) result = file.StartBlock;
            if (result == Int32.MaxValue || result < 4)
                throw new InvalidDataException("The filesystem contains an invalid first file block.");
            return result;
        }

        private static bool[] NewReservedMap(int blockCount)
        {
            if (blockCount <= 0 || blockCount > 0x1000)
                throw new InvalidDataException("The target filesystem block count is unsupported.");
            return new bool[blockCount];
        }

        private static void ReserveRange(bool[] reserved, int first, int count)
        {
            if (first < 0 || count < 0 || first + count > reserved.Length)
                throw new InvalidDataException("A reserved filesystem range is outside the target image.");
            for (int block = first; block < first + count; block++) reserved[block] = true;
        }

        private static int NormalizeAnchorVersion(int version)
        {
            version &= 0xFF;
            return version == 0 ? 1 : version;
        }

        private static void ConvertBigOnSmallToBigBlock(string input, string output, NandImageInfo source,
            NandConversionTarget target, Action<int> progress)
        {
            if (target != NandConversionTarget.BigBlock1024)
            {
                ConvertBigOnSmallToLegacy(input, output, source, progress);
                return;
            }

            string temporary = output + ".legacy-bb.tmp";
            try
            {
                ConvertBigOnSmallToLegacy(input, temporary, source,
                    delegate(int value) { Report(progress, value / 2); });
                OneGbGeometryConverter.ConvertFromLegacyBigBlock(temporary, output,
                    delegate(int value) { Report(progress, 50 + value / 2); });
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(temporary + ".tmp")) File.Delete(temporary + ".tmp");
            }
        }

        private static void ConvertBigBlockToBigOnSmall(string input, string output, NandImageInfo source,
            int targetDataSize, Action<int> progress)
        {
            string legacyInput = input;
            string temporary = null;
            try
            {
                if (source.PagesPerBlock == 512)
                {
                    temporary = output + ".legacy-bb.tmp";
                    OneGbGeometryConverter.ConvertToLegacyBigBlock(input, temporary,
                        delegate(int value) { Report(progress, value / 2); });
                    legacyInput = temporary;
                }
                ConvertLegacyToBigOnSmall(legacyInput, output, targetDataSize,
                    source.PagesPerBlock == 512
                        ? (Action<int>)delegate(int value) { Report(progress, 50 + value / 2); }
                        : progress);
            }
            finally
            {
                if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
                if (temporary != null && File.Exists(temporary + ".tmp")) File.Delete(temporary + ".tmp");
            }
        }

        private static void ConvertBigOnSmallToLegacy(string input, string output, NandImageInfo source,
            Action<int> progress)
        {
            int sourceDataSize = source.Kind == NandImageKind.BigOnSmall16
                ? SmallDataSize : LargeSmallControllerDataSize;
            byte[] remappedRaw = BadBlock.find_bad_blocks_b(input, true);
            int sourcePages = sourceDataSize / DataSize;
            if (remappedRaw == null || remappedRaw.Length < sourcePages * RawPageSize)
                throw new InvalidDataException("The big-on-small source could not be read or remapped.");

            byte[] sourceData = new byte[sourceDataSize];
            byte[] sourceSpare = new byte[sourcePages * SpareSize];
            for (int page = 0; page < sourcePages; page++)
            {
                int raw = page * RawPageSize;
                Buffer.BlockCopy(remappedRaw, raw, sourceData, page * DataSize, DataSize);
                Buffer.BlockCopy(remappedRaw, raw + DataSize, sourceSpare, page * SpareSize, SpareSize);
                if ((page & 0x1FFF) == 0) Report(progress, page * 10 / sourcePages);
            }
            remappedRaw = null;

            byte[] targetData = new byte[LargeSmallControllerDataSize];
            byte[] targetSpare = new byte[(LargeSmallControllerDataSize / DataSize) * SpareSize];
            Fill(targetData, 0, targetData.Length, 0xFF);
            Fill(targetSpare, 0, targetSpare.Length, 0xFF);

            uint oldHeaderSize = ReadUInt32BigEndian(sourceData, HeaderSizeOffset);
            uint oldSysUpdate = ReadUInt32BigEndian(sourceData, HeaderSysUpdateOffset);
            uint oldSlotSize = ReadUInt32BigEndian(sourceData, HeaderSysUpdateSizeOffset);
            if (oldSlotSize == 0) oldSlotSize = 0x10000;
            int updateCount = (sourceData[HeaderSysUpdateCountOffset] << 8) |
                sourceData[HeaderSysUpdateCountOffset + 1];
            if (oldHeaderSize == 0 || oldHeaderSize > sourceDataSize || oldSysUpdate > sourceDataSize ||
                oldSlotSize > 0x100000 || updateCount > 8)
                throw new InvalidDataException("The source flash header contains invalid system-update geometry.");

            int nominalUpdateEnd = checked((int)(oldSysUpdate + (long)oldSlotSize * updateCount));
            if (nominalUpdateEnd > sourceDataSize)
                throw new InvalidDataException("A system-update slot falls outside the supported system area.");

            // xeBuild constructs Trinity physical-big-block images with the
            // update area at 0xC0000 and a 0x10000 candidate stride.  Slot 0
            // normally contains CF plus the inline beginning of CG.  Slot 1
            // contains the plaintext runtime HV/kernel patch stream beginning
            // at +0x10; it is deliberately not another CF image.
            //
            // A compact big-on-small image places that second slot at 0xC0000
            // and commonly places CF's external CG chunks immediately after it.
            // Moving only CF therefore drops the runtime patches.  Moving the
            // whole two-slot array without rebuilding CF instead overwrites CG.
            // Capture every programmed slot, relocate the external CG chunks,
            // rewrite CF's absolute 0x4000-byte block table, and re-HMAC CF.
            int bootEnd = Math.Min((int)oldSysUpdate, (int)oldHeaderSize);
            CopyBosPagesToLegacy(sourceData, sourceSpare, targetData, targetSpare,
                0, 0, bootEnd, false);
            if (oldSlotSize != LegacyBigPatchSlotSize)
                throw new InvalidDataException(
                    "Changing the system-update slot stride requires rebuilding CF/CG.");

            int slotLength = checked((int)oldSlotSize);
            byte[][] relocatedSlots = new byte[updateCount][];
            for (int slot = 0; slot < updateCount; slot++)
            {
                int sourceOffset = checked((int)(oldSysUpdate + (long)slot * oldSlotSize));
                if (IsAll(sourceData, sourceOffset, slotLength, 0xFF)) continue;
                relocatedSlots[slot] = new byte[slotLength];
                Buffer.BlockCopy(sourceData, sourceOffset, relocatedSlots[slot], 0, slotLength);
            }

            int externalDestination = checked(LegacyBigSystemUpdate +
                updateCount * (int)LegacyBigPatchSlotSize);
            externalDestination = (int)AlignUp(externalDestination, SmallBlockDataSize);
            byte[] cpuKey = null;

            for (int slot = 0; slot < updateCount; slot++)
            {
                byte[] slotBytes = relocatedSlots[slot];
                if (slotBytes == null || slotBytes.Length < 0x30 ||
                    !((slotBytes[0] == (byte)'C' || slotBytes[0] == (byte)'S') &&
                      slotBytes[1] == (byte)'F'))
                    continue;

                uint cfSizeField = ReadUInt32BigEndian(slotBytes, 0x0C);
                if (cfSizeField < 0x230 || cfSizeField > slotBytes.Length)
                    throw new InvalidDataException(String.Format(
                        "CF in update slot {0} has invalid size 0x{1:X}.", slot, cfSizeField));
                int cfSize = checked((int)cfSizeField);
                int cgOffset = checked((int)AlignUp(cfSize, 0x10));
                if (cgOffset + 0x10 > slotBytes.Length ||
                    slotBytes[cgOffset] != (byte)'C' || slotBytes[cgOffset + 1] != (byte)'G')
                    throw new InvalidDataException(String.Format(
                        "CF in update slot {0} is not followed by an inline CG header.", slot));

                uint cgSizeField = ReadUInt32BigEndian(slotBytes, cgOffset + 0x0C);
                if (cgSizeField < 0x10)
                    throw new InvalidDataException(String.Format(
                        "CG in update slot {0} has invalid size 0x{1:X}.", slot, cgSizeField));
                int inlineLength = Math.Min(checked((int)cgSizeField), slotLength - cgOffset);
                int remaining = checked((int)cgSizeField) - inlineLength;
                if (remaining == 0) continue;

                byte[] encryptedCf = new byte[cfSize];
                Buffer.BlockCopy(slotBytes, 0, encryptedCf, 0, cfSize);
                byte[] decryptedCf = Nand.decrypt_CF(encryptedCf);
                int usedBlocks = ReadUInt16BigEndian(decryptedCf, 0x30);
                if (usedBlocks <= 0 || usedBlocks > 223 || remaining > usedBlocks * SmallBlockDataSize)
                    throw new InvalidDataException(String.Format(
                        "CF in update slot {0} has an invalid external CG block table.", slot));

                if (cpuKey == null) cpuKey = GetLoadedCpuKeyForCfRewrite();
                int blocksNeeded = (remaining + SmallBlockDataSize - 1) / SmallBlockDataSize;
                for (int blockIndex = 0; blockIndex < blocksNeeded; blockIndex++)
                {
                    int tableOffset = 0x32 + blockIndex * 2;
                    int sourceBlock = ReadUInt16BigEndian(decryptedCf, tableOffset);
                    int sourceChunk = checked(sourceBlock * SmallBlockDataSize);
                    if (sourceChunk < 0 || sourceChunk + SmallBlockDataSize > sourceData.Length)
                        throw new InvalidDataException(String.Format(
                            "CF external CG block 0x{0:X} falls outside the source image.", sourceBlock));
                    if (externalDestination + SmallBlockDataSize > LegacyBigFileSystemStart)
                        throw new InvalidDataException(
                            "The relocated CG payload collides with the big-block filesystem area.");

                    CopyBosPagesToLegacy(sourceData, sourceSpare, targetData, targetSpare,
                        sourceChunk, externalDestination, SmallBlockDataSize, false);
                    ValidateSystemUpdateRelocation(sourceData, targetData,
                        sourceChunk, externalDestination, SmallBlockDataSize);
                    WriteUInt16BigEndian(decryptedCf, tableOffset,
                        externalDestination / SmallBlockDataSize);
                    externalDestination += SmallBlockDataSize;
                }

                byte[] rewrittenCf = EncryptRewrittenCf(decryptedCf, encryptedCf, cpuKey);
                Buffer.BlockCopy(rewrittenCf, 0, slotBytes, 0, rewrittenCf.Length);

                byte[] verifyCf = Nand.decrypt_CF(rewrittenCf);
                for (int blockIndex = 0; blockIndex < blocksNeeded; blockIndex++)
                {
                    int expectedBlock = (externalDestination / SmallBlockDataSize) -
                        blocksNeeded + blockIndex;
                    if (ReadUInt16BigEndian(verifyCf, 0x32 + blockIndex * 2) != expectedBlock)
                        throw new InvalidDataException("CF block-table rewrite verification failed.");
                }
            }

            for (int slot = 0; slot < updateCount; slot++)
            {
                byte[] slotBytes = relocatedSlots[slot];
                if (slotBytes == null) continue;
                int targetOffset = checked(LegacyBigSystemUpdate +
                    slot * (int)LegacyBigPatchSlotSize);
                Buffer.BlockCopy(slotBytes, 0, targetData, targetOffset, slotBytes.Length);
                SynthesizeLegacyMetadata(targetSpare, targetOffset / DataSize,
                    slotBytes.Length / DataSize);
            }
            WriteUInt32BigEndian(targetData, HeaderSizeOffset, LegacyBigSystemUpdate);
            WriteUInt32BigEndian(targetData, HeaderSysUpdateOffset, LegacyBigSystemUpdate);
            WriteUInt32BigEndian(targetData, HeaderSysUpdateSizeOffset, LegacyBigPatchSlotSize);

            // Big-block FFS entries are relative to the start of the bigffs
            // window. Preserve those indices by placing the complete small-NAND
            // logical window at the legacy bigffs base and translating only the
            // filesystem spare records.
            CopyBosPagesToLegacy(sourceData, sourceSpare, targetData, targetSpare,
                0, LegacyBigFileSystemStart, SmallFileSystemWindow, true);

            List<int> rootBlocks = FindBosFileSystemRoots(sourceSpare);
            int rootStart = LegacyBigConfigStart - rootBlocks.Count * 0x40000;
            for (int index = 0; index < rootBlocks.Count; index++)
            {
                CopyBosPagesToLegacy(sourceData, sourceSpare, targetData, targetSpare,
                    rootBlocks[index] * SmallBlockDataSize, rootStart + index * 0x40000,
                    SmallBlockDataSize, true, true);
            }

            int sourceConfig = sourceDataSize == SmallDataSize
                ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
            for (int block = 0; block < 4; block++)
            {
                int sourceOffset = sourceConfig + block * SmallBlockDataSize;
                int targetOffset = LegacyBigConfigStart + block * LegacyBigBlockDataSize;
                Buffer.BlockCopy(sourceData, sourceOffset, targetData, targetOffset, SmallBlockDataSize);
                SynthesizeLegacyMetadata(targetSpare, targetOffset / DataSize,
                    SmallBlockDataSize / DataSize);
            }

            ValidateBootXellPreserved(sourceData, targetData, oldSysUpdate, oldSlotSize, updateCount);
            WriteRawImage(output, targetData, targetSpare, LegacyBigPagesPerBlock, 10, progress);
        }

        private static void ConvertLegacyToBigOnSmall(string input, string output, int targetDataSize,
            Action<int> progress)
        {
            byte[] remappedRaw = BadBlock.find_bad_blocks_b(input, true);
            int sourcePages = LargeSmallControllerDataSize / DataSize;
            if (remappedRaw == null || remappedRaw.Length < sourcePages * RawPageSize)
                throw new InvalidDataException("The legacy big-block source could not be read or remapped.");

            byte[] sourceData = new byte[LargeSmallControllerDataSize];
            byte[] sourceSpare = new byte[sourcePages * SpareSize];
            for (int page = 0; page < sourcePages; page++)
            {
                int raw = page * RawPageSize;
                Buffer.BlockCopy(remappedRaw, raw, sourceData, page * DataSize, DataSize);
                Buffer.BlockCopy(remappedRaw, raw + DataSize, sourceSpare, page * SpareSize, SpareSize);
                if ((page & 0x1FFF) == 0) Report(progress, page * 10 / sourcePages);
            }
            remappedRaw = null;

            byte[] targetData = new byte[targetDataSize];
            byte[] targetSpare = new byte[(targetDataSize / DataSize) * SpareSize];
            Fill(targetData, 0, targetData.Length, 0x00);
            Fill(targetSpare, 0, targetSpare.Length, 0xFF);

            CopyLegacyPagesToBos(sourceData, sourceSpare, targetData, targetSpare,
                LegacyBigFileSystemStart, 0, SmallFileSystemWindow, true);

            List<int> rootBlocks = FindLegacyFileSystemRoots(sourceSpare);
            int smallRootStart = 0x3AC - rootBlocks.Count * 2;
            if (smallRootStart < 0)
                throw new InvalidDataException("The big-block image has too many filesystem versions for a 16 MB big-on-small image.");
            for (int index = 0; index < rootBlocks.Count; index++)
            {
                CopyLegacyPagesToBos(sourceData, sourceSpare, targetData, targetSpare,
                    rootBlocks[index] * LegacyBigBlockDataSize, (smallRootStart + index * 2) * SmallBlockDataSize,
                    SmallBlockDataSize, true, true);
            }

            uint oldHeaderSize = ReadUInt32BigEndian(sourceData, HeaderSizeOffset);
            uint oldSysUpdate = ReadUInt32BigEndian(sourceData, HeaderSysUpdateOffset);
            uint oldSlotSize = ReadUInt32BigEndian(sourceData, HeaderSysUpdateSizeOffset);
            if (oldSlotSize == 0) oldSlotSize = LegacyBigBlockDataSize;
            int updateCount = (sourceData[HeaderSysUpdateCountOffset] << 8) |
                sourceData[HeaderSysUpdateCountOffset + 1];
            if (oldHeaderSize == 0 || oldSysUpdate > sourceData.Length || oldSlotSize > 0x100000 || updateCount > 8)
                throw new InvalidDataException("The source flash header contains invalid system-update geometry.");

            int updateEnd = FindSystemUpdatePayloadEnd(sourceData, oldSysUpdate,
                oldSlotSize, updateCount);
            int updateLength = updateEnd - checked((int)oldSysUpdate);
            const int targetSysUpdate = 0x0B0000;
            const int targetSlotSize = 0x010000;
            if (updateEnd > sourceData.Length || targetSysUpdate + updateLength > targetDataSize)
                throw new InvalidDataException("A system-update payload falls outside the selected big-on-small output.");
            int bootEnd = Math.Min((int)oldSysUpdate, (int)oldHeaderSize);
            CopyLegacyPagesToBos(sourceData, sourceSpare, targetData, targetSpare,
                0, 0, bootEnd, false);
            CopyLegacyPagesToBos(sourceData, sourceSpare, targetData, targetSpare,
                checked((int)oldSysUpdate), targetSysUpdate, updateLength, false);
            WriteUInt32BigEndian(targetData, HeaderSizeOffset, targetSysUpdate);
            WriteUInt32BigEndian(targetData, HeaderSysUpdateOffset, targetSysUpdate);
            WriteUInt32BigEndian(targetData, HeaderSysUpdateSizeOffset, targetSlotSize);

            int targetConfig = targetDataSize == SmallDataSize
                ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
            if (targetDataSize != SmallDataSize)
            {
                Fill(targetData, SmallConfigDataOffset, ConfigDataLength, 0x00);
                SynthesizeBosMetadata(targetSpare, SmallConfigDataOffset / DataSize,
                    ConfigDataLength / DataSize);
            }
            for (int block = 0; block < 4; block++)
            {
                int sourceOffset = LegacyBigConfigStart + block * LegacyBigBlockDataSize;
                int targetOffset = targetConfig + block * SmallBlockDataSize;
                Buffer.BlockCopy(sourceData, sourceOffset, targetData, targetOffset, SmallBlockDataSize);
                SynthesizeBosMetadata(targetSpare, targetOffset / DataSize,
                    SmallBlockDataSize / DataSize);
            }

            ValidateSystemUpdateRelocation(sourceData, targetData, checked((int)oldSysUpdate),
                targetSysUpdate, updateLength);
            ValidateBootXellPreserved(sourceData, targetData, oldSysUpdate, oldSlotSize, updateCount);
            WriteRawImage(output, targetData, targetSpare, 32, 10, progress);
        }

        private static void ValidateSystemUpdateRelocation(byte[] sourceData, byte[] targetData,
            int sourceOffset, int targetOffset, int length)
        {
            if (sourceOffset < 0 || targetOffset < 0 || length < 0 ||
                sourceOffset + length > sourceData.Length || targetOffset + length > targetData.Length)
                throw new InvalidDataException("The relocated system-update payload falls outside the NAND image.");
            for (int index = 0; index < length; index++)
            {
                if (sourceData[sourceOffset + index] != targetData[targetOffset + index])
                    throw new InvalidDataException(String.Format(
                        "System-update relocation differs at payload offset 0x{0:X}.", index));
            }
        }

        private static void ValidateBootXellPreserved(byte[] sourceData, byte[] targetData,
            uint sysUpdate, uint slotSize, int updateCount)
        {
            List<int> candidates = new List<int>
            {
                0x70000, 0x95060, 0x100000, 0xC0000, 0xE0000
            };
            long afterPatchSlots = sysUpdate + (long)slotSize * updateCount;
            if (afterPatchSlots >= 0 && afterPatchSlots <= Int32.MaxValue)
                candidates.Add((int)afterPatchSlots);

            foreach (int offset in candidates.Distinct())
            {
                if (!HasBytes(sourceData, offset, XellHeader)) continue;
                if (offset < 0 || offset + XellImageSize > sourceData.Length ||
                    offset + XellImageSize > targetData.Length)
                    throw new InvalidDataException("The detected XeLL image falls outside the converted system area.");
                for (int index = 0; index < XellImageSize; index++)
                {
                    if (sourceData[offset + index] != targetData[offset + index])
                        throw new InvalidDataException(String.Format(
                            "XeLL at logical offset 0x{0:X} changed during NAND geometry conversion.", offset));
                }
            }
        }

        private static int FindSystemUpdatePayloadEnd(byte[] data, uint sysUpdate,
            uint slotSizeField, int updateCount)
        {
            uint slotSize = slotSizeField == 0 ? 0x10000U : slotSizeField;
            long nominalEnd = sysUpdate + (long)slotSize * updateCount;
            if (sysUpdate > data.Length || nominalEnd > data.Length)
                throw new InvalidDataException("A system-update slot falls outside the NAND image.");

            long payloadEnd = nominalEnd;
            for (int slot = 0; slot < updateCount; slot++)
            {
                long cfOffset64 = sysUpdate + (long)slot * slotSize;
                if (cfOffset64 + 0x10 > data.Length) break;
                int cfOffset = (int)cfOffset64;
                if (data[cfOffset] != (byte)'C' || data[cfOffset + 1] != (byte)'F')
                    continue;

                uint cfSize = ReadUInt32BigEndian(data, cfOffset + 0x0C);
                if (cfSize < 0x20 || cfOffset64 + cfSize > data.Length)
                    throw new InvalidDataException(String.Format(
                        "CF in update slot {0} has invalid size 0x{1:X}.", slot, cfSize));

                long cgOffset64 = AlignUp(cfOffset64 + cfSize, 0x10);
                if (cgOffset64 + 0x10 > data.Length)
                    throw new InvalidDataException(String.Format(
                        "CG header for update slot {0} falls outside the NAND image.", slot));
                int cgOffset = (int)cgOffset64;
                if (data[cgOffset] != (byte)'C' || data[cgOffset + 1] != (byte)'G')
                    throw new InvalidDataException(String.Format(
                        "CF in update slot {0} is not followed by a CG payload.", slot));

                uint cgSize = ReadUInt32BigEndian(data, cgOffset + 0x0C);
                if (cgSize < 0x10 || cgOffset64 + cgSize > data.Length)
                    throw new InvalidDataException(String.Format(
                        "CG in update slot {0} has invalid size 0x{1:X}.", slot, cgSize));

                // The CG header begins after CF inside this candidate, but CD
                // stops reading inline data at the end of the slot.  Remaining
                // CG bytes are loaded through CF's 0x4000-byte block table. In
                // the compact BOS form those chunks begin after the complete
                // candidate array, not directly after the inline fragment.
                long inlineCapacity = Math.Max(0, cfOffset64 + slotSize - cgOffset64);
                long spillLength = Math.Max(0, (long)cgSize - inlineCapacity);
                payloadEnd = Math.Max(payloadEnd,
                    AlignUp(nominalEnd + spillLength, 0x10));
            }

            return checked((int)AlignUp(payloadEnd, DataSize));
        }

        private static long AlignUp(long value, int alignment)
        {
            return (value + alignment - 1) & ~((long)alignment - 1);
        }

        private static Dictionary<int, byte[]> CaptureBootXells(byte[] data)
        {
            uint sysUpdate = ReadUInt32BigEndian(data, HeaderSysUpdateOffset);
            uint slotSize = ReadUInt32BigEndian(data, HeaderSysUpdateSizeOffset);
            if (slotSize == 0) slotSize = 0x10000;
            int updateCount = (data[HeaderSysUpdateCountOffset] << 8) |
                data[HeaderSysUpdateCountOffset + 1];
            List<int> candidates = new List<int>
            {
                0x70000, 0x95060, 0x100000, 0xC0000, 0xE0000
            };
            long afterPatchSlots = sysUpdate + (long)slotSize * updateCount;
            if (afterPatchSlots >= 0 && afterPatchSlots <= Int32.MaxValue)
                candidates.Add((int)afterPatchSlots);

            Dictionary<int, byte[]> result = new Dictionary<int, byte[]>();
            foreach (int offset in candidates.Distinct())
            {
                if (!HasBytes(data, offset, XellHeader)) continue;
                if (offset + XellImageSize > data.Length)
                    throw new InvalidDataException("The detected XeLL image falls outside the NAND system area.");
                byte[] image = new byte[XellImageSize];
                Buffer.BlockCopy(data, offset, image, 0, image.Length);
                result.Add(offset, image);
            }
            return result;
        }

        private static void ValidateCapturedBootXells(byte[] data, Dictionary<int, byte[]> captured)
        {
            foreach (KeyValuePair<int, byte[]> xell in captured)
            {
                if (xell.Key + xell.Value.Length > data.Length)
                    throw new InvalidDataException("The detected XeLL image falls outside the converted system area.");
                for (int index = 0; index < xell.Value.Length; index++)
                {
                    if (data[xell.Key + index] != xell.Value[index])
                        throw new InvalidDataException(String.Format(
                            "XeLL at logical offset 0x{0:X} changed during NAND geometry conversion.", xell.Key));
                }
            }
        }

        private static bool HasBytes(byte[] data, int offset, byte[] expected)
        {
            if (offset < 0 || offset + expected.Length > data.Length) return false;
            for (int index = 0; index < expected.Length; index++)
                if (data[offset + index] != expected[index]) return false;
            return true;
        }

        private static void CopyBosPagesToLegacy(byte[] sourceData, byte[] sourceSpare,
            byte[] targetData, byte[] targetSpare, int sourceOffset, int targetOffset, int count,
            bool fileSystemWindow, bool preserveRoot = false)
        {
            if ((sourceOffset | targetOffset | count) % DataSize != 0)
                throw new InvalidDataException("A NAND mapping operation was not page aligned.");
            int pages = count / DataSize;
            for (int page = 0; page < pages; page++)
            {
                int sourcePage = sourceOffset / DataSize + page;
                int targetPage = targetOffset / DataSize + page;
                Buffer.BlockCopy(sourceData, sourcePage * DataSize, targetData, targetPage * DataSize, DataSize);
                int sourceMeta = sourcePage * SpareSize;
                int targetMeta = targetPage * SpareSize;
                if (IsAll(sourceData, sourcePage * DataSize, DataSize, 0xFF) &&
                    IsAll(sourceSpare, sourceMeta, SpareSize, 0xFF))
                    continue;

                for (int i = 0; i < 13; i++) targetSpare[targetMeta + i] = 0;
                int targetBlock = targetPage / LegacyBigPagesPerBlock;
                targetSpare[targetMeta] = 0xFF;
                targetSpare[targetMeta + 1] = (byte)targetBlock;
                targetSpare[targetMeta + 2] = (byte)(targetBlock >> 8);
                Buffer.BlockCopy(sourceSpare, sourceMeta + 3, targetSpare, targetMeta + 3, 10);
                // Layout 1 stores the bad-block marker at byte 5. Layout 2
                // moves that marker to byte 0, so byte 5 must not retain FF;
                // it is the low filesystem-sequence byte on managed pages.
                targetSpare[targetMeta + 5] = 0;

                int sourceType = sourceSpare[sourceMeta + 12] & 0x3F;
                if (fileSystemWindow && sourceType >= 0x30 && sourceType < 0x3F)
                {
                    int sequence = (sourceSpare[sourceMeta + 4] << 16) |
                        (sourceSpare[sourceMeta + 3] << 8) | sourceSpare[sourceMeta];
                    if (sequence > 0xFFFF)
                        throw new InvalidDataException("The small-block filesystem sequence does not fit big-block metadata.");
                    targetSpare[targetMeta + 3] = 0;
                    targetSpare[targetMeta + 4] = (byte)(sequence >> 8);
                    targetSpare[targetMeta + 5] = (byte)sequence;
                    targetSpare[targetMeta + 6] = 0;
                    targetSpare[targetMeta + 7] = 0x0A;
                    targetSpare[targetMeta + 8] = 0x60;
                    targetSpare[targetMeta + 9] = 0x04;
                    targetSpare[targetMeta + 12] = (byte)((sourceSpare[sourceMeta + 12] & 0xC0) |
                        (sourceType - 4));
                    if (sourceType == 0x30 && !preserveRoot)
                        targetSpare[targetMeta + 12] = (byte)(sourceSpare[sourceMeta + 12] & 0xC0);
                }
            }
        }

        private static void CopyLegacyPagesToBos(byte[] sourceData, byte[] sourceSpare,
            byte[] targetData, byte[] targetSpare, int sourceOffset, int targetOffset, int count,
            bool fileSystemWindow, bool preserveRoot = false)
        {
            if ((sourceOffset | targetOffset | count) % DataSize != 0)
                throw new InvalidDataException("A NAND mapping operation was not page aligned.");
            int pages = count / DataSize;
            for (int page = 0; page < pages; page++)
            {
                int sourcePage = sourceOffset / DataSize + page;
                int targetPage = targetOffset / DataSize + page;
                Buffer.BlockCopy(sourceData, sourcePage * DataSize, targetData, targetPage * DataSize, DataSize);
                int sourceMeta = sourcePage * SpareSize;
                int targetMeta = targetPage * SpareSize;
                if (IsAll(sourceData, sourcePage * DataSize, DataSize, 0xFF) &&
                    IsAll(sourceSpare, sourceMeta, SpareSize, 0xFF))
                    continue;

                for (int i = 0; i < 13; i++) targetSpare[targetMeta + i] = 0;
                int targetBlock = targetPage / 32;
                targetSpare[targetMeta] = sourceSpare[sourceMeta + 3];
                targetSpare[targetMeta + 1] = (byte)targetBlock;
                targetSpare[targetMeta + 2] = (byte)(targetBlock >> 8);
                targetSpare[targetMeta + 3] = sourceSpare[sourceMeta + 4];
                targetSpare[targetMeta + 4] = 0;
                targetSpare[targetMeta + 5] = 0xFF;
                targetSpare[targetMeta + 6] = sourceSpare[sourceMeta + 6];
                Buffer.BlockCopy(sourceSpare, sourceMeta + 7, targetSpare, targetMeta + 7, 6);

                int sourceType = sourceSpare[sourceMeta + 12] & 0x3F;
                if (fileSystemWindow && sourceType >= 0x2C && sourceType <= 0x3A)
                {
                    int sequence = (sourceSpare[sourceMeta + 4] << 8) | sourceSpare[sourceMeta + 5];
                    targetSpare[targetMeta] = (byte)sequence;
                    targetSpare[targetMeta + 3] = (byte)(sequence >> 8);
                    targetSpare[targetMeta + 4] = 0;
                    targetSpare[targetMeta + 6] = 0;
                    targetSpare[targetMeta + 7] = 0;
                    targetSpare[targetMeta + 8] = 0;
                    targetSpare[targetMeta + 9] = 0;
                    targetSpare[targetMeta + 12] = (byte)((sourceSpare[sourceMeta + 12] & 0xC0) |
                        (sourceType + 4));
                    if (sourceType == 0x2C && !preserveRoot)
                        targetSpare[targetMeta + 12] = (byte)(sourceSpare[sourceMeta + 12] & 0xC0);
                }
            }
        }

        private static List<int> FindBosFileSystemRoots(byte[] spare)
        {
            List<KeyValuePair<int, int>> roots = new List<KeyValuePair<int, int>>();
            int blockCount = Math.Min(0x400, spare.Length / (32 * SpareSize));
            for (int block = 0; block < blockCount; block++)
            {
                int metadata = block * 32 * SpareSize;
                if ((spare[metadata + 12] & 0x3F) != 0x30) continue;
                int sequence = (spare[metadata + 4] << 16) |
                    (spare[metadata + 3] << 8) | spare[metadata];
                roots.Add(new KeyValuePair<int, int>(sequence, block));
            }
            roots.Sort(delegate(KeyValuePair<int, int> left, KeyValuePair<int, int> right)
            {
                return left.Key.CompareTo(right.Key);
            });
            List<int> result = new List<int>();
            foreach (KeyValuePair<int, int> root in roots) result.Add(root.Value);
            if (result.Count == 0)
                throw new InvalidDataException("No small-block filesystem version blocks were found.");
            return result;
        }

        private static List<int> FindLegacyFileSystemRoots(byte[] spare)
        {
            List<KeyValuePair<int, int>> roots = new List<KeyValuePair<int, int>>();
            int blockCount = spare.Length / (LegacyBigPagesPerBlock * SpareSize);
            for (int block = 0; block < blockCount; block++)
            {
                int metadata = block * LegacyBigPagesPerBlock * SpareSize;
                if ((spare[metadata + 12] & 0x3F) != 0x2C) continue;
                int sequence = (spare[metadata + 4] << 8) | spare[metadata + 5];
                roots.Add(new KeyValuePair<int, int>(sequence, block));
            }
            roots.Sort(delegate(KeyValuePair<int, int> left, KeyValuePair<int, int> right)
            {
                return left.Key.CompareTo(right.Key);
            });
            List<int> result = new List<int>();
            foreach (KeyValuePair<int, int> root in roots) result.Add(root.Value);
            if (result.Count == 0)
                throw new InvalidDataException("No legacy big-block filesystem version blocks were found.");
            return result;
        }

        private static void SynthesizeLegacyMetadata(byte[] metadata, int firstPage, int pageCount)
        {
            for (int page = firstPage; page < firstPage + pageCount; page++)
            {
                int offset = page * SpareSize;
                for (int i = 0; i < 13; i++) metadata[offset + i] = 0;
                int block = page / LegacyBigPagesPerBlock;
                metadata[offset] = 0xFF;
                metadata[offset + 1] = (byte)block;
                metadata[offset + 2] = (byte)(block >> 8);
            }
        }

        private static void SynthesizeBosMetadata(byte[] metadata, int firstPage, int pageCount)
        {
            for (int page = firstPage; page < firstPage + pageCount; page++)
            {
                int offset = page * SpareSize;
                for (int i = 0; i < 13; i++) metadata[offset + i] = 0;
                int block = page / 32;
                metadata[offset + 1] = (byte)block;
                metadata[offset + 2] = (byte)(block >> 8);
                metadata[offset + 5] = 0xFF;
            }
        }

        private static void WriteRawImage(string output, byte[] data, byte[] metadata, int pagesPerBlock,
            int progressStart, Action<int> progress)
        {
            int pages = data.Length / DataSize;
            byte[] raw = new byte[pages * RawPageSize];
            Fill(raw, 0, raw.Length, 0xFF);
            for (int page = 0; page < pages; page++)
            {
                int dataOffset = page * DataSize;
                int metadataOffset = page * SpareSize;
                if (IsAll(data, dataOffset, DataSize, 0xFF) &&
                    IsAll(metadata, metadataOffset, SpareSize, 0xFF))
                    continue;

                int rawOffset = page * RawPageSize;
                Buffer.BlockCopy(data, dataOffset, raw, rawOffset, DataSize);
                if (IsAll(metadata, metadataOffset, SpareSize, 0xFF))
                {
                    if (pagesPerBlock == 32)
                    {
                        int block = page / pagesPerBlock;
                        raw[rawOffset + DataSize] = 0;
                        raw[rawOffset + DataSize + 1] = (byte)block;
                        raw[rawOffset + DataSize + 2] = (byte)(block >> 8);
                        raw[rawOffset + DataSize + 5] = 0xFF;
                    }
                    else
                    {
                        int block = page / pagesPerBlock;
                        raw[rawOffset + DataSize] = 0xFF;
                        raw[rawOffset + DataSize + 1] = (byte)block;
                        raw[rawOffset + DataSize + 2] = (byte)(block >> 8);
                    }
                }
                else
                {
                    Buffer.BlockCopy(metadata, metadataOffset, raw, rawOffset + DataSize, 13);
                }
                raw[rawOffset + DataSize + 13] = 0;
                raw[rawOffset + DataSize + 14] = 0;
                raw[rawOffset + DataSize + 15] = 0;
                WriteEcc(raw, rawOffset);
                if ((page & 0x1FFF) == 0)
                    Report(progress, progressStart + page * (100 - progressStart) / pages);
            }
            WriteAtomic(output, raw);
            Report(progress, 100);
        }

        private static void ConvertSmallController(string input, string output, NandImageInfo source,
            int targetLayout, int targetDataSize, Action<int> progress)
        {
            int sourceDataSize = source.Kind == NandImageKind.SmallBlock16 || source.Kind == NandImageKind.BigOnSmall16
                ? SmallDataSize : LargeSmallControllerDataSize;
            int sourcePages = sourceDataSize / DataSize;
            int targetPages = targetDataSize / DataSize;
            int workingDataSize = Math.Max(sourceDataSize, targetDataSize);
            int workingPages = workingDataSize / DataSize;
            byte[] data = new byte[workingDataSize];
            byte[] metadata = new byte[workingPages * SpareSize];
            Fill(data, 0, data.Length, sourceDataSize < targetDataSize ? (byte)0x00 : (byte)0xFF);
            Fill(metadata, 0, metadata.Length, 0xFF);

            using (FileStream stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] page = new byte[RawPageSize];
                int pagesToRead = sourcePages;
                for (int pageIndex = 0; pageIndex < pagesToRead; pageIndex++)
                {
                    ReadExactly(stream, page, 0, page.Length);
                    Buffer.BlockCopy(page, 0, data, pageIndex * DataSize, DataSize);
                    Buffer.BlockCopy(page, DataSize, metadata, pageIndex * SpareSize, SpareSize);
                    if ((pageIndex & 0x1FFF) == 0) Report(progress, pageIndex * 25 / pagesToRead);
                }
            }

            if (sourceDataSize != targetDataSize)
            {
                int from = sourceDataSize == SmallDataSize ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
                int to = targetDataSize == SmallDataSize ? SmallConfigDataOffset : LargeSmallConfigDataOffset;
                byte[] config = new byte[ConfigDataLength];
                byte[] configMeta = new byte[(ConfigDataLength / DataSize) * SpareSize];
                Buffer.BlockCopy(data, from, config, 0, config.Length);
                Buffer.BlockCopy(metadata, (from / DataSize) * SpareSize, configMeta, 0, configMeta.Length);
                Fill(data, from, config.Length, 0x00);
                Fill(metadata, (from / DataSize) * SpareSize, configMeta.Length, 0x00);
                Buffer.BlockCopy(config, 0, data, to, config.Length);
                Buffer.BlockCopy(configMeta, 0, metadata, (to / DataSize) * SpareSize, configMeta.Length);
            }

            byte[] outputRaw = new byte[targetPages * RawPageSize];
            byte[] sourceSpare = new byte[SpareSize];
            byte[] targetSpare = new byte[SpareSize];
            int relocatedFrom = sourceDataSize == targetDataSize ? -1 :
                (sourceDataSize == SmallDataSize ? SmallConfigDataOffset : LargeSmallConfigDataOffset);
            int relocatedTo = sourceDataSize == targetDataSize ? -1 :
                (targetDataSize == SmallDataSize ? SmallConfigDataOffset : LargeSmallConfigDataOffset);
            for (int pageIndex = 0; pageIndex < targetPages; pageIndex++)
            {
                int raw = pageIndex * RawPageSize;
                int logicalOffset = pageIndex * DataSize;
                Buffer.BlockCopy(data, pageIndex * DataSize, outputRaw, raw, DataSize);
                Buffer.BlockCopy(metadata, pageIndex * SpareSize, sourceSpare, 0, SpareSize);
                bool relocatedConfig = relocatedFrom >= 0 &&
                    ((logicalOffset >= relocatedFrom && logicalOffset < relocatedFrom + ConfigDataLength) ||
                     (logicalOffset >= relocatedTo && logicalOffset < relocatedTo + ConfigDataLength));
                TranslateSmallSpare(sourceSpare, source.SpareLayout, targetSpare, targetLayout, pageIndex / 32,
                    pageIndex >= sourcePages || relocatedConfig);
                Buffer.BlockCopy(targetSpare, 0, outputRaw, raw + DataSize, SpareSize);
                WriteEcc(outputRaw, raw);
                if ((pageIndex & 0x1FFF) == 0) Report(progress, 25 + pageIndex * 65 / targetPages);
            }
            WriteAtomic(output, outputRaw);
            Report(progress, 100);
        }

        private static void TranslateSmallSpare(byte[] source, int sourceLayout, byte[] target,
            int targetLayout, int logicalBlock, bool synthesized)
        {
            Array.Clear(target, 0, target.Length);
            int blockId;
            byte seq0, seq1, seq2, seq3, bad;
            if (synthesized)
            {
                blockId = logicalBlock;
                seq0 = seq1 = seq2 = seq3 = 0;
                bad = 0xFF;
                for (int i = 7; i < 13; i++) target[i] = 0;
            }
            else if (sourceLayout == 1)
            {
                blockId = source[1] | (source[2] << 8);
                seq0 = source[0]; seq1 = source[3]; seq2 = source[4]; seq3 = source[6]; bad = source[5];
                Buffer.BlockCopy(source, 7, target, 7, 6);
            }
            else
            {
                blockId = source[0] | (source[1] << 8);
                seq0 = source[2]; seq1 = source[3]; seq2 = source[4]; seq3 = source[6]; bad = source[5];
                Buffer.BlockCopy(source, 7, target, 7, 6);
            }

            if (targetLayout == 1)
            {
                target[0] = seq0;
                target[1] = (byte)blockId;
                target[2] = (byte)(blockId >> 8);
            }
            else
            {
                target[0] = (byte)blockId;
                target[1] = (byte)(blockId >> 8);
                target[2] = seq0;
            }
            target[3] = seq1;
            target[4] = seq2;
            target[5] = bad;
            target[6] = seq3;
        }

        private static void CopyPrefixAtomic(string input, string output, int count, Action<int> progress)
        {
            if (new FileInfo(input).Length < count)
                throw new InvalidDataException("The input image is smaller than the required output system area.");
            string directory = Path.GetDirectoryName(output);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = output + ".tmp";
            byte[] buffer = new byte[0x100000];
            try
            {
                using (FileStream source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int remaining = count;
                    while (remaining > 0)
                    {
                        int wanted = Math.Min(buffer.Length, remaining);
                        ReadExactly(source, buffer, 0, wanted);
                        destination.Write(buffer, 0, wanted);
                        remaining -= wanted;
                        Report(progress, (count - remaining) * 100 / count);
                    }
                    destination.Flush();
                }
                ReplaceOutput(temporary, output);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }

        private static void WriteAtomic(string output, byte[] data)
        {
            string directory = Path.GetDirectoryName(output);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = output + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                ReplaceOutput(temporary, output);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }

        private static void ReplaceOutput(string temporary, string output)
        {
            if (File.Exists(output)) File.Delete(output);
            File.Move(temporary, output);
        }

        private static NandImageInfo NewInfo(NandImageKind kind, long length, int layout, int pagesPerBlock, string description)
        {
            return new NandImageInfo
            {
                Kind = kind,
                FileLength = length,
                SpareLayout = layout,
                PagesPerBlock = pagesPerBlock,
                Description = description
            };
        }

        private static string SystemSuffix(long length)
        {
            return length == SystemRawSize ? " (64 MB system area)" : " (full dump)";
        }

        private static byte[] Slice(byte[] data, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(data, offset, result, 0, count);
            return result;
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

        private static void Fill(byte[] data, int offset, int count, byte value)
        {
            int end = offset + count;
            for (int i = offset; i < end; i++) data[i] = value;
        }

        private static bool IsAll(byte[] data, int offset, int count, byte value)
        {
            int end = offset + count;
            for (int i = offset; i < end; i++)
                if (data[i] != value) return false;
            return true;
        }

        private static byte[] GetLoadedCpuKeyForCfRewrite()
        {
            string value = (variables.cpukey ?? String.Empty).Trim();
            if (value.Length != 32)
                throw new InvalidDataException(
                    "This conversion must rewrite CF's CG block table. Load the NAND with its correct CPU key before converting it.");

            byte[] result = new byte[0x10];
            try
            {
                for (int index = 0; index < result.Length; index++)
                    result[index] = System.Convert.ToByte(value.Substring(index * 2, 2), 16);
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    "The loaded CPU key is not valid. Load the NAND with its correct 32-digit hexadecimal CPU key before converting it.");
            }
            return result;
        }

        private static byte[] EncryptRewrittenCf(byte[] decryptedCf, byte[] originalEncryptedCf,
            byte[] cpuKey)
        {
            if (decryptedCf == null || originalEncryptedCf == null ||
                decryptedCf.Length != originalEncryptedCf.Length || decryptedCf.Length < 0x230)
                throw new InvalidDataException("CF rewrite received an invalid bootloader buffer.");

            // decrypt_CF exposes the derived RC4 key at +0x20.  CF's CPU-key
            // HMAC covers decrypted bytes [0, 0x220), and the HMAC itself is
            // part of the RC4-encrypted body at +0x220.  It must therefore be
            // inserted before encrypting +0x30 onward.  Nand.encrypt_CF writes
            // it afterward and does not reproduce the on-flash CF format.
            byte[] hash;
            using (HMACSHA1 hmac = new HMACSHA1(cpuKey))
                hash = hmac.ComputeHash(decryptedCf, 0, 0x220);
            Buffer.BlockCopy(hash, 0, decryptedCf, 0x220, 0x10);

            byte[] rc4Key = Slice(decryptedCf, 0x20, 0x10);
            byte[] encryptedBody = Slice(decryptedCf, 0x30, decryptedCf.Length - 0x30);
            Oper.RC4_v(ref encryptedBody, rc4Key);

            byte[] result = new byte[decryptedCf.Length];
            Buffer.BlockCopy(decryptedCf, 0, result, 0, 0x20);
            Buffer.BlockCopy(originalEncryptedCf, 0x20, result, 0x20, 0x10);
            Buffer.BlockCopy(encryptedBody, 0, result, 0x30, encryptedBody.Length);
            return result;
        }

        private static void Report(Action<int> progress, int value)
        {
            if (progress != null) progress(Math.Max(0, Math.Min(100, value)));
        }

        private static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static int ReadUInt16BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static void WriteUInt16BigEndian(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
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
            public uint OldPatchSlotSize;
            public uint NewPatchSlotSize;
        }

        private static class OneGbGeometryConverter
        {
            private const int SectorCount = LargeSmallControllerDataSize / DataSize;
    
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
    
            internal static Nand1GbConversionReport ConvertFromLegacyBigBlock(string inputPath,
                string outputPath, Action<int> progress)
            {
                string input = Path.GetFullPath(inputPath);
                string output = Path.GetFullPath(outputPath);
                if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Input and output paths must be different.");
    
                long inputLength = new FileInfo(input).Length;
                if (inputLength == 0x42000000L)
                    throw new InvalidDataException("The source is already a full 1 GB NAND image.");
                if (inputLength != SystemRawSize && inputLength != Full256RawSize && inputLength != Full512RawSize)
                {
                    throw new InvalidDataException(String.Format(
                        "Expected a raw 64, 256, or 512 MB NAND image (0x{0:X}, 0x{1:X}, or 0x{2:X} bytes); got 0x{3:X} bytes.",
                        SystemRawSize, Full256RawSize, Full512RawSize, inputLength));
                }
    
                BadBlock.NandGeometry sourceGeometry = BadBlock.GetGeometry(input, false);
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
                byte[] data = new byte[LargeSmallControllerDataSize];
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
                Dictionary<int, byte[]> bootXells = CaptureBootXells(data);
    
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
                if (smcConfigVersion != 4 && smcConfigVersion != 5)
                    throw new InvalidDataException(String.Format(
                        "Relocated SMC config has unsupported structure version {0}; PSB/KSB 1 GB conversion expects version 4 or 5.",
                        smcConfigVersion));
                ValidateCapturedBootXells(data, bootXells);
    
                uint oldHeaderSize = ReadUInt32BigEndian(data, HeaderSizeOffset);
                uint oldSysUpdate = ReadUInt32BigEndian(data, HeaderSysUpdateOffset);
                uint oldPatchSlotSize = ReadUInt32BigEndian(data, HeaderSysUpdateSizeOffset);
                int updateCount = ReadUInt16BigEndian(data, HeaderSysUpdateCountOffset);
                ValidateSystemUpdateSlots(data, oldSysUpdate, oldPatchSlotSize, updateCount);

                // CD consumes these as logical offsets from the ECC-stripped
                // NAND aperture. Changing physical erase-block geometry must
                // not change a retail or devkit image's CF/CG candidate layout.
                uint newPatchSlotSize = oldPatchSlotSize;
                uint newHeaderSize = oldHeaderSize;
                uint newSysUpdate = oldSysUpdate;
                RelocateSystemUpdate(data, spare, oldSysUpdate, oldPatchSlotSize,
                    updateCount, newSysUpdate, newPatchSlotSize);
                WriteUInt32BigEndian(data, HeaderSizeOffset, newHeaderSize);
                WriteUInt32BigEndian(data, HeaderSysUpdateOffset, newSysUpdate);
                WriteUInt32BigEndian(data, HeaderSysUpdateSizeOffset, newPatchSlotSize);
                ValidateSystemUpdateSlots(data, newSysUpdate, newPatchSlotSize, updateCount);
                ValidateCapturedBootXells(data, bootXells);
    
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
                    OldPatchSlotSize = oldPatchSlotSize,
                    NewPatchSlotSize = newPatchSlotSize
                };
            }
    
            internal static Nand1GbConversionReport ConvertToLegacyBigBlock(string inputPath, string outputPath,
                Action<int> progress)
            {
                string input = Path.GetFullPath(inputPath);
                string output = Path.GetFullPath(outputPath);
                if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Input and output paths must be different.");
    
                long inputLength = new FileInfo(input).Length;
                if (inputLength != SystemRawSize && inputLength != Full1024RawSize)
                {
                    throw new InvalidDataException(String.Format(
                        "Expected a 64 MB system image or full 1024 MB raw image (0x{0:X} or 0x{1:X} bytes); got 0x{2:X} bytes.",
                        SystemRawSize, Full1024RawSize, inputLength));
                }
    
                BadBlock.NandGeometry sourceGeometry = BadBlock.GetGeometry(input, false);
                if (sourceGeometry.PagesPerBlock != TargetPagesPerBlock)
                    throw new InvalidDataException("The source does not use 1024 MB NAND geometry.");
    
                byte[] source = new byte[SystemRawSize];
                using (FileStream stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                    ReadExactly(stream, source, 0, source.Length);
                if (source[0x4400] != 0xFF)
                    throw new InvalidDataException("The source is not a PSB/KSB layout-2 big-block image.");
    
                byte[] data = new byte[LargeSmallControllerDataSize];
                byte[] spare = new byte[SectorCount * SpareSize];
                int sourceValid = 0;
                int sourceInvalid = 0;
                for (int sector = 0; sector < SectorCount; sector++)
                {
                    int rawOffset = sector * RawPageSize;
                    Buffer.BlockCopy(source, rawOffset, data, sector * DataSize, DataSize);
                    Buffer.BlockCopy(source, rawOffset + DataSize, spare, sector * SpareSize, SpareSize);
                    if (!IsAll(source, rawOffset, RawPageSize, 0xFF))
                    {
                        uint stored = ReadUInt32LittleEndian(source, rawOffset + 0x20C) & 0xFFFFFFC0U;
                        if (stored == CalculateEcc(source, rawOffset)) sourceValid++;
                        else sourceInvalid++;
                    }
                    if ((sector & 0x1FFF) == 0) Report(progress, sector * 20 / SectorCount);
                }
                source = null;
                Dictionary<int, byte[]> bootXells = CaptureBootXells(data);
    
                // Capture configuration before relocating the filesystem. The
                // source 1 GB config window begins at 0x03B00000, while the
                // legacy filesystem destination extends through 0x03B80000;
                // relocating first would overwrite the first two config blocks.
                byte[] configData = new byte[SourceBlockSize * 4];
                byte[] configSpare = new byte[SourcePagesPerBlock * SpareSize * 4];
                for (int block = 0; block < 4; block++)
                {
                    int sourceData = TargetConfigStart + block * TargetBlockSize;
                    Buffer.BlockCopy(data, sourceData, configData, block * SourceBlockSize, SourceBlockSize);
                    Buffer.BlockCopy(spare, (sourceData / DataSize) * SpareSize, configSpare,
                        block * SourcePagesPerBlock * SpareSize, SourcePagesPerBlock * SpareSize);
                }
    
                byte[] fileSystemData = new byte[FileSystemSize];
                byte[] fileSystemSpare = new byte[(FileSystemSize / DataSize) * SpareSize];
                Buffer.BlockCopy(data, TargetFileSystemStart, fileSystemData, 0, fileSystemData.Length);
                Buffer.BlockCopy(spare, (TargetFileSystemStart / DataSize) * SpareSize,
                    fileSystemSpare, 0, fileSystemSpare.Length);
                Fill(data, TargetFileSystemStart, SourceFileSystemStart - TargetFileSystemStart, 0xFF);
                Fill(spare, (TargetFileSystemStart / DataSize) * SpareSize,
                    ((SourceFileSystemStart - TargetFileSystemStart) / DataSize) * SpareSize, 0xFF);
                Buffer.BlockCopy(fileSystemData, 0, data, SourceFileSystemStart, fileSystemData.Length);
                Buffer.BlockCopy(fileSystemSpare, 0, spare, (SourceFileSystemStart / DataSize) * SpareSize,
                    fileSystemSpare.Length);
    
                int translatedMetadata = 0;
                int destinationFirstSector = SourceFileSystemStart / DataSize;
                int fileSystemSectorCount = FileSystemSize / DataSize;
                for (int index = 0; index < fileSystemSectorCount; index++)
                {
                    int metadata = (destinationFirstSector + index) * SpareSize;
                    if (spare[metadata + 7] != 0x05 || spare[metadata + 8] != 0x30 || spare[metadata + 9] != 0x04)
                        continue;
                    spare[metadata + 7] = 0x0A;
                    spare[metadata + 8] = 0x60;
                    translatedMetadata++;
                }
                if (translatedMetadata == 0)
                    throw new InvalidDataException("No 1024 MB filesystem layout metadata (05/30/04) was found.");
    
                // Collapse the first 128 KiB of each 256 KiB 1 GB configuration
                // block into the four contiguous legacy configuration blocks.
                Fill(data, SourceConfigStart, SourceBlockSize * 4, 0xFF);
                Fill(spare, (SourceConfigStart / DataSize) * SpareSize,
                    SourcePagesPerBlock * SpareSize * 4, 0xFF);
                Buffer.BlockCopy(configData, 0, data, SourceConfigStart, configData.Length);
                Buffer.BlockCopy(configSpare, 0, spare, (SourceConfigStart / DataSize) * SpareSize,
                    configSpare.Length);
    
                Fill(data, SourceReserveStartBlock * SourceBlockSize,
                    data.Length - SourceReserveStartBlock * SourceBlockSize, 0xFF);
                Fill(spare, SourceReserveStartBlock * SourcePagesPerBlock * SpareSize,
                    spare.Length - SourceReserveStartBlock * SourcePagesPerBlock * SpareSize, 0xFF);
                ValidateCapturedBootXells(data, bootXells);
    
                uint oldHeaderSize = ReadUInt32BigEndian(data, HeaderSizeOffset);
                uint oldSysUpdate = ReadUInt32BigEndian(data, HeaderSysUpdateOffset);
                uint oldPatchSlotSize = ReadUInt32BigEndian(data, HeaderSysUpdateSizeOffset);
                int updateCount = ReadUInt16BigEndian(data, HeaderSysUpdateCountOffset);
                ValidateSystemUpdateSlots(data, oldSysUpdate, oldPatchSlotSize, updateCount);
                uint newPatchSlotSize = oldPatchSlotSize;
                uint newHeaderSize = oldHeaderSize;
                uint newSysUpdate = oldSysUpdate;
                RelocateSystemUpdate(data, spare, oldSysUpdate, oldPatchSlotSize,
                    updateCount, newSysUpdate, newPatchSlotSize);
                WriteUInt32BigEndian(data, HeaderSizeOffset, newHeaderSize);
                WriteUInt32BigEndian(data, HeaderSysUpdateOffset, newSysUpdate);
                WriteUInt32BigEndian(data, HeaderSysUpdateSizeOffset, newPatchSlotSize);
                ValidateSystemUpdateSlots(data, newSysUpdate, newPatchSlotSize, updateCount);
                ValidateCapturedBootXells(data, bootXells);
    
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
    
                    int targetBlock = sector / SourcePagesPerBlock;
                    outputSystem[rawOffset + DataSize] = 0xFF;
                    outputSystem[rawOffset + DataSize + 1] = (byte)targetBlock;
                    outputSystem[rawOffset + DataSize + 2] = (byte)(targetBlock >> 8);
                    outputSystem[rawOffset + DataSize + 13] = 0;
                    outputSystem[rawOffset + DataSize + 14] = 0;
                    outputSystem[rawOffset + DataSize + 15] = 0;
                    WriteEcc(outputSystem, rawOffset);
                    programmedPages++;
                    if ((sector & 0x1FFF) == 0) Report(progress, 30 + sector * 55 / SectorCount);
                }
    
                int invalidOutput = 0;
                for (int sector = 0; sector < SectorCount; sector++)
                {
                    int rawOffset = sector * RawPageSize;
                    if (!IsAll(outputSystem, rawOffset, RawPageSize, 0xFF) &&
                        ((ReadUInt32LittleEndian(outputSystem, rawOffset + 0x20C) & 0xFFFFFFC0U) != CalculateEcc(outputSystem, rawOffset)))
                        invalidOutput++;
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
                    SourceBadBlocks = new int[0],
                    AppliedRemaps = new Dictionary<int, int>(),
                    BadBlocksWithoutRemaps = new int[0],
                    TranslatedFileSystemMetadataPages = translatedMetadata,
                    SynthesizedMetadataPages = synthesizedMetadata,
                    OutputProgrammedPages = programmedPages,
                    OutputInvalidEccPages = invalidOutput,
                    SmcConfigVersion = data[SourceConfigStart + (3 * SourceBlockSize) + 0x0E],
                    OldHeaderSize = oldHeaderSize,
                    NewHeaderSize = newHeaderSize,
                    OldSysUpdateAddress = oldSysUpdate,
                    NewSysUpdateAddress = newSysUpdate,
                    OldPatchSlotSize = oldPatchSlotSize,
                    NewPatchSlotSize = newPatchSlotSize
                };
            }
    
            private static void ValidateSystemUpdateSlots(byte[] data, uint address,
                uint patchSlotSizeField, int updateCount)
            {
                if (updateCount < 0 || updateCount > 8)
                    throw new InvalidDataException("The flash header contains an invalid system-update slot count.");
                if (updateCount == 0) return;

                uint slotSize = patchSlotSizeField == 0 ? 0x10000U : patchSlotSizeField;
                if (slotSize > 0x100000)
                    throw new InvalidDataException("The flash header contains an unsupported system-update slot size.");

                long end = address + (long)slotSize * updateCount;
                if (address > data.Length || end > data.Length)
                    throw new InvalidDataException("A system-update slot falls outside the 64 MB system area.");
            }

            private static void RelocateSystemUpdate(byte[] data, byte[] spare,
                uint oldAddress, uint oldSlotSize, int updateCount,
                uint newAddress, uint newSlotSize)
            {
                uint normalizedOldSlotSize = oldSlotSize == 0 ? 0x10000U : oldSlotSize;
                uint normalizedNewSlotSize = newSlotSize == 0 ? 0x10000U : newSlotSize;
                if (oldAddress == newAddress && normalizedOldSlotSize == normalizedNewSlotSize)
                    return;

                // CD uses +0x64 as the first candidate address and +0x70 as
                // the candidate stride.  Only the occupied inline candidate
                // windows move with those fields.  A CF contains a table of
                // absolute logical 0x4000-byte block numbers for the remainder
                // of CG; moving that external extent without decrypting and
                // rebuilding CF leaves the table pointing at the wrong bytes.
                if (normalizedOldSlotSize != normalizedNewSlotSize)
                    throw new InvalidDataException(
                        "Changing the system-update slot stride requires rebuilding CF/CG.");
                if ((oldAddress | newAddress | normalizedOldSlotSize) % DataSize != 0)
                    throw new InvalidDataException("The system-update relocation is not NAND-page aligned.");

                int slotLength = checked((int)normalizedOldSlotSize);
                byte[][] slotData = new byte[updateCount][];
                byte[][] slotSpare = new byte[updateCount][];
                for (int slot = 0; slot < updateCount; slot++)
                {
                    int sourceOffset = checked((int)(oldAddress + (long)slot * normalizedOldSlotSize));
                    int destinationOffset = checked((int)(newAddress + (long)slot * normalizedNewSlotSize));
                    if (sourceOffset < 0 || sourceOffset + slotLength > data.Length ||
                        destinationOffset < 0 || destinationOffset + slotLength > data.Length)
                        throw new InvalidDataException("A relocated system-update slot falls outside the 64 MB system area.");

                    bool occupied = (data[sourceOffset] == (byte)'C' || data[sourceOffset] == (byte)'S') &&
                        data[sourceOffset + 1] == (byte)'F';
                    if (!occupied) continue;

                    slotData[slot] = new byte[slotLength];
                    slotSpare[slot] = new byte[(slotLength / DataSize) * SpareSize];
                    Buffer.BlockCopy(data, sourceOffset, slotData[slot], 0, slotLength);
                    Buffer.BlockCopy(spare, (sourceOffset / DataSize) * SpareSize,
                        slotSpare[slot], 0, slotSpare[slot].Length);
                }

                // Clear only occupied source candidates.  Do not clear nominal
                // empty candidates: CF is allowed to reference those addresses
                // as CG overflow storage (the common RGH3 BOS layout does so).
                for (int slot = 0; slot < updateCount; slot++)
                {
                    if (slotData[slot] == null) continue;
                    int sourceOffset = checked((int)(oldAddress + (long)slot * normalizedOldSlotSize));
                    Fill(data, sourceOffset, slotLength, 0xFF);
                    Fill(spare, (sourceOffset / DataSize) * SpareSize,
                        slotSpare[slot].Length, 0xFF);
                }

                for (int slot = 0; slot < updateCount; slot++)
                {
                    if (slotData[slot] == null) continue;
                    int destinationOffset = checked((int)(newAddress + (long)slot * normalizedNewSlotSize));
                    Buffer.BlockCopy(slotData[slot], 0, data, destinationOffset, slotLength);
                    Buffer.BlockCopy(slotSpare[slot], 0, spare,
                        (destinationOffset / DataSize) * SpareSize, slotSpare[slot].Length);
                    NandImageConverter.ValidateSystemUpdateRelocation(
                        slotData[slot], data, 0, destinationOffset, slotLength);
                }
            }

        }
    }
}
