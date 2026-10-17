using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Management;

namespace JRunner
{
    public class PicoFlasher : IISD
    {
        public delegate void updateProgress_t(int progress);
        public event updateProgress_t UpdateProgress;

        public delegate void log_t(string text);
        public event log_t log;

        // Set while an operation holds the command port
        public bool InUse = false;

        // Protocol version of the connected firmware
        public UInt32 Version = 0;

        // Commands are a 1 byte opcode and a 32 bit little endian argument
        enum COMMANDS : byte
        {
            GET_VERSION = 0x00,
            GET_FLASH_CONFIG,
            READ_FLASH,
            WRITE_FLASH,
            READ_FLASH_STREAM,
            ERASE_FLASH,
            WRITE_FLASH_STREAM,

            SET_SMC_WORKAROUND = 0x20,
            STOP_SMC,
            START_SMC,
            SET_SMC_DBG_EN,
            READ_REG,
            WRITE_REG,
            I2C_TRANSFER,
            SET_SPI_CLOCK,

            EMMC_DETECT = 0x50,
            EMMC_INIT,
            EMMC_GET_CID,
            EMMC_GET_CSD,
            EMMC_GET_EXT_CSD,
            EMMC_READ,
            EMMC_READ_STREAM,
            EMMC_WRITE,
            EMMC_WRITE_STREAM,

            ISD1200_INIT = 0xA0,
            ISD1200_DEINIT,
            ISD1200_READ_ID,
            ISD1200_READ_FLASH,
            ISD1200_ERASE_FLASH,
            ISD1200_WRITE_FLASH,
            ISD1200_PLAY_VOICE,
            ISD1200_EXEC_MACRO,
            ISD1200_RESET,

            REBOOT_TO_BOOTLOADER = 0xFE
        }

        // On eMMC we only need the first 48MB
        private const uint EmmcSystemBytes = 48 * 1024 * 1024;

        // Progress is reported in J-Runner blocks of 0x20 pages
        private const uint PagesPerBlock = 0x20;

        // Pages per serial write of a write stream
        private const uint StreamWritePages = 8;

        // A failure status reported by the firmware
        private class StatusException : Exception
        {
            public StatusException(string what, UInt32 status)
                : base("Error: " + status.ToString("X") + " (" + what + ")")
            {
            }
        }

        // What was found on the other end of the PicoFlasher
        private class Target
        {
            public UInt32 FlashConfig;
            public SfcxConfig Config;
            public bool IsEmmc;
        }

        private readonly object sync = new object();

        // Command port of the operation in progress
        private SerialPort serial;

        private static List<string> ComPortNamesWine(string vid, string pid)
        {
            var results = new List<string>();
            char[] trimChars = { '\n', '\r', '\t', ' ' };

            if (File.Exists("/tmp/jrunner_comport.txt")) File.Delete("/tmp/jrunner_comport.txt");

            // Using a helper script, write a list of Wine COM ports
            // matching the specified PID/VID to a file
            try
            {
                Process p = new Process();
                ProcessStartInfo psi = new ProcessStartInfo();

                // Determine the "linux path" to our helper script
                psi.FileName = @"C:\windows\system32\winepath.exe";
                psi.UseShellExecute = false;
                psi.Arguments = "common/scripts/getWineComPorts.sh";
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                p.StartInfo = psi;

                p.Start();

                string winepath = p.StandardOutput.ReadToEnd().TrimEnd(trimChars);

                p.WaitForExit();

                if(variables.debugMode) Console.WriteLine(winepath);

                Process p2 = new Process();
                ProcessStartInfo psi2 = new ProcessStartInfo();

                // Now we actually run the script
                psi2.FileName = "/bin/bash";
                psi2.UseShellExecute = false;
                psi2.Arguments = winepath + $" {vid} {pid}";
                psi2.CreateNoWindow = true;
                p2.StartInfo = psi2;

                p2.Start();
                p2.WaitForExit();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            string[] res = null;

            var deadline = DateTime.UtcNow.AddSeconds(1);

            // Loop a bit until we are able to successfully read the file
            // containing our COM ports from disk
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    res = File.ReadAllLines("/tmp/jrunner_comport.txt");
                    break; // Success, exit retry loop
                }
                catch (Exception ex)
                {
                    if (variables.debugMode) Console.WriteLine($"Read failed: {ex.Message}. Retrying...");
                    Thread.Sleep(100); // wait before retrying
                }
            }

            // The helper script may not have produced the file in time
            if (res != null)
            {
                foreach (string s in res)
                {
                    results.Add(s);
                }
            }

            return results;
        }

