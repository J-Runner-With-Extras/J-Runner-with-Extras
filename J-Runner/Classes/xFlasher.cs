using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace JRunner
{
    public class xFlasher
    {
        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int spi(int mode, int size, string file, int startblock = 0, int length = 0);

        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int spiGetBlocks();

        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int spiGetConfig();

        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void spiStop();


        //xFlasher eMMC over SPI
        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int emmc_read(string file, int startBlock, int blockNum = 98304);

        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int emmc_write(string file, int startBlock);

        [DllImport(@"common\xflasher\xFlasher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int emmcGetBlocks();


        public bool ready = false;
        public bool inUse = false;
        public bool waiting = false;
        private string flashconf = "";
        public int selType = 0;

        private static int initCount = 0;
        public static int inUseCount = 0;
        public static string xFlasherTimeString = "";
        System.Windows.Threading.DispatcherTimer initTimer;
        public System.Timers.Timer inUseTimer;

        // Libraries
        public void initTimerSetup()
        {
            initTimer = new System.Windows.Threading.DispatcherTimer();
            initTimer.Interval = new TimeSpan(0, 0, 0, 0, 300);
            initTimer.Tick += initTimerUpd;
        }

        public void initDevice()
        {
            initTimer.Stop();
            initCount = 0;
            ready = false;
            initTimer.Start();
        }

        private void initTimerUpd(object source, EventArgs e)
        {
            if (initCount < 16)
            {
                initCount++;
            }
            else
            {
                initTimer.Stop();
                if (!inUse && MainForm.mainForm.getProgressBarStyle() == ProgressBarStyle.Marquee) MainForm.mainForm.xFlasherBusy(-1);
                waiting = false;
                ready = true; // Last
            }
        }

        public void inUseTimerSetup()
        {
            inUseTimer = new System.Timers.Timer(1000);
            inUseTimer.Elapsed += inUseTimerUpd;
        }

        private void inUseTimerUpd(object source, EventArgs e)
        {
            inUseCount++;

            if (inUseCount > 59) xFlasherTimeString = TimeSpan.FromSeconds(inUseCount).ToString(@"m\:ss") + " min(s)";
            else if (inUseCount >= 0) xFlasherTimeString = inUseCount + " sec(s)";
        }

        public void abort()
        {
            spiStop();
        }

        // Flash Config
        public void getFlashConfig()
        {
            if (inUse || waiting) return;

            Thread ftdiThread = new Thread(() =>
            {
                try
                {
                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    getFlashConfigActual();
                }
                catch (Exception ex)
                {
                    inUse = false;

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            ftdiThread.Start();
        }

        public int getFlashConfigActual(bool auto = false)
        {
            Console.WriteLine("xFlasher: Checking Console...");
            inUse = true;

            int result = spi(0, 16, @"common\xflasher\nand.bin");

            flashconf = spiGetConfig().ToString("X8");

            inUse = false;

            if (result == 0 || result == -3 || result == -4)
            {
                Console.WriteLine("Flash Config: 0x{0}", flashconf);

                if (flashconf == "00000000" || flashconf == "FFFFFFFF")
                {
                    Console.WriteLine("xFlasher: Console Not Found");
                    Console.WriteLine("");
                    return 1;
                }

                if (flashconf == "C0462002")
                {
                    Console.WriteLine("Corona: 4GB");


                }
                else if (result == -4)
                {
                    Console.WriteLine("xFlasher: Unknown Nand");

                    return 1;
                }
                else if (flashconf == "00023010") Console.WriteLine("Jasper, Trinity: 16MB");
                else if (flashconf == "00043000") Console.WriteLine("Corona: 16MB");
                else if (flashconf == "008A3020") Console.WriteLine("Jasper, Trinity: 256MB");
                else if (flashconf == "00AA3020") Console.WriteLine("Jasper, Trinity: 512MB");
                else if (flashconf == "008C3020") Console.WriteLine("Corona: 256MB");
                else if (flashconf == "00AC3020") Console.WriteLine("Corona: 512MB");
                else if (flashconf == "01198010") Console.WriteLine("Xenon, Zephyr, Falcon: 16MB");
                else if (flashconf == "01198030") Console.WriteLine("Xenon, Zephyr, Falcon: 64MB");
                else Console.WriteLine("Unrecongized Flash Config");

                Console.WriteLine("");
                return 0;
            }
            else if (result == -2)
            {
                Console.WriteLine("xFlasher: Device Not Initialized");
                Console.WriteLine("");
                return 1;
            }
            else
            {
                Console.WriteLine("xFlasher: Unknown Error");
                Console.WriteLine("");
                return 1;
            }
        }

        public void getConsoleCb()
        {
            if (inUse || waiting) return;

            Thread ftdiThread = new Thread(() =>
            {
                try
                {
                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    if (getFlashConfigActual(true) != 0)
                    {
                        return;
                    }

                    if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");

                    MainForm.mainForm.xFlasherBusy(-2);
                    Console.WriteLine("xFlasher: Checking CB...");
                    inUse = true;

                    int result = spi(1, 16, @"common\xflasher\nand.bin", 0, 4); // Read Conf

                    inUse = false;
                    MainForm.mainForm.xFlasherBusy(-1);

                    if (result == 0)
                    {
                        if (File.Exists("common/xflasher/nand.bin"))
                        {
                            variables.conf = File.ReadAllBytes("common/xflasher/nand.bin");
                            MainForm.mainForm.getcb_v(flashconf);
                            File.Delete("common/xflasher/nand.bin");
                        }

                        Console.WriteLine("");
                    }
                    else if (result == -2)
                    {
                        if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");
                        Console.WriteLine("xFlasher: Device Not Initialized");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -3)
                    {
                        if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");
                        Console.WriteLine("xFlasher: Console Not Found");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -4)
                    {
                        if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");
                        Console.WriteLine("xFlasher: Unknown Nand");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -11)
                    {
                        if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");
                        Console.WriteLine("xFlasher: Couldn't Open File");
                        Console.WriteLine("");
                        return;
                    }
                    else
                    {
                        if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");
                        Console.WriteLine("xFlasher: Unknown Error");
                        Console.WriteLine("");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    inUse = false;
                    MainForm.mainForm.xFlasherBusy(-1);
                    if (File.Exists("common/xflasher/nand.bin")) File.Delete("common/xflasher/nand.bin");

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            ftdiThread.Start();
        }

        // Read Nand
        public void readNandAuto(int size, int iterations, bool skipboardcheck = false) // Automated read, do not use for any special/custom read
        {
            if (inUse || waiting) return;

            Thread ftdiThread = new Thread(() =>
            {
                try
                {
                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    if (!skipboardcheck)
                    {
                        if (getFlashConfigActual(true) != 0)
                        {
                            return;
                        }

                        if (size == 0) // Auto Detect Dump Size From Board
                        {
                            if (flashconf == "00023010" || flashconf == "00043000" || flashconf == "01198010")
                            {
                                size = 16;
                            }
                            else if (flashconf == "01198030")
                            {
                                size = 64;
                            }
                            else if (flashconf == "C0462002")
                            {

                            }
                            else if (flashconf == "008A3020" || flashconf == "008C3020")
                            {
                                MainForm.mainForm.BeginInvoke((Action)(() => MainForm.mainForm.xFlasherNandSelShow(1, 2))); // Ask BB
                                return;
                            }
                            else if (flashconf == "00AA3020" || flashconf == "00AC3020")
                            {
                                MainForm.mainForm.BeginInvoke((Action)(() => MainForm.mainForm.xFlasherNandSelShow(1, 3))); // Ask BB
                                return;
                            }
                            else
                            {
                                MainForm.mainForm.BeginInvoke((Action)(() => MainForm.mainForm.xFlasherNandSelShow(1))); // Ask
                                return;
                            }
                        }
                    }

                    int i = 1;
                    for (i = 1; i <= iterations;)
                    {
                        variables.iterations = i;
                        if (i == 1)
                        {
                            if (iterations == 1)
                            {
                                Console.WriteLine("xFlasher: {0} Nand Read Queued", iterations);
                            }
                            else
                            {
                                Console.WriteLine("xFlasher: {0} Nand Reads Queued", iterations);
                            }
                        }
                        else
                        {
                            Thread.Sleep(1000);
                            MainForm.mainForm.xFlasherInitNand(i);
                            Thread.Sleep(1000);
                        }

                        variables.filename = variables.outfolder + "\\nanddump" + i + ".bin";
                        if (File.Exists(variables.filename))
                        {
                            if (DialogResult.Cancel == MessageBox.Show("A nand dump already exists!\n\nContinuing will cause the contents to be overwritten!", "File Conflict", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                            {
                                Console.WriteLine("xFlasher: Cancelled");
                                Console.WriteLine("");
                                return;
                            };
                        }

                        variables.reading = true;
                        MainForm.mainForm.xFlasherBusy(1);
                        Console.WriteLine("xFlasher: Reading Nand to {0}", variables.filename);

                        int result = -1;
                        if (flashconf != "C0462002")
                        {
                            Thread blocksThread = new Thread(() =>
                            {
                                getBlocks(0, size * 64);
                            });

                            inUse = true;
                            blocksThread.Start();

                            result = spi(1, size, variables.filename);
                        }
                        else
                        {
                            Thread blocksThread = new Thread(() =>
                            {
                                geteMMCBlocks(0, 98304);
                            });

                            inUse = true;
                            blocksThread.Start();

                            result = emmc_read(variables.filename, 0, 98304);

                        }

                        inUseTimer.Enabled = false;
                        inUseCount = 0;
                        inUse = false;
                        variables.reading = false;
                        MainForm.mainForm.xFlasherBusy(0);

                        if (result == -1)
                        {
                            i = iterations + 1;
                            Console.WriteLine("xFlasher: Cancelled");
                            Console.WriteLine("");
                            return;
                        }
                        else if (result == 0)
                        {
                            Console.WriteLine("xFlasher: Read Successful! Time Elapsed: {0}", xFlasherTimeString);
                            Console.WriteLine("");

                            if (variables.playSuccess)
                            {
                                SoundPlayer success = new SoundPlayer(Properties.Resources.chime);
                                success.Play();
                            }
                        }
                        else if (result == -2)
                        {
                            Console.WriteLine("xFlasher: Device Not Initialized");
                            Console.WriteLine("");
                            return;
                        }
                        else if (result == -3)
                        {
                            Console.WriteLine("xFlasher: Console Not Found");
                            Console.WriteLine("");
                            return;
                        }
                        else if (result == -4)
                        {
                            Console.WriteLine("xFlasher: Unknown Nand");
                            Console.WriteLine("");
                            return;
                        }
                        else if (result == -11)
                        {
                            Console.WriteLine("xFlasher: Couldn't Open File");
                            Console.WriteLine("");
                            return;
                        }
                        else
                        {
                            Console.WriteLine("xFlasher: Unknown Error");
                            Console.WriteLine("");
                            return;
                        }



                        i++;
                    }
                    if (i > iterations)
                    {
                        Thread.Sleep(1000);
                        MainForm.mainForm.xFlasherInitNand(i);
                    }
                }
                catch (Exception ex)
                {
                    inUseTimer.Enabled = false;
                    inUseCount = 0;
                    inUse = false;
                    variables.reading = false;
                    MainForm.mainForm.xFlasherBusy(0);

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            ftdiThread.Start();
        }

        public void readNand(int size, string filename, int startblock = 0, int length = 0)
        {
            if (inUse || waiting) return;

            if (string.IsNullOrWhiteSpace(filename)) return;

            Thread ftdiThread = new Thread(() =>
            {
                try
                {

                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    if (getFlashConfigActual(true) != 0)
                    {
                        return;
                    }

                    if (File.Exists(filename))
                    {
                        if (DialogResult.Cancel == MessageBox.Show("A nand dump already exists!\n\nContinuing will cause the contents to be overwritten!", "File Conflict", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                        {
                            Console.WriteLine("xFlasher: Cancelled");
                            Console.WriteLine("");
                            return;
                        };
                    }

                    variables.reading = true;
                    MainForm.mainForm.xFlasherBusy(1);
                    Console.WriteLine("xFlasher: Reading Nand to {0}", filename);

                    Thread blocksThread = new Thread(() =>
                    {
                        int len = size * 64;
                        if (length > 0) len = length;
                        getBlocks(startblock, len);
                    });

                    inUse = true;
                    blocksThread.Start();

                    int result = spi(1, size, filename, startblock, length);

                    inUseTimer.Enabled = false;
                    inUseCount = 0;
                    inUse = false;
                    variables.reading = false;
                    MainForm.mainForm.xFlasherBusy(0);

                    if (result == -1)
                    {
                        Console.WriteLine("xFlasher: Cancelled");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == 0)
                    {
                        Console.WriteLine("xFlasher: Read Successful! Time Elapsed: {0}", xFlasherTimeString);
                        Console.WriteLine("");

                        if (variables.playSuccess)
                        {
                            SoundPlayer success = new SoundPlayer(Properties.Resources.chime);
                            success.Play();
                        }
                    }
                    else if (result == -2)
                    {
                        Console.WriteLine("xFlasher: Device Not Initialized");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -3)
                    {
                        Console.WriteLine("xFlasher: Console Not Found");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -4)
                    {
                        Console.WriteLine("xFlasher: Unknown Nand");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -11)
                    {
                        Console.WriteLine("xFlasher: Couldn't Open File");
                        Console.WriteLine("");
                        return;
                    }
                    else
                    {
                        Console.WriteLine("xFlasher: Unknown Error");
                        Console.WriteLine("");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    inUseTimer.Enabled = false;
                    inUseCount = 0;
                    inUse = false;
                    variables.reading = false;
                    MainForm.mainForm.xFlasherBusy(0);

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            ftdiThread.Start();
        }

        // Write Nand
        public void writeXeLLAuto()
        {
            if (string.IsNullOrWhiteSpace(variables.filename1)) return;
            if (!File.Exists(variables.filename1)) return;


            if (Path.GetExtension(variables.filename1) == ".ecc") writeNand(16, variables.filename1, 1);
            else writeNand(16, variables.filename1, 2);
        }

        public void writeNandAuto()
        {
            if (string.IsNullOrWhiteSpace(variables.filename1)) return;
            if (!File.Exists(variables.filename1)) return;

            long len = new FileInfo(variables.filename1).Length;
            if (len == 0x3000000)
            {
                variables.nandsizex = Nandsize.S64;
                writeNand(64, variables.filename1);
            }
            else if (len == 553648128)
            {
                variables.nandsizex = Nandsize.S512;
                writeNand(512, variables.filename1);
            }
            else if (len == 276824064)
            {
                variables.nandsizex = Nandsize.S256;
                writeNand(256, variables.filename1);
            }
            else if (len == 69206016)
            {
                variables.nandsizex = Nandsize.S64;
                writeNand(64, variables.filename1);
            }
            else if (len == 17301504)
            {
                variables.nandsizex = Nandsize.S16;
                writeNand(16, variables.filename1);
            }
            else if (len == 1310720 | len == 1351680)
            {
                variables.nandsizex = Nandsize.S16;
                writeNand(16, variables.filename1, 2);
            }
            else
            {
                MessageBox.Show("Nand is not a valid size", "Can't", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void writeNand(int size, string filename, int mode = 0, int startblock = 0, int length = 0, bool skipboardcheck = false)
        {
            if (inUse || waiting) return;

            if (filename != "erase")
            {
                if (string.IsNullOrWhiteSpace(filename)) return;
                if (!File.Exists(filename)) return;
            }

            Thread ftdiThread = new Thread(() =>
            {
                try
                {

                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    if (getFlashConfigActual(true) != 0)
                    {
                        return;
                    }
                    if (flashconf == "C0462002")
                    {
                        if (mode == 0)
                        {
                            mode = 3;
                        }
                        else
                        {
                            mode = 4;
                        }
                    }
                    if (mode == 0 && filename != "erase" && !skipboardcheck)
                    {
                        if (flashconf == "00023010" || flashconf == "00043000" || flashconf == "01198010")
                        {
                            if (size != 16)
                            {
                                if (DialogResult.No == MessageBox.Show("You are attempting to write a " + size + "MB nand to a board with a 16MB flash config.\n\nAre you sure that you want to do that?", "Steep Hill Ahead", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                                {
                                    Console.WriteLine("xFlasher: Cancelled");
                                    Console.WriteLine("");
                                    return;
                                }
                            }
                        }
                        else if (flashconf == "01198030")
                        {
                            if (size != 64)
                            {
                                if (DialogResult.No == MessageBox.Show("You are attempting to write a " + size + "MB nand to a board with a 64MB flash config.\n\nAre you sure that you want to do that?", "Steep Hill Ahead", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                                {
                                    Console.WriteLine("xFlasher: Cancelled");
                                    Console.WriteLine("");
                                    return;
                                }
                            }
                        }
                        else if (flashconf == "008A3020" || flashconf == "008C3020")
                        {
                            if (size == 16 || size == 512)
                            {
                                if (DialogResult.No == MessageBox.Show("You are attempting to write a " + size + "MB nand to a board with a 64/256MB flash config.\n\nAre you sure that you want to do that?", "Steep Hill Ahead", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                                {
                                    Console.WriteLine("xFlasher: Cancelled");
                                    Console.WriteLine("");
                                    return;
                                }
                            }
                        }
                        else if (flashconf == "00AA3020" || flashconf == "00AC3020")
                        {
                            if (size == 16 || size == 256)
                            {
                                if (DialogResult.No == MessageBox.Show("You are attempting to write a " + size + "MB nand to a board with a 64/512MB flash config.\n\nAre you sure that you want to do that?", "Steep Hill Ahead", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                                {
                                    Console.WriteLine("xFlasher: Cancelled");
                                    Console.WriteLine("");
                                    return;
                                }
                            }
                        }
                        else
                        {
                            if (DialogResult.No == MessageBox.Show("I think the correct size is " + size + "MB.\n\nDo you want to continue as " + size + "MB?", "Unrecognized Flash Config", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                            {
                                MainForm.mainForm.BeginInvoke((Action)(() => MainForm.mainForm.xFlasherNandSelShow(2))); // Ask
                                return;
                            }
                        }
                    }

                    variables.writing = true;

                    if (filename == "erase")
                    {
                        MainForm.mainForm.xFlasherBusy(3);
                        Console.WriteLine("xFlasher: Erasing Nand");
                    }
                    else
                    {
                        MainForm.mainForm.xFlasherBusy(2);
                        Console.WriteLine("xFlasher: Writing {0} to Nand", Path.GetFileName(filename));
                    }
                    if (flashconf != "C0462002")
                    {
                        Thread blocksThread = new Thread(() =>
                        {
                            if (mode == 1 || mode == 2) getBlocks(0, 80);
                            else
                            {
                                int len = size * 64;
                                if (length > 0) len = length;
                                getBlocks(startblock, len);
                            }
                        });
                        inUse = true;
                        blocksThread.Start();
                    }
                    else
                    {
                        Thread blocksThread = new Thread(() =>
                        {
                            if (mode == 4)
                            {
                                geteMMCBlocks(0, 2560);
                            }
                            else
                            {
                                geteMMCBlocks(0, 98304);
                            }

                        });
                        inUse = true;
                        blocksThread.Start();
                    }

                    int result;
                    if (filename == "erase")
                    {
                        result = spi(5, size, "erase", startblock, length);
                    }
                    else if ((mode == 3) || (mode == 4))
                    {
                        result = emmc_write(filename, 0);
                    }
                    else if (mode == 1)
                    {
                        result = spi(4, size, filename, startblock, length);
                    }
                    else
                    {
                        result = spi(3, size, filename, startblock, length);
                    }

                    inUseTimer.Enabled = false;
                    inUseCount = 0;
                    inUse = false;
                    variables.writing = false;
                    MainForm.mainForm.xFlasherBusy(0);

                    if (result == -1)
                    {
                        Console.WriteLine("xFlasher: Cancelled");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == 0)
                    {
                        if (filename == "erase") Console.WriteLine("xFlasher: Erase Successful! Time Elapsed: {0}", xFlasherTimeString);
                        else Console.WriteLine("xFlasher: Write Successful! Time Elapsed: {0}", xFlasherTimeString);
                        Console.WriteLine("");

                        if (variables.playSuccess)
                        {
                            SoundPlayer success = new SoundPlayer(Properties.Resources.chime);
                            success.Play();
                        }

                        if (mode == 1)
                        {
                            Thread.Sleep(500);
                            MainForm.mainForm.afterWriteXeLLCleanup();
                        }
                    }
                    else if (result == -2)
                    {
                        Console.WriteLine("xFlasher: Device Not Initialized");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -3)
                    {
                        Console.WriteLine("xFlasher: Console Not Found");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -4)
                    {
                        Console.WriteLine("xFlasher: Unknown Nand");
                        Console.WriteLine("");
                        return;
                    }
                    else if (result == -11)
                    {
                        Console.WriteLine("xFlasher: Couldn't Open File");
                        Console.WriteLine("");
                        return;
                    }
                    else
                    {
                        Console.WriteLine("xFlasher: Unknown Error");
                        Console.WriteLine("");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    inUseTimer.Enabled = false;
                    inUseCount = 0;
                    inUse = false;
                    variables.writing = false;
                    MainForm.mainForm.xFlasherBusy(0);

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            ftdiThread.Start();
        }

        private void getBlocks(int start, int length)
        {
            int blocks;
            while (inUse)
            {
                blocks = spiGetBlocks();
                if (blocks >= 0)
                {
                    if (!inUseTimer.Enabled)
                    {
                        xFlasherTimeString = "< 1 sec(s)"; // If it doesn't update at least once, time was less than 1 second
                        inUseTimer.Enabled = true;
                    }
                    MainForm.mainForm.xFlasherBlocksUpdate(blocks.ToString("X"), ((blocks - start) * 100) / length);
                }
                else MainForm.mainForm.xFlasherBlocksUpdate("Initializing", 0);
                Thread.Sleep(40);
            }
        }
        public void geteMMCBlocks(int start, int length)
        {
            int blocks = 0;
            while (inUse)
            {
                blocks = emmcGetBlocks();
                if (blocks >= 0)
                {
                    if (!inUseTimer.Enabled)
                    {
                        xFlasherTimeString = "< 1 sec(s)"; // If it doesn't update at least once, time was less than 1 second
                        inUseTimer.Enabled = true;
                    }

                    MainForm.mainForm.xFlasherBlocksUpdate(blocks.ToString("X"), ((blocks - start) * 100) / length);
                }
                else MainForm.mainForm.xFlasherBlocksUpdate("Initializing", 0);
                Thread.Sleep(40);
            }
        }

        // SVF Flashing
        public void flashSvf(string filename, string speed = "1M")
        {
            string xsvfToolPath = @"common/xsvftool/x86/xsvftool-ftd2xx.exe";

            if (Environment.Is64BitOperatingSystem)
            {
                xsvfToolPath = @"common/xsvftool/x64/xsvftool-ftd2xx.exe";
            }

            if (inUse || waiting) return;

            if (Process.GetProcessesByName("xsvftool").Length > 0)
            {
                Console.WriteLine("xFlasher: XSVFtool is already running!");
                return;
            }

            Thread xsvfToolThread = new Thread(() =>
            {
                try
                {
                    bool xsvf = false;
                    if (!ready)
                    {
                        waiting = true;
                        MainForm.mainForm.xFlasherBusy(-2);
                        Console.WriteLine("xFlasher: Waiting for device to become ready");
                    }
                    while (!ready)
                    {
                        // Do nothing and wait
                    }

                    if (!File.Exists(filename))
                    {
                        Console.WriteLine("xFlasher: File Not Found: {0}", filename);
                        return;
                    }
                    if (Path.GetExtension(filename) != ".svf" && Path.GetExtension(filename) != ".xsvf")
                    {
                        Console.WriteLine("xFlasher: Wrong File Type: {0}", filename);
                        return;
                    }
                    if (Path.GetExtension(filename) == ".xsvf") xsvf = true;
                    try
                    {
                        if (File.Exists(MainForm.tempTimingPath))
                        {
                            File.Delete(MainForm.tempTimingPath);
                        }
                        File.Copy(filename, MainForm.tempTimingPath);
                    }
                    catch
                    {
                        Console.WriteLine("xFlasher: Could not open temporary file for flashing");
                        Console.WriteLine("xFlasher: {0} is locked by another process", MainForm.tempTimingPath);
                        return;
                    }

                    Console.WriteLine("xFlasher: Flashing {0} via xsvftool", Path.GetFileName(filename));
                    Console.WriteLine("xFlasher: Setting flash speed to {0}", speed);

                    Process psi = new Process();
                    psi.StartInfo.FileName = xsvfToolPath;
                    psi.StartInfo.Arguments = "-l";
                    psi.StartInfo.CreateNoWindow = true;
                    psi.StartInfo.UseShellExecute = false;
                    psi.StartInfo.RedirectStandardOutput = true;
                    psi.StartInfo.RedirectStandardInput = true;
                    psi.StartInfo.RedirectStandardError = true;

                    inUse = true;
                    psi.Start();

                    StreamReader rr = psi.StandardOutput;
                    string str = rr.ReadToEnd().Replace("\n", "\r\n");
                    rr.Close();
                    inUse = false;
                    Match dev = Regex.Match(str, @"Device\s+\d+\s+-\s+(.+)");
                    if (dev.Groups.Count >= 2 && dev.Groups[1].Value != "")
                    {
                        Console.WriteLine($"xFlasher: {dev} detected");
                        psi = new Process();
                        psi.StartInfo.FileName = xsvfToolPath;
                        psi.StartInfo.Arguments = "-j 0 -p -f " + speed + (xsvf ? " -x" : " -s") + " \"" + MainForm.tempTimingPath + "\"";
                        psi.StartInfo.CreateNoWindow = true;
                        psi.StartInfo.UseShellExecute = false;
                        psi.StartInfo.RedirectStandardOutput = true;
                        psi.StartInfo.RedirectStandardInput = true;
                        psi.StartInfo.RedirectStandardError = true;
                        psi.OutputDataReceived += (procSender, procE) =>
                        {
                            if (procE.Data != null)
                            {
                                if (procE.Data.Contains("Progress : "))
                                    MainForm.mainForm.updateProgress(int.Parse(new Regex(@"\[(.*?)\]").Match(procE.Data).Groups[0].Value.Replace("[", "").Replace("%]", "")));
                            }
                        };
                        inUse = true;

                        // Count process time
                        Stopwatch watch = new Stopwatch();
                        watch.Start();
                        psi.Start();
                        psi.BeginOutputReadLine();
                        psi.WaitForExit();
                        watch.Stop();

                        inUse = false;

                        if (psi.ExitCode == 0)
                        {
                            if (variables.playSuccess)
                            {
                                SoundPlayer success = new SoundPlayer(Properties.Resources.chime);
                                success.Play();
                            }
                            string time = $"{watch.Elapsed.TotalSeconds:F2} sec(s)";
                            Console.WriteLine("xFlasher: Flash Successful! Time Elapsed: {0}", time);
                        }
                        else Console.WriteLine("xFlasher: Flash Failed!");

                        Console.WriteLine();

                        if (File.Exists(MainForm.tempTimingPath))
                        {
                            File.Delete(MainForm.tempTimingPath);
                        }
                    }
                    else Console.WriteLine("xFlasher: Could not detect device");
                }
                catch (Exception ex)
                {
                    inUse = false;

                    Console.WriteLine(ex.Message);
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    Console.WriteLine("");
                }
            });
            xsvfToolThread.Start();
        }
    }
}
