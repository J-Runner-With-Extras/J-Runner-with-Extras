namespace JRunner.Forms
{
    partial class EnterCPUKey
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EnterCPUKey));
            this.btnOk = new System.Windows.Forms.Button();
            this.txtCpuKey = new System.Windows.Forms.TextBox();
            this.lblPhysicalCpuKey = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.Location = new System.Drawing.Point(57, 86);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(129, 23);
            this.btnOk.TabIndex = 4;
            this.btnOk.Text = "OK";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);
            // 
            // txtCpuKey
            // 
            this.txtCpuKey.Location = new System.Drawing.Point(113, 60);
            this.txtCpuKey.MaxLength = 32;
            this.txtCpuKey.Name = "txtCpuKey";
            this.txtCpuKey.Size = new System.Drawing.Size(208, 20);
            this.txtCpuKey.TabIndex = 3;
            // 
            // lblPhysicalCpuKey
            // 
            this.lblPhysicalCpuKey.AutoSize = true;
            this.lblPhysicalCpuKey.Location = new System.Drawing.Point(12, 63);
            this.lblPhysicalCpuKey.Name = "lblPhysicalCpuKey";
            this.lblPhysicalCpuKey.Size = new System.Drawing.Size(95, 13);
            this.lblPhysicalCpuKey.TabIndex = 2;
            this.lblPhysicalCpuKey.Text = "Physical CPU Key:";
            this.lblPhysicalCpuKey.DoubleClick += new System.EventHandler(this.lblPhysicalCpuKey_DoubleClick);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(192, 86);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(129, 23);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(309, 16);
            this.label1.TabIndex = 0;
            this.label1.Text = "Enter the physical CPU Key of the console as reported by XeLL";
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Underline, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.Color.Firebrick;
            this.label2.Location = new System.Drawing.Point(12, 25);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(309, 23);
            this.label2.TabIndex = 1;
            this.label2.Text = "This is required for g3fix on a glitch2m image!";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // EnterCPUKey
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(328, 118);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblPhysicalCpuKey);
            this.Controls.Add(this.txtCpuKey);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnCancel);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EnterCPUKey";
            this.Text = "G3Fix: Enter Physical CPU Key";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.TextBox txtCpuKey;
        private System.Windows.Forms.Label lblPhysicalCpuKey;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}