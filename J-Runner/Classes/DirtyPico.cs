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
    public class DirtyPico
    {
        public bool ready = false;
        public bool inUse = false;
        public bool waiting = false;
        public int selType = 0;

        // SVF Flashing
        // Basically everything here is copied from xFlasher class
        public void flashSvf(string filename, string speed = "1M")
        {
            string xsvfToolPath = @"common/xsvftool/x86/xsvftool-dirtyjtag.exe";

            if (Environment.Is64BitOperatingSystem)
            {
                xsvfToolPath = @"common/xsvftool/x64/xsvftool-dirtyjtag.exe";
            }

            if (inUse || waiting) return;

            if (Process.GetProcessesByName("xsvftool").Length > 0)
            {
                Console.WriteLine("DirtyPico: XSVFtool is already running!");
                return;
            }

            Thread xsvfToolThread = new Thread(() =>
            {
                try
                {
                    bool xsvf = false;
                    bool bChipIsDetected = false;

                    // Leftover from xFlash class. Not needed here.
                    //if (!ready)
                    //{
                    //    waiting = true;
                    //    MainForm.mainForm.xFlasherBusy(-2);
                    //    Console.WriteLine("DirtyPico: Waiting for device to become ready");
                    //}
                    //while (!ready)
                    //{
                    //    //Do nothing and wait
                    //}

                    if (!File.Exists(filename))
                    {
                        Console.WriteLine("DirtyPico: File Not Found: {0}", filename);
                        return;
                    }
                    if (Path.GetExtension(filename) != ".svf" && Path.GetExtension(filename) != ".xsvf")
                    {
                        Console.WriteLine("DirtyPico: Wrong File Type: {0}", filename);
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
                        Console.WriteLine("DirtyPico: Could not open temporary file for flashing");
                        Console.WriteLine("DirtyPico: {0} is locked by another process", MainForm.tempTimingPath);
                        return;
                    }

                    Console.WriteLine("DirtyPico: Flashing {0} via xsvftool", Path.GetFileName(filename));
                    Console.WriteLine("DirtyPico: Setting flash speed to {0}", speed);

                    // Detect CPLD attached to the DirtyPico
                    Process psi = new Process();
                    psi.StartInfo.FileName = xsvfToolPath;
                    psi.StartInfo.Arguments = "-c";
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


                    Match dev = Regex.Match(str, @"idcode=0x(?<idcode>[0-9A-Fa-f]+),\s*revision=0x(?<revision>[0-9A-Fa-f]+),\s*part=0x(?<part>[0-9A-Fa-f]+),\s*manufactor=0x(?<manufacturer>[0-9A-Fa-f]+)");

                    if ( dev.Success &&
                         psi.ExitCode == 0 )
                    {
                        try
                        {
                            // If xsvftool returned something, but that something was all zeros,
                            // then something went wrong with the JTAG device or one wasn't attached
                            if (Convert.ToInt32(dev.Groups["idcode"].Value, 16) != 0)
                            {
                                bChipIsDetected = true;
                            }
                        } catch { }
                    }

                    if (bChipIsDetected)
                    {
                        Console.WriteLine($"DirtyPico: Detected chip ID: " + dev.Groups["idcode"].Value);

                        psi = new Process();
                        psi.StartInfo.FileName = xsvfToolPath;
                        psi.StartInfo.Arguments = "-p -f " + speed + (xsvf ? " -x" : " -s") + " \"" + MainForm.tempTimingPath + "\"";

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
                            Console.WriteLine("DirtyPico: Flash Successful! Time Elapsed: {0}", time);
                        }
                        else Console.WriteLine("DirtyPico: Flash Failed!");

                        Console.WriteLine();

                        if (File.Exists(MainForm.tempTimingPath))
                        {
                            File.Delete(MainForm.tempTimingPath);
                        }
                    }
                    else
                    {
                        if (psi.ExitCode == 0)
                        {
                            // the xsvftool call succeeded, but there were no suitable JTAG devices returned 
                            Console.WriteLine("DirtyPico: Glitch Chip not detected");
                        }
                        else
                        {
                            // xsvftool returned an error when scanning for the glitch chip, rip
                            Console.WriteLine($"DirtyPico: xsfvtool returned error {psi.ExitCode} when scanning for glitch chips");
                        }

                        // If JRunner is in debug mode, print the contents of stdout and stderr
                        // from xsvftool to the console
                        if (variables.debugMode)
                        {
                            Console.WriteLine(str);

                            StreamReader rErr = psi.StandardError;
                            string strErr = rErr.ReadToEnd().Replace("\n", "\r\n");
                            rErr.Close();

                            Console.WriteLine(strErr);
                        }

                        MainForm.mainForm.updateProgress(100);
                    }
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