        private static List<string> ComPortNames(string vid, string pid)
        {
            string vidPidPattern = string.Format("VID_{0}&PID_{1}", vid.ToUpper(), pid.ToUpper());

            var results = new Dictionary<string, int>();

            using (var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
            {
                ManagementObjectCollection ports = searcher.Get();
                if (variables.debugMode) Console.WriteLine("PicoFlasher: COM ports found: " + ports.Count);

                foreach (ManagementObject port in ports)
                {
                    string deviceId = port["DeviceID"] != null ? port["DeviceID"].ToString() : "";
                    string name = port["Name"] != null ? port["Name"].ToString() : "";

                    if (deviceId.ToUpper().IndexOf(vidPidPattern, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    // Extract COM port name from e.g. "USB Serial Device (COM3)"
                    int comStart = name.IndexOf("(COM", StringComparison.Ordinal) + 1;
                    int comEnd = name.IndexOf(')', comStart);

                    if (comStart <= 0 || comEnd <= comStart)
                    {
                        if (variables.debugMode) Console.WriteLine("PicoFlasher: VID/PID matched but could not parse COM port");
                        continue;
                    }

                    string comPort = name.Substring(comStart, comEnd - comStart);

                    // Extract MI_xx interface number from DeviceID
                    // e.g. USB\VID_1234&PID_5678&MI_01\7&...
                    Match miMatch = Regex.Match(deviceId, @"MI_([0-9A-Fa-f]+)", RegexOptions.IgnoreCase);
                    int interfaceIndex = miMatch.Success
                        ? int.Parse(miMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)
                        : 0;

                    // Deduplicate- Do not set the com port if it was already found in the list
                    // with a lower interface index than what we're furrently inspecting. This
                    // will keep the COM port with the lowest interface index if we run in to a
                    // situation where the WMI query returns duplicates
                    if (!(results.ContainsKey(comPort) && interfaceIndex > results[comPort]))
                    {
                        results[comPort] = interfaceIndex;
                    }

                }
            }

            List<string> ordered = results
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            if (variables.debugMode)
            {
                Console.WriteLine("PicoFlasher: COM port list (" + ordered.Count + " entries):");
                for (int i = 0; i < ordered.Count; i++)
                    Console.WriteLine("PicoFlasher:   [" + i + "] " + ordered[i] + " (MI_" + results[ordered[i]] + ")");
            }

            return ordered;
        }

        private void Send(COMMANDS cmd, UInt32 lba = 0, byte[] data = null, int offset = 0, int count = 0)
        {
            byte[] buf = new byte[5 + count];
            buf[0] = (byte)cmd;
            buf[1] = (byte)lba;
            buf[2] = (byte)(lba >> 8);
            buf[3] = (byte)(lba >> 16);
            buf[4] = (byte)(lba >> 24);
            if (count > 0)
                Buffer.BlockCopy(data, offset, buf, 5, count);

            serial.Write(buf, 0, buf.Length);
        }

        // A timeout throws, a zeroed buffer would read as success
        private void Recv(byte[] buf, int offset, int count)
        {
            while (count > 0)
            {
                int got = serial.Read(buf, offset, count);
                offset += got;
                count -= got;
            }
        }

        private byte[] Recv(int count)
        {
            byte[] buf = new byte[count];
            Recv(buf, 0, count);
            return buf;
        }

        private UInt32 RecvUInt32()
        {
            return BitConverter.ToUInt32(Recv(4), 0);
        }

        private byte RecvUInt8()
        {
            return Recv(1)[0];
        }

        // Discard what an abandoned stream left on the line: let the firmware
        // time out the stream (100ms idle), then drop data until the line is quiet
        private void Drain()
        {
            Thread.Sleep(250);
            serial.DiscardInBuffer();

            // Polled, read timeouts never fire under Wine
            Stopwatch quiet = Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < 100)
            {
                if (serial.BytesToRead > 0)
                {
                    serial.DiscardInBuffer();
                    quiet.Restart();
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private void ClosePort()
        {
            try
            {
                if (serial != null && serial.IsOpen)
                {
                    if (Version >= 4)
                    {
                        // Restart the SMC before we close the serial port.
                        // This allows us to leave the PicoFlasher connected
                        // and use things like debug UART
                        Send(COMMANDS.START_SMC);
                    }

                    serial.Close();
                }
            }
            catch { }

            serial = null;
            InUse = false;
        }

        // Claims the command port for one operation and stops the SMC for flash
        // access, the SMC runs again between operations so the console can be used
        private bool OpenPort()
        {
            lock (sync)
            {
                if (InUse)
                {
                    Console.WriteLine("PicoFlasher is busy");
                    return false;
                }
                InUse = true;
            }

            try
            {
                List<string> ports;
                if (WineMethods.IsWine())
                {
                    ports = ComPortNamesWine("600D", "7001");
                }
                else
                {
                    ports = ComPortNames("600D", "7001");
                }

                if (ports.Count <= 0)
                {
                    ClosePort();
                    MessageBox.Show("Can't find PicoFlasher COM port\n\nUpdate the PicoFlasher firmware and check your drivers", "Can't", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (variables.debugMode) Console.WriteLine(ports[0]);

                serial = new SerialPort(ports[0]);
                serial.ReadTimeout = 5000;
                serial.WriteTimeout = 5000;
                serial.Open();

                // A previous session may have abandoned a stream
                Drain();

                Version = 0;
                Send(COMMANDS.GET_VERSION);
                Version = RecvUInt32();

                if (variables.debugMode) Console.WriteLine($"PicoFlasher Version: {Version}");

                if (Version < 2)
                {
                    ClosePort();
                    MessageBox.Show("PicoFlasher firmware is too old\n\nUpdate the PicoFlasher firmware to continue", "Can't", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (Version < 3)
                {
                    Console.WriteLine("PicoFlasher: v" + Version + " firmware doesn't support eMMC, only SPI mode is available");
                }

                // The firmware keeps the clock until it is reset, so it is set for every operation
                if (variables.picoSpiClockMhz != 0)
                {
                    if (Version >= 5)
                    {
                        Send(COMMANDS.SET_SPI_CLOCK, (uint)variables.picoSpiClockMhz * 1000);
                        UInt32 hz = RecvUInt32();
                        Console.WriteLine("PicoFlasher: SPI clock {0:F3} MHz", hz / 1e6);
                    }
                    else
                    {
                        Console.WriteLine("PicoFlasher: v" + Version + " firmware doesn't support setting the SPI clock");
                    }
                }

                if (Version >= 4)
                {
                    if (variables.debugMode) Console.WriteLine("PicoFlasher: Enhanced SMC control mode");

                    // We can disable the silly "fallback" mode if the version is reported
                    // as 4 or later. This is a new feature of the hax360 version of PicoFlasher
                    // that does not hold the SMC in reset unless we're actually flashing the
                    // NAND. See https://codeberg.org/hax360/PicoFlasher for more details.
                    Send(COMMANDS.SET_SMC_WORKAROUND, 0); // 0 = false, 1 = true. SMC workaround is enabled by default

                    // Now that the workaround mode is disabled, we can stop the SMC manually.
                    // The firmware handles no further commands until the SMC is stopped.
                    Send(COMMANDS.STOP_SMC);
                }

                return true;
            }
            catch (Exception ex)
            {
                ClosePort();

                if (variables.debugMode) Console.WriteLine(ex.ToString());
                else Console.WriteLine(ex.GetType());

                if (ex.GetType() == typeof(UnauthorizedAccessException) && WineMethods.IsWine())
                {
                    Console.WriteLine("Ensure the current user is part of the dialout group, then reboot the computer.");
                }

                MessageBox.Show("PicoFlasher COM port could not be found\n\nYou may need to update the PicoFlasher firmware or check your connections to continue", "Can't", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // Runs an operation on its own thread with the port open and the SMC
        // stopped, after runs once the operation succeeded and the SMC is running
        private void RunOperation(string name, Action body, Action after = null)
        {
            Thread thread = new Thread(() =>
            {
                if (!OpenPort())
                    return;

                bool done = false;
                try
                {
                    body();
                    done = true;
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine(name + " Cancelled");
                    Console.WriteLine("");
                }
                catch (StatusException ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(name + " Failed!");
                    Console.WriteLine("");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("PicoFlasher has encountered an error");
                    Console.WriteLine("Try to unplug and replug the device");
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    else Console.WriteLine(ex.GetType());
                    Console.WriteLine("");
                }
                finally
                {
                    ClosePort();
                }

                if (done && after != null)
                    after();
            });
            thread.Start();
        }

        // Identifies the connected console, null if there is none to operate on
        private Target DetectConsole()
        {
            Console.WriteLine("Checking Console...");

            Target target = new Target();
            Send(COMMANDS.GET_FLASH_CONFIG);
            target.FlashConfig = RecvUInt32();
            target.Config = SfcxConfig.Decode(target.FlashConfig);

            Console.WriteLine("Flash Config: 0x" + target.FlashConfig.ToString("X8"));

            // Older eMMC PicoFlasher firmware using the eMMC header returns
            // flashconfig 0xFFFFFFFF instead of the console's eMMC flashconfig
            if ((target.Config.IsEmmc || target.FlashConfig == 0xFFFFFFFF) && Version >= 3)
            {
                for (int i = 0; i < 10 && !target.IsEmmc; i++)
                {
                    Send(COMMANDS.EMMC_DETECT);
                    target.IsEmmc = RecvUInt8() != 0;

                    if (variables.debugMode)
                        Console.WriteLine(target.IsEmmc ? "PicoFlasher: eMMC detected." : "PicoFlasher: eMMC flashconfig, but eMMC was not detected. Retrying.");
                }
            }

            if (target.IsEmmc)
            {
                Console.WriteLine("Corona/Winchester: 4GB (eMMC connected)");
                return target;
            }

            if (target.Config.IsEmmc)
            {
                Console.WriteLine("Corona/Winchester: 4GB (eMMC not connected)");
                Console.WriteLine("Console Not Found");
                return null;
            }

            if (target.Config.IsNoDevice)
            {
                Console.WriteLine("Console Not Found");
                return null;
            }

            if (!target.Config.IsSupported)
            {
                Console.WriteLine("Unsupported SFCX flash configuration");
                return null;
            }

            Console.WriteLine(target.Config.Description);
            return target;
        }

        private UInt32 UNSTUFF_BITS(UInt32[] resp, int start, int size)
        {
            int __size = size;
            UInt32 __mask = (UInt32)(__size < 32 ? 1 << __size : 0) - 1;
            int __off = 3 - ((start) / 32);
            int __shft = (start) & 31;
            UInt32 __res;

            __res = resp[__off] >> __shft;
            if (__size + __shft > 32)
                __res |= resp[__off - 1] << ((32 - __shft) % 32);

            return __res & __mask;
        }

        // Initialises the eMMC for access, its registers are shown in debug mode
        private void InitEmmc()
        {
            Send(COMMANDS.EMMC_INIT);
            UInt32 emmc_init_ret = RecvUInt32();
            if (emmc_init_ret != 0)
                throw new StatusException("eMMC init", emmc_init_ret);

            Send(COMMANDS.EMMC_GET_CID);
            byte[] CID = Recv(16);

            Send(COMMANDS.EMMC_GET_CSD);
            byte[] CSD = Recv(16);

            Send(COMMANDS.EMMC_GET_EXT_CSD);
            byte[] ext_csd = Recv(512);

            if (!variables.debugMode)
                return;

            Console.Write("CID: ");
            for (int i = 0; i < CID.Length; i++)
                Console.Write(CID[i].ToString("X2"));
            Console.WriteLine("");

            Console.Write("CSD: ");
            for (int i = 0; i < CSD.Length; i++)
                Console.Write(CSD[i].ToString("X2"));
            Console.WriteLine("");

            UInt32[] csd = new UInt32[4];
            Buffer.BlockCopy(CSD, 0, csd, 0, CSD.Length);

            for (int i = 0; i < 4; i++)
            {
                byte[] bytes = BitConverter.GetBytes(csd[i]);
                Array.Reverse(bytes);
                csd[i] = BitConverter.ToUInt32(bytes, 0);
            }

            Console.WriteLine("CSD_STRUCTURE: " + UNSTUFF_BITS(csd, 126, 2).ToString("X"));
            Console.WriteLine("TAAC: " + UNSTUFF_BITS(csd, 112, 8).ToString("X"));
            Console.WriteLine("TRAN_SPEED: " + UNSTUFF_BITS(csd, 96, 8).ToString("X"));
            Console.WriteLine("READ_BL_LEN: " + UNSTUFF_BITS(csd, 80, 4).ToString("X"));
            Console.WriteLine("READ_BL_PARTIAL: " + UNSTUFF_BITS(csd, 79, 1).ToString("X"));
            Console.WriteLine("C_SIZE: " + UNSTUFF_BITS(csd, 62, 12).ToString("X"));
            Console.WriteLine("C_SIZE_MULT: " + UNSTUFF_BITS(csd, 47, 3).ToString("X"));
            Console.WriteLine("ERASE_GRP_SIZE: " + UNSTUFF_BITS(csd, 42, 5).ToString("X"));

            Console.WriteLine("Extended CSD V1." + ext_csd[194].ToString()); // EXT_CSD_STRUCTURE
            Console.WriteLine(" Spec Version:  " + UNSTUFF_BITS(csd, 122, 4).ToString("X2"));
            Console.WriteLine(" Extended Rev:  1." + ext_csd[192].ToString()); // EXT_CSD_REV

            byte[] devver = new byte[4];
            Buffer.BlockCopy(ext_csd, 262, devver, 0, 2); // EXT_CSD_DEVICE_VERSION

            Console.WriteLine(" Dev Version:   " + BitConverter.ToUInt32(devver, 0).ToString());
            Console.WriteLine(" Cmd Classes:   " + UNSTUFF_BITS(csd, 84, 12).ToString("X2"));
            Console.WriteLine(" Capacity:      " + ((UNSTUFF_BITS(csd, 62, 12) == 0xfff && UNSTUFF_BITS(csd, 47, 3) == 7) ? "High" : "Low"));

            byte card_type = ext_csd[196]; // EXT_CSD_CARD_TYPE
            int speed = 0;
            if ((card_type & (1 << 0)) != 0) // EXT_CSD_CARD_TYPE_HS_26
                speed = (26 << 16) | 26;
            if ((card_type & (1 << 1)) != 0) // EXT_CSD_CARD_TYPE_HS_52
                speed = (52 << 16) | 52;
            if ((card_type & (1 << 2)) != 0) // EXT_CSD_CARD_TYPE_DDR_1_8V
                speed = (52 << 16) | 104;
            if ((card_type & (1 << 4)) != 0) // EXT_CSD_CARD_TYPE_HS200_1_8V
                speed = (200 << 16) | 200;
            if ((card_type & (1 << 6)) != 0) // EXT_CSD_CARD_TYPE_HS400_1_8V
                speed = (200 << 16) | 400;

            Console.WriteLine(" Max Rate:      " + (speed & 0xFFFF).ToString() + " MB/s (" + ((speed >> 16) & 0xFFFF).ToString() + " MHz)");
            Console.Write(" Type Support:  ");
            if ((card_type & (1 << 0)) != 0) // EXT_CSD_CARD_TYPE_HS_26
                Console.Write("HS26");
            if ((card_type & (1 << 1)) != 0) // EXT_CSD_CARD_TYPE_HS_52
                Console.Write(", HS52");
            if ((card_type & (1 << 2)) != 0) // EXT_CSD_CARD_TYPE_DDR_1_8V
                Console.Write(", DDR52_1.8V");
            if ((card_type & (1 << 4)) != 0) // EXT_CSD_CARD_TYPE_HS200_1_8V
                Console.Write(", HS200_1.8V");
            if ((card_type & (1 << 6)) != 0) // EXT_CSD_CARD_TYPE_HS400_1_8V
                Console.Write(", HS400_1.8V");
            Console.WriteLine("");
        }

        // Selects the pages of a start/end block range, 0/0 selects all total pages
        private static void PageRange(uint start, uint end, uint total, out uint first, out uint count)
        {
            first = 0;
            count = total;
            if (start == 0 && end == 0)
                return;

            first = Math.Min(start * PagesPerBlock, total);
            count = Math.Max(Math.Min(end * PagesPerBlock, total), first) - first;
        }

        private static void ShowProgress(uint lba, uint first, uint count)
        {
            MainForm.mainForm.PicoFlasherBlocksUpdate((lba / PagesPerBlock).ToString("X"), (int)((lba - first) * 100L / count));
        }

        // eMMC blocks have no spare area unlike NAND pages
        private static int PageSize(bool emmc)
        {
            return emmc ? 0x200 : SfcxConfig.RawBytesPerPage;
        }

        // Reads count NAND pages or eMMC blocks starting at first into output,
        // a read from 0 uses a read stream
        private void ReadPages(bool emmc, uint first, uint count, Stream output, bool progress)
        {
            bool stream = first == 0;
            int pageSize = PageSize(emmc);
            byte[] page = new byte[pageSize];

            if (stream)
                Send(emmc ? COMMANDS.EMMC_READ_STREAM : COMMANDS.READ_FLASH_STREAM, count);

            for (uint lba = first; lba < first + count; lba++)
            {
                if (variables.escapeloop)
                {
                    // A cancelled stream keeps sending until the firmware times it out
                    if (stream)
                        Drain();
                    throw new OperationCanceledException();
                }

                if (!stream)
                    Send(emmc ? COMMANDS.EMMC_READ : COMMANDS.READ_FLASH, lba);

                // A failure ends a stream with only its status
                UInt32 ret = RecvUInt32();
                if (ret != 0)
                    throw new StatusException("read at page 0x" + lba.ToString("X"), ret);

                Recv(page, 0, pageSize);
                output.Write(page, 0, pageSize);

                if (progress && (lba - first) % PagesPerBlock == 0)
                    ShowProgress(lba, first, count);
            }
        }

        private byte[] ReadPages(bool emmc, uint count)
        {
            using (MemoryStream output = new MemoryStream())
            {
                ReadPages(emmc, 0, count, output, false);
                return output.ToArray();
            }
        }

        // Reads count pages of pageSize bytes from input
        private static byte[] ReadInput(BinaryReader input, uint count, int pageSize)
        {
            byte[] data = input.ReadBytes((int)count * pageSize);
            if (data.Length != count * pageSize)
                throw new EndOfStreamException();
            return data;
        }

        // Writes count NAND pages or eMMC blocks from input starting at first, one per command
        private void WritePagesSingle(bool emmc, uint first, uint count, BinaryReader input)
        {
            int pageSize = PageSize(emmc);

            for (uint lba = first; lba < first + count; lba++)
            {
                if (variables.escapeloop)
                    throw new OperationCanceledException();

                byte[] page = ReadInput(input, 1, pageSize);
                Send(emmc ? COMMANDS.EMMC_WRITE : COMMANDS.WRITE_FLASH, lba, page, 0, pageSize);

                UInt32 ret = RecvUInt32();
                if (ret != 0)
                    throw new StatusException("write at page 0x" + lba.ToString("X"), ret);

                if ((lba - first) % PagesPerBlock == 0)
                    ShowProgress(lba, first, count);
            }
        }

        // Streams count NAND pages or eMMC blocks from input starting at 0,
        // the status arrives after the last one or on the first failure
        private void WritePagesStream(bool emmc, uint count, BinaryReader input)
        {
            int pageSize = PageSize(emmc);
            bool cancelled = false;
            UInt32 ret = 0;

            Send(emmc ? COMMANDS.EMMC_WRITE_STREAM : COMMANDS.WRITE_FLASH_STREAM, count);

            for (uint lba = 0; lba < count; )
            {
                if (variables.escapeloop)
                {
                    cancelled = true;
                    break;
                }

                // Stop sending as soon as the device reports a failure
                if (serial.BytesToRead > 0)
                {
                    ret = RecvUInt32();
                    if (ret == 0)
                        throw new IOException("PicoFlasher: unexpected write stream status at page 0x" + lba.ToString("X"));
                    break;
                }

                // Batched to keep the number of serial writes down
                uint pages = Math.Min(count - lba, StreamWritePages);
                byte[] data = ReadInput(input, pages, pageSize);
                serial.Write(data, 0, data.Length);

                ShowProgress(lba, 0, count);
                lba += pages;
            }

            // The 1 byte trailer is a short packet, which completes the
            // device's CDC transfer holding the last page
            serial.Write(new byte[] { 0 }, 0, 1);

            if (!cancelled && ret == 0)
                ret = RecvUInt32();

            if (cancelled || ret != 0)
            {
                // The firmware drops a cancelled stream, or discards the pages
                // still in flight after a failure, once the line is idle
                Drain();

                if (cancelled)
                    throw new OperationCanceledException();
                throw new StatusException("write stream", ret);
            }
        }

        // Writes count NAND pages or eMMC blocks from input starting at first,
        // a write from 0 on v5 firmware uses a write stream
        private void WritePages(bool emmc, uint first, uint count, BinaryReader input)
        {
            if (Version >= 5 && first == 0)
                WritePagesStream(emmc, count, input);
            else
                WritePagesSingle(emmc, first, count, input);
        }

        private static string ElapsedTime(Stopwatch watch)
        {
            return string.Format("{0}:{1:D2}", (int)watch.Elapsed.TotalMinutes, watch.Elapsed.Seconds);
        }

        private static void PlaySuccess()
        {
            if (variables.playSuccess)
            {
                SoundPlayer success = new SoundPlayer(Properties.Resources.chime);
                success.Play();
            }
        }

        public void printFlashConfig()
        {
            RunOperation("Console check", () =>
            {
                variables.conf = null;

                Target target = DetectConsole();
                if (target == null)
                {
                    Console.WriteLine("");
                    return;
                }

                byte[] conf;
                Console.WriteLine("Checking CB...");
                if (target.IsEmmc)
                {
                    InitEmmc();

                    // Flash config alone cannot distinguish Corona from
                    // Winchester eMMC. Read the same first 64KB used by
                    // the SPI console check and let CB + SMC select the
                    // exact motherboard (including Winchester CB 16128).
                    conf = ReadPages(true, 0x80);
                }
                else
                {
                    conf = ReadPages(false, 4 * PagesPerBlock);
                }

                variables.flashconfig = target.FlashConfig.ToString("X8");
                variables.conf = conf;
                MainForm.mainForm.getcb_v(variables.flashconfig);
                Console.WriteLine("");
            });
        }

        public void Read(int iterations, uint start = 0, uint end = 0)
        {
            Console.WriteLine("PicoFlasher: Beginning Read");

            RunOperation("Read", () =>
            {
                Target target = DetectConsole();
                if (target == null)
                {
                    Console.WriteLine("");
                    return;
                }

                uint total;
                if (target.IsEmmc)
                {
                    InitEmmc();
                    total = EmmcSystemBytes / 0x200;
                }
                else
                {
                    total = (uint)(target.Config.TotalDataBytes / SfcxConfig.DataBytesPerPage);

                    if (target.Config.HasMemoryUnit)
                    {
                        DialogResult bbdr = MessageBox.Show("A big block nand has been detected\n\nDo you want to dump only the system partition? (recommended)", "Nand Dump Size", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                        if (bbdr == DialogResult.Cancel)
                            return;
                        else if (bbdr == DialogResult.Yes)
                            total = (uint)(target.Config.SystemDataBytes / SfcxConfig.DataBytesPerPage);
                    }
                }

                uint first, count;
                PageRange(start, end, total, out first, out count);

                for (int i = 0; i < iterations; i++)
                {
                    variables.filename = variables.outfolder + "\\nanddump" + (i + 1) + ".bin";
                    if (File.Exists(variables.filename))
                    {
                        if (DialogResult.Cancel == MessageBox.Show("A nand dump already exists!\n\nContinuing will cause the contents to be overwritten!", "File Conflict", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                        {
                            Console.WriteLine("Cancelled");
                            Console.WriteLine("");
                            return;
                        }
                    }

                    Console.WriteLine("");
                    Console.WriteLine("Reading Nand to {0}", variables.filename);

                    Stopwatch watch = Stopwatch.StartNew();
                    variables.reading = true;
                    MainForm.mainForm.PicoFlasherBusy(1);
                    try
                    {
                        using (FileStream output = File.Open(variables.filename, FileMode.Create, FileAccess.Write))
                            ReadPages(target.IsEmmc, first, count, output, true);
                    }
                    finally
                    {
                        variables.reading = false;
                        MainForm.mainForm.PicoFlasherBusy(0);
                    }

                    Console.WriteLine("Read Successful! Time Elapsed: {0}", ElapsedTime(watch));
                    Console.WriteLine("");
                    PlaySuccess();

                    Thread.Sleep(1000);
                    MainForm.mainForm.PicoFlasherInitNand(i);

                    if (i + 1 < iterations)
                        Thread.Sleep(1000);
                }
            });
        }

        public void Write(int fixEcc, uint start = 0, uint end = 0, bool isEccOrXell = false)
        {
            string filename = variables.filename1;
            if (string.IsNullOrWhiteSpace(filename)) return;
            if (!File.Exists(filename)) return;

            Console.WriteLine("PicoFlasher: Beginning Write");

            RunOperation("Write", () =>
            {
                Target target = DetectConsole();
                if (target == null)
                {
                    Console.WriteLine("");
                    return;
                }

                uint total;
                if (target.IsEmmc)
                {
                    InitEmmc();
                    total = EmmcSystemBytes / 0x200;
                }
                else
                {
                    total = (uint)(target.Config.TotalDataBytes / SfcxConfig.DataBytesPerPage);
                }

                uint first, count;
                PageRange(start, end, total, out first, out count);

                // The image is written from its start, the ECC is regenerated
                // for the pages it lands on before anything is written
                Stream image;
                if (fixEcc == 1 && !target.IsEmmc)
                {
                    SfcxConfig config = target.Config;
                    image = new MemoryStream(JRunner.Nand.Nand.addecc_v2(File.ReadAllBytes(filename), false,
                        (int)(first * SfcxConfig.RawBytesPerPage), config.Layout, (int)config.PagesPerBlock));
                }
                else
                {
                    image = File.Open(filename, FileMode.Open, FileAccess.Read);
                }

                using (BinaryReader input = new BinaryReader(image))
                {
                    // Up to the image's last whole page
                    count = (uint)Math.Min(count, image.Length / PageSize(target.IsEmmc));

                    Console.WriteLine("");
                    Console.WriteLine("Writing {0} to Nand", Path.GetFileName(filename));

                    Stopwatch watch = Stopwatch.StartNew();
                    variables.writing = true;
                    MainForm.mainForm.PicoFlasherBusy(2);
                    try
                    {
                        WritePages(target.IsEmmc, first, count, input);
                    }
                    finally
                    {
                        variables.writing = false;
                        MainForm.mainForm.PicoFlasherBusy(0);
                    }

                    Console.WriteLine("Write Successful! Time Elapsed: {0}", ElapsedTime(watch));
                }

                Console.WriteLine("");
                PlaySuccess();
            },
            () =>
            {
                if (isEccOrXell)
                {
                    Thread.Sleep(500);
                    MainForm.mainForm.afterWriteXeLLCleanup();
                }
            });
        }

        // Erases length J-Runner blocks from start, length 0 erases size MB
        public void Erase(int size, uint start, uint length)
        {
            Console.WriteLine("PicoFlasher: Beginning Erase");

            RunOperation("Erase", () =>
            {
                if (Version < 4)
                {
                    Console.WriteLine("PicoFlasher: v" + Version + " firmware doesn't support erasing");
                    Console.WriteLine("");
                    return;
                }

                Target target = DetectConsole();
                if (target == null)
                {
                    Console.WriteLine("");
                    return;
                }

                if (target.IsEmmc)
                {
                    Console.WriteLine("PicoFlasher: eMMC can't be erased");
                    Console.WriteLine("");
                    return;
                }

                if (length == 0)
                    length = (uint)size * 1024 * 1024 / (PagesPerBlock * SfcxConfig.DataBytesPerPage);

                uint total = (uint)(target.Config.TotalDataBytes / SfcxConfig.DataBytesPerPage);
                uint first, count;
                PageRange(start, start + length, total, out first, out count);

                // SFCX erases whole physical blocks, each one the range touches is erased
                uint pagesPerEraseBlock = target.Config.PagesPerBlock;

                Console.WriteLine("");
                Console.WriteLine("Erasing Nand");

                Stopwatch watch = Stopwatch.StartNew();
                MainForm.mainForm.PicoFlasherBusy(3);
                try
                {
                    for (uint lba = first / pagesPerEraseBlock * pagesPerEraseBlock; lba < first + count; lba += pagesPerEraseBlock)
                    {
                        if (variables.escapeloop)
                            throw new OperationCanceledException();

                        Send(COMMANDS.ERASE_FLASH, lba);

                        UInt32 ret = RecvUInt32();
                        if (ret != 0)
                            throw new StatusException("erase at page 0x" + lba.ToString("X"), ret);

                        ShowProgress(Math.Max(lba, first), first, count);
                    }
                }
                finally
                {
                    MainForm.mainForm.PicoFlasherBusy(0);
                }

                Console.WriteLine("Erase Successful! Time Elapsed: {0}", ElapsedTime(watch));
                Console.WriteLine("");
                PlaySuccess();
            });
        }

        public int Open()
        {
            return 1;
        }

        public int Close()
        {
            return 1;
        }

        // The ISD1200 commands were removed from the firmware before protocol v5,
        // which would take the data of an ISD write as commands
        private void IsdSend(COMMANDS cmd, UInt32 arg = 0, byte[] data = null, int offset = 0, int count = 0)
        {
            if (Version >= 5)
                throw new NotSupportedException("PicoFlasher v" + Version + " firmware does not support ISD1200 flashing, use a software flasher");

            Send(cmd, arg, data, offset, count);
        }

        // Sends one ISD1200 command in its own session, false if the port could not be opened
        private bool IsdCommand(COMMANDS cmd, UInt32 arg, out byte reply)
        {
            reply = 0;
            if (!OpenPort())
                return false;

            try
            {
                IsdSend(cmd, arg);
                reply = RecvUInt8();
                return true;
            }
            finally
            {
                ClosePort();
            }
        }

        // Reads the whole ISD1200 flash, null if the port could not be opened
        private byte[] IsdReadFlash()
        {
            if (!OpenPort())
                return null;

            try
            {
                byte[] data = new byte[0xB000];

                for (uint i = 0; i < 0xB000 / 512; i++)
                {
                    IsdSend(COMMANDS.ISD1200_READ_FLASH, i);
                    Recv(data, (int)i * 512, 512);

                    UpdateProgress?.Invoke((int)((i * 100) / (0xB000 / 512)));
                }
                UpdateProgress?.Invoke(100);

                return data;
            }
            finally
            {
                ClosePort();
            }
        }

        public int PowerUp()
        {
            byte ret;
            return IsdCommand(COMMANDS.ISD1200_INIT, 0, out ret) && ret == 0 ? 1 : 0;
        }

        public int GetID()
        {
            byte id;
            if (!IsdCommand(COMMANDS.ISD1200_READ_ID, 0, out id))
                return 0;

            switch (id)
            {
                case 0x01:
                    return 1;
                case 0x10:
                    return 2;
                case 0x11:
                    return 3;
                default:
                    return 0;
            }
        }

        public void PowerDown()
        {
            byte ret;
            IsdCommand(COMMANDS.ISD1200_DEINIT, 0, out ret);
        }

        public void Reset()
        {
            byte ret;
            IsdCommand(COMMANDS.ISD1200_RESET, 0, out ret);
        }

        public int ISD_Read_Flash(string filename)
        {
            if (File.Exists(filename))
            {
                if (DialogResult.Cancel == MessageBox.Show("A nand dump already exists!\n\nContinuing will cause the contents to be overwritten!", "File Conflict", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                {
                    Console.WriteLine("Cancelled");
                    Console.WriteLine("");
                    return 0;
                }
            }

            byte[] data = IsdReadFlash();
            if (data == null)
                return 0;

            File.WriteAllBytes(filename, data);
            return 1;
        }

        public int ISD_Erase_Flash()
        {
            byte ret;
            return IsdCommand(COMMANDS.ISD1200_ERASE_FLASH, 0, out ret) && ret == 0 ? 1 : 0;
        }

        public void ISD_Write_Flash(string filename)
        {
            byte[] file = File.ReadAllBytes(filename);

            if (!OpenPort())
                return;

            try
            {
                for (uint i = 0; i < Math.Min(file.Length, 0xB000) / 16; i++)
                {
                    IsdSend(COMMANDS.ISD1200_WRITE_FLASH, i, file, (int)i * 16, 16);

                    UInt32 ret = RecvUInt32();
                    if (ret != 0)
                    {
                        Console.WriteLine("Error: " + ret.ToString("X"));
                        Console.WriteLine("");
                        break;
                    }

                    UpdateProgress?.Invoke((int)((i * 100) / (0xB000 / 16)));
                }
                UpdateProgress?.Invoke(100);
            }
            finally
            {
                ClosePort();
            }
        }

        public Boolean ISD_Verify_Flash(string filename)
        {
            if (!File.Exists(filename))
            {
                log("Image file not found\n");
                return false;
            }
            FileInfo fl = new FileInfo(filename);
            if (fl.Length != 0xB000)
            {
                log("Image file must be 44Kb\n");
                return false;
            }
            byte[] file = File.ReadAllBytes(filename);

            byte[] data = IsdReadFlash();
            if (data == null)
                return false;

            return Oper.ByteArrayCompare(file, data);
        }

        public void ISD_Play(ushort index)
        {
            byte ret;
            IsdCommand(COMMANDS.ISD1200_PLAY_VOICE, index, out ret);
        }

        public void ISD_Exec(ushort index)
        {
            byte ret;
            IsdCommand(COMMANDS.ISD1200_EXEC_MACRO, index, out ret);
        }
    }
}
