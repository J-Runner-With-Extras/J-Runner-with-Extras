using System;
using System.Globalization;

namespace JRunner
{
    public enum SfcxControllerType
    {
        Xsb = 0,
        Psb = 1,
        Ksb = 2,
        Unsupported = 3
    }

    /// <summary>
    /// Decodes the Xbox 360 SFCX flash configuration register. Geometry is
    /// derived from the controller major/minor fields instead of matching a
    /// table of complete register values.
    /// </summary>
    public sealed class SfcxConfig
    {
        public const int DataBytesPerPage = 0x200;
        public const int RawBytesPerPage = 0x210;
        public const int TransferRawBytes = 0x4200;

        private SfcxConfig(uint raw)
        {
            Raw = raw;
            Major = (int)((raw >> 17) & 0x3);
            Minor = (int)((raw >> 4) & 0x3);
            ControllerType = Major <= 2 ? (SfcxControllerType)Major : SfcxControllerType.Unsupported;
            IsNoDevice = raw == 0 || raw == UInt32.MaxValue;
            IsEmmc = (raw & 0xF0000000U) == 0xC0000000U;

            if (IsNoDevice || IsEmmc || ControllerType == SfcxControllerType.Unsupported)
                return;

            if (ControllerType == SfcxControllerType.Xsb)
            {
                PagesPerBlock = 32;
                TotalBlockCount = 0x200U << Minor;
                SystemBlockCount = TotalBlockCount;
            }
            else if (Minor <= 1)
            {
                // PSB minor 0/1 and KSB minor 0 are the 16MB, 16KB-block
                // geometry. KSB minor 1 is the 64MB, 16KB-block geometry.
                PagesPerBlock = 32;
                TotalBlockCount = ControllerType == SfcxControllerType.Ksb && Minor == 1
                    ? 0x1000U
                    : 0x400U;
                SystemBlockCount = TotalBlockCount;
            }
            else
            {
                int capacityShift = (int)(((raw >> 19) & 0x3) + ((raw >> 21) & 0xF));
                if (capacityShift >= 25) return;

                if (Minor == 2)
                {
                    PagesPerBlock = 256; // 128KB erase blocks
                    TotalBlockCount = 0x40U << capacityShift;
                    SystemBlockCount = 512;
                }
                else
                {
                    PagesPerBlock = 512; // 256KB erase blocks
                    TotalBlockCount = 0x20U << capacityShift;
                    SystemBlockCount = 256;
                }
            }

            if (TotalBlockCount < SystemBlockCount) return;
            MuBlockCount = TotalBlockCount - SystemBlockCount;
            IsSupported = true;
        }

        public uint Raw { get; private set; }
        public int Major { get; private set; }
        public int Minor { get; private set; }
        public SfcxControllerType ControllerType { get; private set; }
        public bool IsNoDevice { get; private set; }
        public bool IsEmmc { get; private set; }
        public bool IsSupported { get; private set; }
        public uint PagesPerBlock { get; private set; }
        public uint TotalBlockCount { get; private set; }
        public uint SystemBlockCount { get; private set; }
        public uint MuBlockCount { get; private set; }

        public bool IsBigBlock { get { return PagesPerBlock > 32; } }
        public bool HasMemoryUnit { get { return MuBlockCount != 0; } }
        public int Layout { get { return !IsSupported ? -1 : (IsBigBlock ? 2 : (ControllerType == SfcxControllerType.Xsb ? 0 : 1)); } }
        public long BlockDataBytes { get { return (long)PagesPerBlock * DataBytesPerPage; } }
        public long TotalDataBytes { get { return (long)TotalBlockCount * BlockDataBytes; } }
        public long SystemDataBytes { get { return (long)SystemBlockCount * BlockDataBytes; } }
        public long TotalRawBytes { get { return (long)TotalBlockCount * PagesPerBlock * RawBytesPerPage; } }
        public long SystemRawBytes { get { return (long)SystemBlockCount * PagesPerBlock * RawBytesPerPage; } }
        public int TotalSizeMb { get { return (int)(TotalDataBytes / 0x100000); } }
        public int SystemSizeMb { get { return (int)(SystemDataBytes / 0x100000); } }
        public int SystemReserveBlockCount
        {
            get
            {
                if (!IsBigBlock) return 0x20;
                return (int)(0x400000 / BlockDataBytes);
            }
        }
        public int SystemReserveStartBlock { get { return IsSupported ? (int)SystemBlockCount - SystemReserveBlockCount : 0; } }
        public int SystemLastBlock { get { return IsSupported ? (int)SystemBlockCount - 1 : 0; } }
        // Legacy small-block remapping is confined to the first 16MB window.
        // Big-block remapping uses the end of the decoded 64MB system region.
        public int RemapLastBlock { get { return IsBigBlock ? SystemLastBlock : 0x3FF; } }
        public Nandsize FullNandSize { get { return (Nandsize)(TotalRawBytes / TransferRawBytes); } }
        public Nandsize SystemNandSize { get { return (Nandsize)(SystemRawBytes / TransferRawBytes); } }

        public string FamilyName
        {
            get
            {
                switch (ControllerType)
                {
                    case SfcxControllerType.Xsb: return "Xenon, Zephyr, Falcon";
                    case SfcxControllerType.Psb: return "Jasper, Trinity";
                    case SfcxControllerType.Ksb: return "Corona, Winchester";
                    default: return "Unsupported";
                }
            }
        }

        public string Description
        {
            get
            {
                if (IsEmmc) return "Corona, Winchester: eMMC";
                if (!IsSupported) return "Unsupported SFCX configuration";
                return FamilyName + ": " + TotalSizeMb + "MB";
            }
        }

        public int SelectionGroup
        {
            get
            {
                if (!HasMemoryUnit) return 0;
                if (TotalSizeMb == 256) return 2;
                if (TotalSizeMb == 512) return 3;
                if (TotalSizeMb == 1024) return 4;
                return 1;
            }
        }

        public static SfcxConfig Decode(uint raw)
        {
            return new SfcxConfig(raw);
        }

        public static bool TryParse(string value, out SfcxConfig config)
        {
            config = null;
            if (String.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);

            uint raw;
            if (!UInt32.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw)) return false;
            config = Decode(raw);
            return true;
        }
    }
}
