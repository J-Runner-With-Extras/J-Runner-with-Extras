
namespace JRunner.Forms
{
    partial class CPUKeyGenGUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CPUKeyGenGUI));
            this.txtGenKey = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnGenKey = new System.Windows.Forms.Button();
            this.btnInsertKey = new System.Windows.Forms.Button();
            this.btnValKey = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // txtGenKey
            // 
            this.txtGenKey.Location = new System.Drawing.Point(67, 9);
            this.txtGenKey.Margin = new System.Windows.Forms.Padding(2);
            this.txtGenKey.MaxLength = 32;
            this.txtGenKey.Name = "txtGenKey";
            this.txtGenKey.Size = new System.Drawing.Size(229, 20);
            this.txtGenKey.TabIndex = 0;
            this.txtGenKey.TextChanged += new System.EventHandler(this.txtGenKey_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 12);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(53, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "CPU Key:";
            // 
            // btnGenKey
            // 
            this.btnGenKey.Location = new System.Drawing.Point(300, 8);
            this.btnGenKey.Margin = new System.Windows.Forms.Padding(2);
            this.btnGenKey.Name = "btnGenKey";
            this.btnGenKey.Size = new System.Drawing.Size(126, 22);
            this.btnGenKey.TabIndex = 2;
            this.btnGenKey.Text = "Generate Key";
            this.btnGenKey.UseVisualStyleBackColor = true;
            this.btnGenKey.Click += new System.EventHandler(this.btnGenKey_Click);
            // 
            // btnInsertKey
            // 
            this.btnInsertKey.Enabled = false;
            this.btnInsertKey.Location = new System.Drawing.Point(560, 8);
            this.btnInsertKey.Margin = new System.Windows.Forms.Padding(2);
            this.btnInsertKey.Name = "btnInsertKey";
            this.btnInsertKey.Size = new System.Drawing.Size(126, 22);
            this.btnInsertKey.TabIndex = 5;
            this.btnInsertKey.Text = "Insert and Close";
            this.btnInsertKey.UseVisualStyleBackColor = true;
            this.btnInsertKey.Click += new System.EventHandler(this.btnInsertKey_Click);
            // 
            // btnValKey
            // 
            this.btnValKey.Location = new System.Drawing.Point(430, 8);
            this.btnValKey.Margin = new System.Windows.Forms.Padding(2);
            this.btnValKey.Name = "btnValKey";
            this.btnValKey.Size = new System.Drawing.Size(126, 22);
            this.btnValKey.TabIndex = 6;
            this.btnValKey.Text = "Validate Key";
            this.btnValKey.UseVisualStyleBackColor = true;
            this.btnValKey.Click += new System.EventHandler(this.btnValKey_Click);
            // 
            // CPUKeyGenGUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(694, 38);
            this.Controls.Add(this.btnValKey);
            this.Controls.Add(this.btnInsertKey);
            this.Controls.Add(this.btnGenKey);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtGenKey);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CPUKeyGenGUI";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "CPU Key Generation";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtGenKey;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnGenKey;
        private System.Windows.Forms.Button btnInsertKey;
        private System.Windows.Forms.Button btnValKey;
    }
}