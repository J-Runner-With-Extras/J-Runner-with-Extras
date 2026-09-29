namespace JRunner.Forms
{
    partial class NandConverter
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label inputLabel;
        private System.Windows.Forms.TextBox inputPath;
        private System.Windows.Forms.Button browseInput;
        private System.Windows.Forms.Label detected;
        private System.Windows.Forms.Label consoleTypeLabel;
        private System.Windows.Forms.ComboBox consoleType;
        private System.Windows.Forms.CheckBox manualConsoleType;
        private System.Windows.Forms.Label outputTypeLabel;
        private System.Windows.Forms.ComboBox outputType;
        private System.Windows.Forms.Label outputFileLabel;
        private System.Windows.Forms.TextBox outputPath;
        private System.Windows.Forms.Button browseOutput;
        private System.Windows.Forms.Label note;
        private System.Windows.Forms.ProgressBar progress;
        private System.Windows.Forms.Label status;
        private System.Windows.Forms.CheckBox loadOutput;
        private System.Windows.Forms.Button convert;
        private System.Windows.Forms.Button close;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.inputLabel = new System.Windows.Forms.Label();
            this.inputPath = new System.Windows.Forms.TextBox();
            this.browseInput = new System.Windows.Forms.Button();
            this.detected = new System.Windows.Forms.Label();
            this.consoleTypeLabel = new System.Windows.Forms.Label();
            this.consoleType = new System.Windows.Forms.ComboBox();
            this.manualConsoleType = new System.Windows.Forms.CheckBox();
            this.outputTypeLabel = new System.Windows.Forms.Label();
            this.outputType = new System.Windows.Forms.ComboBox();
            this.outputFileLabel = new System.Windows.Forms.Label();
            this.outputPath = new System.Windows.Forms.TextBox();
            this.browseOutput = new System.Windows.Forms.Button();
            this.note = new System.Windows.Forms.Label();
            this.progress = new System.Windows.Forms.ProgressBar();
            this.status = new System.Windows.Forms.Label();
            this.loadOutput = new System.Windows.Forms.CheckBox();
            this.convert = new System.Windows.Forms.Button();
            this.close = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // inputLabel
            // 
            this.inputLabel.Location = new System.Drawing.Point(14, 18);
            this.inputLabel.Name = "inputLabel";
            this.inputLabel.Size = new System.Drawing.Size(105, 22);
            this.inputLabel.TabIndex = 0;
            this.inputLabel.Text = "Input NAND:";
            // 
            // inputPath
            // 
            this.inputPath.Location = new System.Drawing.Point(122, 15);
            this.inputPath.Name = "inputPath";
            this.inputPath.Size = new System.Drawing.Size(465, 23);
            this.inputPath.TabIndex = 1;
            // 
            // browseInput
            // 
            this.browseInput.Location = new System.Drawing.Point(596, 14);
            this.browseInput.Name = "browseInput";
            this.browseInput.Size = new System.Drawing.Size(80, 25);
            this.browseInput.TabIndex = 2;
            this.browseInput.Text = "Browse...";
            this.browseInput.UseVisualStyleBackColor = true;
            this.browseInput.Click += new System.EventHandler(this.BrowseInput);
            // 
            // detected
            // 
            this.detected.Location = new System.Drawing.Point(122, 44);
            this.detected.Name = "detected";
            this.detected.Size = new System.Drawing.Size(554, 38);
            this.detected.TabIndex = 3;
            this.detected.Text = "Select an input NAND image.";
            // 
            // consoleTypeLabel
            // 
            this.consoleTypeLabel.Location = new System.Drawing.Point(14, 91);
            this.consoleTypeLabel.Name = "consoleTypeLabel";
            this.consoleTypeLabel.Size = new System.Drawing.Size(105, 22);
            this.consoleTypeLabel.TabIndex = 4;
            this.consoleTypeLabel.Text = "Console type:";
            // 
            // consoleType
            // 
            this.consoleType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.consoleType.Enabled = false;
            this.consoleType.FormattingEnabled = true;
            this.consoleType.Items.AddRange(new object[] {
            "Xenon",
            "Zephyr",
            "Falcon",
            "Jasper",
            "Trinity",
            "Corona",
            "Winchester"});
            this.consoleType.Location = new System.Drawing.Point(122, 88);
            this.consoleType.Name = "consoleType";
            this.consoleType.Size = new System.Drawing.Size(230, 24);
            this.consoleType.TabIndex = 5;
            this.consoleType.SelectedIndexChanged += new System.EventHandler(this.ConsoleTypeSelectedIndexChanged);
            // 
            // manualConsoleType
            // 
            this.manualConsoleType.Location = new System.Drawing.Point(365, 90);
            this.manualConsoleType.Name = "manualConsoleType";
            this.manualConsoleType.Size = new System.Drawing.Size(225, 22);
            this.manualConsoleType.TabIndex = 6;
            this.manualConsoleType.Text = "I know what I am doing";
            this.manualConsoleType.UseVisualStyleBackColor = true;
            this.manualConsoleType.CheckedChanged += new System.EventHandler(this.ManualConsoleTypeChanged);
            // 
            // outputTypeLabel
            // 
            this.outputTypeLabel.Location = new System.Drawing.Point(14, 176);
            this.outputTypeLabel.Name = "outputTypeLabel";
            this.outputTypeLabel.Size = new System.Drawing.Size(105, 22);
            this.outputTypeLabel.TabIndex = 8;
            this.outputTypeLabel.Text = "Output type:";
            // 
            // outputType
            // 
            this.outputType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.outputType.FormattingEnabled = true;
            this.outputType.Location = new System.Drawing.Point(122, 173);
            this.outputType.Name = "outputType";
            this.outputType.Size = new System.Drawing.Size(465, 24);
            this.outputType.TabIndex = 9;
            // 
            // outputFileLabel
            // 
            this.outputFileLabel.Location = new System.Drawing.Point(14, 214);
            this.outputFileLabel.Name = "outputFileLabel";
            this.outputFileLabel.Size = new System.Drawing.Size(105, 22);
            this.outputFileLabel.TabIndex = 10;
            this.outputFileLabel.Text = "Output file:";
            // 
            // outputPath
            // 
            this.outputPath.Location = new System.Drawing.Point(122, 211);
            this.outputPath.Name = "outputPath";
            this.outputPath.Size = new System.Drawing.Size(465, 23);
            this.outputPath.TabIndex = 11;
            // 
            // browseOutput
            // 
            this.browseOutput.Location = new System.Drawing.Point(596, 210);
            this.browseOutput.Name = "browseOutput";
            this.browseOutput.Size = new System.Drawing.Size(80, 25);
            this.browseOutput.TabIndex = 12;
            this.browseOutput.Text = "Browse...";
            this.browseOutput.UseVisualStyleBackColor = true;
            this.browseOutput.Click += new System.EventHandler(this.BrowseOutput);
            // 
            // note
            // 
            this.note.ForeColor = System.Drawing.Color.Red;
            this.note.Location = new System.Drawing.Point(14, 119);
            this.note.Name = "note";
            this.note.Size = new System.Drawing.Size(662, 48);
            this.note.TabIndex = 7;
            this.note.Text = "WARNING - RESEARCH/EXPERIMENTAL ONLY: Changing console type does not replace the SMC, SMC Config, bootloaders, KV, or other console-specific payloads. It only converts storage geometry and filesystem layout.";
            // 
            // progress
            // 
            this.progress.Location = new System.Drawing.Point(14, 254);
            this.progress.Name = "progress";
            this.progress.Size = new System.Drawing.Size(662, 20);
            this.progress.TabIndex = 13;
            // 
            // status
            // 
            this.status.Location = new System.Drawing.Point(14, 280);
            this.status.Name = "status";
            this.status.Size = new System.Drawing.Size(662, 20);
            this.status.TabIndex = 14;
            this.status.Text = "Ready";
            // 
            // loadOutput
            // 
            this.loadOutput.Checked = true;
            this.loadOutput.CheckState = System.Windows.Forms.CheckState.Checked;
            this.loadOutput.Location = new System.Drawing.Point(14, 311);
            this.loadOutput.Name = "loadOutput";
            this.loadOutput.Size = new System.Drawing.Size(310, 22);
            this.loadOutput.TabIndex = 15;
            this.loadOutput.Text = "Load converted NAND into Source File";
            this.loadOutput.UseVisualStyleBackColor = true;
            // 
            // convert
            // 
            this.convert.Location = new System.Drawing.Point(500, 307);
            this.convert.Name = "convert";
            this.convert.Size = new System.Drawing.Size(85, 30);
            this.convert.TabIndex = 16;
            this.convert.Text = "Convert";
            this.convert.UseVisualStyleBackColor = true;
            this.convert.Click += new System.EventHandler(this.StartConversion);
            // 
            // close
            // 
            this.close.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.close.Location = new System.Drawing.Point(591, 307);
            this.close.Name = "close";
            this.close.Size = new System.Drawing.Size(85, 30);
            this.close.TabIndex = 17;
            this.close.Text = "Close";
            this.close.UseVisualStyleBackColor = true;
            // 
            // NandConverter
            // 
            this.CancelButton = this.close;
            this.ClientSize = new System.Drawing.Size(690, 365);
            this.Controls.Add(this.close);
            this.Controls.Add(this.convert);
            this.Controls.Add(this.loadOutput);
            this.Controls.Add(this.status);
            this.Controls.Add(this.progress);
            this.Controls.Add(this.note);
            this.Controls.Add(this.browseOutput);
            this.Controls.Add(this.outputPath);
            this.Controls.Add(this.outputFileLabel);
            this.Controls.Add(this.outputType);
            this.Controls.Add(this.outputTypeLabel);
            this.Controls.Add(this.manualConsoleType);
            this.Controls.Add(this.consoleType);
            this.Controls.Add(this.consoleTypeLabel);
            this.Controls.Add(this.detected);
            this.Controls.Add(this.browseInput);
            this.Controls.Add(this.inputPath);
            this.Controls.Add(this.inputLabel);
            this.Font = System.Drawing.SystemFonts.MessageBoxFont;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "NandConverter";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Experimental NAND Image Converter";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
