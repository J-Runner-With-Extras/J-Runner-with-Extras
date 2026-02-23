namespace JRunner
{
    partial class XellCustomizer
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(XellCustomizer));
            this.chkEnableColours = new System.Windows.Forms.CheckBox();
            this.txtXellPreview = new System.Windows.Forms.TextBox();
            this.xellColorDialog = new System.Windows.Forms.ColorDialog();
            this.btnSelectBgcolor = new System.Windows.Forms.Button();
            this.btnSelectFgcolor = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.txtCopyrightString = new System.Windows.Forms.TextBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // chkEnableColours
            // 
            this.chkEnableColours.AutoSize = true;
            this.chkEnableColours.Location = new System.Drawing.Point(12, 16);
            this.chkEnableColours.Name = "chkEnableColours";
            this.chkEnableColours.Size = new System.Drawing.Size(135, 17);
            this.chkEnableColours.TabIndex = 0;
            this.chkEnableColours.Text = "Enable Custom Colours";
            this.chkEnableColours.UseVisualStyleBackColor = true;
            this.chkEnableColours.CheckedChanged += new System.EventHandler(this.chkEnableColours_CheckedChanged);
            // 
            // txtXellPreview
            // 
            this.txtXellPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(78)))), ((int)(((byte)(68)))), ((int)(((byte)(216)))));
            this.txtXellPreview.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtXellPreview.ForeColor = System.Drawing.Color.White;
            this.txtXellPreview.Location = new System.Drawing.Point(288, 10);
            this.txtXellPreview.Multiline = true;
            this.txtXellPreview.Name = "txtXellPreview";
            this.txtXellPreview.ReadOnly = true;
            this.txtXellPreview.Size = new System.Drawing.Size(240, 149);
            this.txtXellPreview.TabIndex = 1;
            this.txtXellPreview.Text = resources.GetString("txtXellPreview.Text");
            this.txtXellPreview.WordWrap = false;
            // 
            // btnSelectBgcolor
            // 
            this.btnSelectBgcolor.Enabled = false;
            this.btnSelectBgcolor.Location = new System.Drawing.Point(12, 39);
            this.btnSelectBgcolor.Name = "btnSelectBgcolor";
            this.btnSelectBgcolor.Size = new System.Drawing.Size(270, 23);
            this.btnSelectBgcolor.TabIndex = 4;
            this.btnSelectBgcolor.Text = "Select Background";
            this.btnSelectBgcolor.UseVisualStyleBackColor = true;
            this.btnSelectBgcolor.Click += new System.EventHandler(this.btnSelectBgcolor_Click);
            // 
            // btnSelectFgcolor
            // 
            this.btnSelectFgcolor.Enabled = false;
            this.btnSelectFgcolor.Location = new System.Drawing.Point(12, 68);
            this.btnSelectFgcolor.Name = "btnSelectFgcolor";
            this.btnSelectFgcolor.Size = new System.Drawing.Size(270, 23);
            this.btnSelectFgcolor.TabIndex = 7;
            this.btnSelectFgcolor.Text = "Select Foreground";
            this.btnSelectFgcolor.UseVisualStyleBackColor = true;
            this.btnSelectFgcolor.Click += new System.EventHandler(this.btnSelectFgcolor_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 94);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(81, 13);
            this.label3.TabIndex = 8;
            this.label3.Text = "Copyright String";
            // 
            // txtCopyrightString
            // 
            this.txtCopyrightString.Location = new System.Drawing.Point(12, 110);
            this.txtCopyrightString.MaxLength = 54;
            this.txtCopyrightString.Name = "txtCopyrightString";
            this.txtCopyrightString.Size = new System.Drawing.Size(270, 20);
            this.txtCopyrightString.TabIndex = 9;
            // 
            // btnSave
            // 
            this.btnSave.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnSave.Location = new System.Drawing.Point(12, 136);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(130, 23);
            this.btnSave.TabIndex = 10;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(152, 136);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(130, 23);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // XellCustomizer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(538, 170);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.txtCopyrightString);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.btnSelectFgcolor);
            this.Controls.Add(this.btnSelectBgcolor);
            this.Controls.Add(this.txtXellPreview);
            this.Controls.Add(this.chkEnableColours);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "XellCustomizer";
            this.Text = "Xell Theme Customizer";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox chkEnableColours;
        private System.Windows.Forms.TextBox txtXellPreview;
        private System.Windows.Forms.ColorDialog xellColorDialog;
        private System.Windows.Forms.Button btnSelectBgcolor;
        private System.Windows.Forms.Button btnSelectFgcolor;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtCopyrightString;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
    }
}