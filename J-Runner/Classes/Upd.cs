using Ionic.Zip;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace JRunner
{
    public static class Upd
    {
        public static int checkStatus = 0; // Default success
        public static bool upToDate = true; // Default true
        public static string failedReason = "Unknown";
        public static string changelog = "Could not retrieve changelog for some reason!"; // Overwritten if successful

        private static string fullUrl;
        private static string expectedFullDigest = "";

        private static int serverVersion = 0;
        private static int serverRelease = 0;
        private static int serverModpack = 0;
        private static int serverFixpack = 0;

        public static bool deleteFolders = false;
        public static bool noUpdateChk = false;
        public static bool runFullUpdate = false;

        private static WebClient wc = null;
        private static UpdUI updUI = null;
        private static HttpClient jsonclient = null;

        public static void check()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // Enable TLS1.2 to connect to GitHub

            try
            {
                jsonclient = new HttpClient();
                jsonclient.DefaultRequestHeaders.Add("User-Agent", "J-Runner-With-Extras/" + variables.staticversion);

                string jsondatastring = jsonclient.GetStringAsync("https://api.github.com/repos/J-Runner-With-Extras/J-Runner-with-Extras/releases/latest").Result;
                JObject releaseData = JObject.Parse(jsondatastring);

                changelog = releaseData["body"].ToString();

                foreach (JObject j in releaseData["assets"])
                {
                    string assetName = j["name"].ToString();

                    if (assetName == "J-Runner-with-Extras.zip" ||
                        assetName == "J-Runner.with.Extras.zip")
                    {
                        fullUrl = j["browser_download_url"].ToString();
                        expectedFullDigest = j["digest"].ToString();
                    }
                }

                // Parse the version string woohoo
                string[] releaseTagStringArr = releaseData["tag_name"].ToString().Split('.');

                if (releaseTagStringArr.Length == 3)
                {
                    // Tag is in format V3.4.0-r3 or V3.4.0
                    serverVersion = int.Parse(releaseTagStringArr[0].Substring(1));
                    serverRelease = int.Parse(releaseTagStringArr[1]);

                    string[] mfStringArr = releaseTagStringArr[2].Split('-');

                    serverModpack = int.Parse(mfStringArr[0]);

                    if (mfStringArr.Length > 1)
                    {
                        serverFixpack = int.Parse(mfStringArr[1].Substring(1));
                    }
                }
                else if (releaseTagStringArr.Length == 4)
                {
                    // Tag is in format 3.4.0.3
                    serverVersion = int.Parse(releaseTagStringArr[0]);
                    serverRelease = int.Parse(releaseTagStringArr[1]);
                    serverModpack = int.Parse(releaseTagStringArr[2]);
                    serverFixpack = int.Parse(releaseTagStringArr[3]);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("SSL/TLS")) checkStatus = 2;
                else checkStatus = 1;
            }

            Thread.Sleep(100);
            MainForm.mainForm.splash.BeginInvoke(new Action(() =>
            {
                MainForm.mainForm.splash.Hide();
            }));
            updUI = new UpdUI();

            if (checkStatus == 0)
            {
                if (runFullUpdate)
                {
                    startFull();
                }
                else if ( variables.jrVersion >= serverVersion &&
                          variables.jrRelease >= serverRelease &&
                          variables.jrModpack >= serverModpack &&
                          variables.jrFixpack >= serverFixpack ) // Up to Date
                {
                    upToDate = true;
                    MainForm.mainForm.startMainForm(true);
                }
                else
                {
                    upToDate = false;

                    MainForm.mainForm.splash.BeginInvoke(new Action(() =>
                    {
                        UpdChangelog updChg = new UpdChangelog();
                        updChg.Show();
                        updChg.showChangelog(changelog);
                    }));
                }
            }
            else
            {
                if (runFullUpdate)
                {
                    if (checkStatus == 2) failedReason = "Could not connect to the update server because TLS1.2 is not enabled.";
                    else failedReason = "Could not connect to the update server.";
                    showUpdUI(1);
                }
                else
                {
                    MainForm.mainForm.startMainForm(true);
                }
            }
        }

        public static void startFull()
        {
            Thread updateFull = new Thread(() =>
            {
                if (File.Exists(@"full.zip")) File.Delete(@"full.zip");

                if (deleteFolders)
                {
                    Thread.Sleep(500); // Make sure all files are released

                    try
                    {
                        if (Directory.Exists("common")) Directory.Delete("common", true);
                        if (Directory.Exists("xeBuild")) Directory.Delete("xeBuild", true);
                    }
                    catch
                    {
                        failedReason = "Failed to cleanup the filesystem.";
                        setUpdUIPage(1);
                    }
                }

                wc = new WebClient();
                wc.DownloadProgressChanged += updUI.updateProgress;
                wc.DownloadFileCompleted += full;
                wc.DownloadFileAsync(new Uri(fullUrl), "full.zip");
            });
            showUpdUI();
            updateFull.Start();
        }

        private static void full(object sender, AsyncCompletedEventArgs e)
        {
            wc.Dispose();

            if (e.Cancelled)
            {
                // Do nothing
            }
            else if (e.Error != null)
            {
                if (File.Exists(@"full.zip")) File.Delete(@"full.zip");
                if (e.Error.ToString().Contains("SSL/TLS")) failedReason = "Could not connect to the update server because TLS1.2 is not enabled.";
                else failedReason = "Failed to download the package.";
                setUpdUIPage(1);
            }
            else
            {
                install();
            }
        }

        private static void install()
        {
            string filename = @"full.zip";

            try
            {
                setUpdUiInstallMode();

                if (true != simpleCheckDigest(filename, expectedFullDigest))
                {
                    if (File.Exists(filename)) File.Delete(filename);
                    failedReason = "Package checksum is invalid.";
                    setUpdUIPage(1);
                    return;
                }

                // Install Package
                File.Move(AppDomain.CurrentDomain.FriendlyName, @"JRunner.exe.old");

                using (ZipFile zip = ZipFile.Read(filename))
                {
                    zip.ExtractAll(Environment.CurrentDirectory, ExtractExistingFileAction.OverwriteSilently);
                }
                File.Delete(filename);

                if (AppDomain.CurrentDomain.FriendlyName != "JRunner.exe")
                {
                    if (File.Exists("JRunner.exe")) File.Move("JRunner.exe", AppDomain.CurrentDomain.FriendlyName);
                }

                setUpdUIPage(0);
            }
            catch (Exception ex)
            {
                if (File.Exists(filename)) File.Delete(filename);
                File.AppendAllText("Error.log", ex.ToString() + Environment.NewLine);
                failedReason = "Failed to extract and install the package.";
                setUpdUIPage(1);
            }
        }

        private static string simpleByteArrayToString(byte[] ba)
        {
            return BitConverter.ToString(ba).Replace("-", "");
        }

        private static bool simpleCheckDigest(string filename, string expectedDigest)
        {
            // Expected digest is in the format <algorithm>:<digest>
            string[] expectedDigestArr = expectedDigest.Split(':');

            if (expectedDigestArr[0].ToString() != "sha256")
            {
                return false;
            }

            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    string shastr;
                    shastr = simpleByteArrayToString(sha256.ComputeHash(stream));
                    stream.Dispose();

                    if (shastr.ToLower() == expectedDigestArr[1].ToLower())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static void cancel()
        {
            try
            {
                wc.CancelAsync();
            }
            catch { }

            Thread.Sleep(100);

            try
            {
                if (File.Exists(@"full.zip")) File.Delete(@"full.zip");
            }
            catch { }

            Application.ExitThread();
            Application.Exit();
        }

        public static void restoreFiles()
        {
            Thread worker = new Thread(() =>
            {
                try
                {
                    ProcessStartInfo jr = new ProcessStartInfo();
                    jr.FileName = "JRunner.exe";
                    jr.Arguments = "/restorefiles";
                    jr.UseShellExecute = true;

                    Process.Start(jr);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not restore files due to the following error:");
                    Console.WriteLine(ex.ToString());
                }
            });
            worker.Start();
        }

        private static void showUpdUI(int type = 0)
        {
            MainForm.mainForm.splash.BeginInvoke(new Action(() =>
            {
                updUI.Show();
                if (type == 1) updUI.showFailed();
                MainForm.mainForm.splash.Dispose();
            }));
        }

        private static void setUpdUiInstallMode()
        {
            updUI.BeginInvoke(new Action(() =>
            {
                updUI.installMode();
            }));
        }

        private static void setUpdUIPage(int type)
        {
            updUI.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (type == 0) updUI.showSuccess();
                    else if (type == 1) updUI.showFailed();
                }
                catch
                {
                    MessageBox.Show("A critical error has occurred!\n\nUpdate UI operation out of sequence\n\nPlease report this to the developers", "Something happened!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel();
                }
            }));
        }
    }
}
