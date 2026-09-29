using JRunner.Nand;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace JRunner.Forms
{
    internal sealed partial class NandConverter : Form
    {
        private sealed class TargetItem
        {
            internal NandConversionTarget Target;
            internal string Text;
            public override string ToString() { return Text; }
        }

        private readonly Action<string> loadCallback;
        private readonly string detectedConsoleType;
        private NandImageInfo inputInfo;
        private bool working;
        private bool changingManualConsoleType;

        internal NandConverter(string initialInput, string currentConsole, Action<string> loadCallback)
        {
            this.loadCallback = loadCallback;
            string normalizedConsole = NormalizeConsole(currentConsole);
            detectedConsoleType = String.IsNullOrEmpty(normalizedConsole) ? "Trinity" : normalizedConsole;
            InitializeComponent();

            int consoleIndex = consoleType.Items.IndexOf(detectedConsoleType);
            consoleType.SelectedIndex = consoleIndex >= 0 ? consoleIndex : 4;
            if (!String.IsNullOrWhiteSpace(initialInput) && File.Exists(initialInput))
            {
                inputPath.Text = Path.GetFullPath(initialInput);
                DetectInput(true);
            }
        }

        private void ConsoleTypeSelectedIndexChanged(object sender, EventArgs e)
        {
            PopulateTargets();
        }

        private void ManualConsoleTypeChanged(object sender, EventArgs e)
        {
            if (changingManualConsoleType) return;
            consoleType.Enabled = false;
            if (!manualConsoleType.Checked)
            {
                RestoreDetectedConsoleType();
                return;
            }
            if (working) return;

            DialogResult first = ShowTimedConfirmation(
                "Are you really sure you know what you are doing?");
            if (first != DialogResult.Yes)
            {
                DisableManualConsoleType();
                return;
            }

            DialogResult second = ShowTimedConfirmation(
                "Are you REALLY sure you know what you are doing?");
            if (second != DialogResult.Yes)
            {
                DisableManualConsoleType();
                return;
            }

            consoleType.Enabled = true;
            consoleType.Focus();
        }

        private DialogResult ShowTimedConfirmation(string message)
        {
            using (Form dialog = new Form())
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                dialog.Text = "Manual Console Selection";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.ClientSize = new Size(440, 145);
                dialog.Font = SystemFonts.MessageBoxFont;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;

                PictureBox icon = new PictureBox();
                icon.Image = SystemIcons.Warning.ToBitmap();
                icon.Location = new Point(18, 20);
                icon.Size = new Size(32, 32);
                icon.SizeMode = PictureBoxSizeMode.CenterImage;
                dialog.Controls.Add(icon);

                Label prompt = new Label();
                prompt.Location = new Point(68, 20);
                prompt.Size = new Size(352, 54);
                prompt.Text = message;
                dialog.Controls.Add(prompt);

                Button yes = new Button();
                yes.Location = new Point(254, 96);
                yes.Size = new Size(80, 28);
                yes.Text = "Yes (5)";
                yes.ForeColor = Color.Red;
                yes.Enabled = false;
                yes.DialogResult = DialogResult.Yes;
                dialog.Controls.Add(yes);

                Button no = new Button();
                no.Location = new Point(340, 96);
                no.Size = new Size(80, 28);
                no.Text = "No";
                no.DialogResult = DialogResult.No;
                dialog.Controls.Add(no);

                dialog.AcceptButton = no;
                dialog.CancelButton = no;

                int secondsRemaining = 5;
                timer.Interval = 1000;
                timer.Tick += delegate
                {
                    secondsRemaining--;
                    if (secondsRemaining > 0)
                    {
                        yes.Text = String.Format("Yes ({0})", secondsRemaining);
                    }
                    else
                    {
                        timer.Stop();
                        yes.Text = "Yes";
                        yes.Enabled = true;
                    }
                };
                dialog.Shown += delegate { timer.Start(); };

                try
                {
                    return dialog.ShowDialog(this);
                }
                finally
                {
                    timer.Stop();
                    if (icon.Image != null) icon.Image.Dispose();
                }
            }
        }

        private void DisableManualConsoleType()
        {
            consoleType.Enabled = false;
            changingManualConsoleType = true;
            manualConsoleType.Checked = false;
            changingManualConsoleType = false;
            RestoreDetectedConsoleType();
        }

        private void RestoreDetectedConsoleType()
        {
            int consoleIndex = consoleType.Items.IndexOf(detectedConsoleType);
            consoleType.SelectedIndex = consoleIndex >= 0 ? consoleIndex : 4;
        }

        private void BrowseInput(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "NAND images (*.bin)|*.bin|All files (*.*)|*.*";
                dialog.Title = "Select input NAND image";
                dialog.RestoreDirectory = true;
                if (File.Exists(inputPath.Text)) dialog.InitialDirectory = Path.GetDirectoryName(inputPath.Text);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                inputPath.Text = dialog.FileName;
                DetectInput(true);
            }
        }

        private void BrowseOutput(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "NAND images (*.bin)|*.bin|All files (*.*)|*.*";
                dialog.Title = "Select converted NAND output";
                dialog.OverwritePrompt = true;
                dialog.RestoreDirectory = true;
                if (File.Exists(inputPath.Text))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(inputPath.Text);
                    dialog.FileName = SuggestedOutputName();
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) outputPath.Text = dialog.FileName;
            }
        }

        private void DetectInput(bool preferFlashConfig)
        {
            try
            {
                inputInfo = NandImageConverter.Detect(inputPath.Text);
                detected.Text = "Detected: " + inputInfo.Description + String.Format("  [0x{0:X} bytes]", inputInfo.FileLength);
                status.Text = "Input detected successfully.";
                PopulateTargets(preferFlashConfig);
                if (String.IsNullOrWhiteSpace(outputPath.Text))
                    outputPath.Text = Path.Combine(Path.GetDirectoryName(inputPath.Text), SuggestedOutputName());
            }
            catch (Exception ex)
            {
                inputInfo = null;
                detected.Text = "Detection failed: " + ex.Message;
                status.Text = "Select a supported NAND image.";
            }
        }

        private void PopulateTargets()
        {
            PopulateTargets(false);
        }

        private void PopulateTargets(bool preferFlashConfig)
        {
            NandConversionTarget? previous = null;
            TargetItem selected = outputType.SelectedItem as TargetItem;
            if (selected != null) previous = selected.Target;
            outputType.Items.Clear();
            IList<NandConversionTarget> targets = NandImageConverter.GetTargets(consoleType.Text);
            NandConversionTarget flashTarget = NandConversionTarget.BigOnSmall16;
            bool hasFlashTarget = preferFlashConfig &&
                NandImageConverter.TryGetTargetForFlashConfig(variables.flashconfig, out flashTarget);
            int preferred = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                TargetItem item = new TargetItem { Target = targets[i], Text = NandImageConverter.TargetDescription(targets[i]) };
                outputType.Items.Add(item);
                if (hasFlashTarget && flashTarget == item.Target) preferred = i;
                else if (preferred < 0 && previous.HasValue && previous.Value == item.Target) preferred = i;
                else if (preferred < 0 && !previous.HasValue && IsMatchingTarget(item.Target)) preferred = i;
            }
            if (outputType.Items.Count > 0) outputType.SelectedIndex = preferred >= 0 ? preferred : 0;
        }

        private bool IsMatchingTarget(NandConversionTarget target)
        {
            if (inputInfo == null) return false;
            switch (inputInfo.Kind)
            {
                case NandImageKind.SmallBlock16: return target == NandConversionTarget.SmallBlock16;
                case NandImageKind.SmallBlock64: return target == NandConversionTarget.SmallBlock64;
                case NandImageKind.BigOnSmall16: return target == NandConversionTarget.BigOnSmall16;
                case NandImageKind.BigOnSmall64: return target == NandConversionTarget.BigOnSmall64;
                case NandImageKind.BigBlock256: return target == NandConversionTarget.BigBlock256;
                case NandImageKind.BigBlock512: return target == NandConversionTarget.BigBlock512;
                case NandImageKind.BigBlockSystem: return target == NandConversionTarget.BigBlock512;
                case NandImageKind.BigBlock1024: return target == NandConversionTarget.BigBlock1024;
                case NandImageKind.Emmc4Gb: return target == NandConversionTarget.Emmc4Gb;
                default: return false;
            }
        }

        private string SuggestedOutputName()
        {
            return "updflash.bin";
        }

        private void StartConversion(object sender, EventArgs e)
        {
            if (working) return;
            DetectInput(false);
            TargetItem item = outputType.SelectedItem as TargetItem;
            if (inputInfo == null || item == null)
            {
                MessageBox.Show(this, "Select a valid input image, console type, and output type.", "Can't Convert",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (String.IsNullOrWhiteSpace(outputPath.Text))
            {
                MessageBox.Show(this, "Select an output file.", "Can't Convert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string source = Path.GetFullPath(inputPath.Text);
            string destination = Path.GetFullPath(outputPath.Text);
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "The output must be a different file. The source will not be overwritten.",
                    "Can't Convert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (File.Exists(destination) && MessageBox.Show(this, "The output file already exists. Replace it?",
                "Replace Output", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            NandConversionTarget target = item.Target;
            SetWorking(true);
            progress.Value = 0;
            status.Text = "Converting...";
            Console.WriteLine("\nExperimental NAND conversion started");
            Console.WriteLine("Input:  {0}", source);
            Console.WriteLine("Output: {0}", destination);
            Console.WriteLine("Console: {0}; target: {1}", consoleType.Text, NandImageConverter.TargetDescription(target));

            Thread worker = new Thread(delegate()
            {
                try
                {
                    NandConversionResult result = NandImageConverter.Convert(source, destination, target, delegate(int value)
                    {
                        if (!IsDisposed) BeginInvoke((Action)delegate { progress.Value = Math.Max(0, Math.Min(100, value)); });
                    });
                    BeginInvoke((Action)delegate
                    {
                        SetWorking(false);
                        progress.Value = 100;
                        status.Text = result.Detail;
                        Console.WriteLine("NAND conversion complete: {0}", result.Detail);
                        if (loadOutput.Checked && loadCallback != null) loadCallback(destination);
                        MessageBox.Show(this, result.Detail + "\n\n" + destination, "Conversion Complete",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex)
                {
                    if (variables.debugMode) Console.WriteLine(ex.ToString());
                    BeginInvoke((Action)delegate
                    {
                        SetWorking(false);
                        status.Text = "Conversion failed.";
                        MessageBox.Show(this, ex.Message, "Conversion Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
            worker.IsBackground = true;
            worker.Name = "NAND Image Converter";
            worker.Start();
        }

        private void SetWorking(bool value)
        {
            working = value;
            inputPath.Enabled = !value;
            outputPath.Enabled = !value;
            browseInput.Enabled = !value;
            browseOutput.Enabled = !value;
            manualConsoleType.Enabled = !value;
            consoleType.Enabled = !value && manualConsoleType.Checked;
            outputType.Enabled = !value;
            convert.Enabled = !value;
            close.Enabled = !value;
            ControlBox = !value;
        }

        private static string NormalizeConsole(string value)
        {
            string lower = (value ?? String.Empty).ToLowerInvariant();
            if (lower.Contains("winchester")) return "Winchester";
            if (lower.Contains("corona")) return "Corona";
            if (lower.Contains("trinity")) return "Trinity";
            if (lower.Contains("jasper")) return "Jasper";
            if (lower.Contains("falcon")) return "Falcon";
            if (lower.Contains("zephyr")) return "Zephyr";
            if (lower.Contains("xenon")) return "Xenon";
            return String.Empty;
        }
    }
}
