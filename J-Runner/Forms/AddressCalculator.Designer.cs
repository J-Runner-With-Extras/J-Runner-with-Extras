namespace JRunner.Forms
{
    partial class AddressCalculator
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AddressCalculator));
            this.txtAddress = new System.Windows.Forms.TextBox();
            this.lblAddress = new System.Windows.Forms.Label();
            this.btnLogicalToPhysical = new System.Windows.Forms.Button();
            this.btnPhysicalToLogical = new System.Windows.Forms.Button();
            this.lblLogicalAddress = new System.Windows.Forms.Label();
            this.txtLogicalAddress = new System.Windows.Forms.TextBox();
            this.lblPhysicalAddress = new System.Windows.Forms.Label();
            this.txtPhysicalAddress = new System.Windows.Forms.TextBox();
            this.lblPageNumber = new System.Windows.Forms.Label();
            this.txtPageNumber = new System.Windows.Forms.TextBox();
            this.lblOffsetInPage = new System.Windows.Forms.Label();
            this.txtOffsetInPage = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // txtAddress
            // 
            this.txtAddress.Location = new System.Drawing.Point(12, 25);
            this.txtAddress.Name = "txtAddress";
            this.txtAddress.Size = new System.Drawing.Size(217, 20);
            this.txtAddress.TabIndex = 0;
            // 
            // lblAddress
            // 
            this.lblAddress.AutoSize = true;
            this.lblAddress.Location = new System.Drawing.Point(12, 9);
            this.lblAddress.Name = "lblAddress";
            this.lblAddress.Size = new System.Drawing.Size(45, 13);
            this.lblAddress.TabIndex = 1;
            this.lblAddress.Text = "Address";
            // 
            // btnLogicalToPhysical
            // 
            this.btnLogicalToPhysical.Location = new System.Drawing.Point(12, 59);
            this.btnLogicalToPhysical.Name = "btnLogicalToPhysical";
            this.btnLogicalToPhysical.Size = new System.Drawing.Size(217, 23);
            this.btnLogicalToPhysical.TabIndex = 2;
            this.btnLogicalToPhysical.Text = "Convert Logical to Physical";
            this.btnLogicalToPhysical.UseVisualStyleBackColor = true;
            this.btnLogicalToPhysical.Click += new System.EventHandler(this.btnLogicalToPhysical_Click);
            // 
            // btnPhysicalToLogical
            // 
            this.btnPhysicalToLogical.Location = new System.Drawing.Point(12, 88);
            this.btnPhysicalToLogical.Name = "btnPhysicalToLogical";
            this.btnPhysicalToLogical.Size = new System.Drawing.Size(217, 23);
            this.btnPhysicalToLogical.TabIndex = 3;
            this.btnPhysicalToLogical.Text = "Convert Physical to Logical";
            this.btnPhysicalToLogical.UseVisualStyleBackColor = true;
            this.btnPhysicalToLogical.Click += new System.EventHandler(this.btnPhysicalToLogical_Click);
            // 
            // lblLogicalAddress
            // 
            this.lblLogicalAddress.AutoSize = true;
            this.lblLogicalAddress.Location = new System.Drawing.Point(12, 129);
            this.lblLogicalAddress.Name = "lblLogicalAddress";
            this.lblLogicalAddress.Size = new System.Drawing.Size(144, 13);
            this.lblLogicalAddress.TabIndex = 5;
            this.lblLogicalAddress.Text = "Logical Address (No SPARE)";
            // 
            // txtLogicalAddress
            // 
            this.txtLogicalAddress.Location = new System.Drawing.Point(12, 145);
            this.txtLogicalAddress.Name = "txtLogicalAddress";
            this.txtLogicalAddress.ReadOnly = true;
            this.txtLogicalAddress.Size = new System.Drawing.Size(217, 20);
            this.txtLogicalAddress.TabIndex = 4;
            this.txtLogicalAddress.DoubleClick += new System.EventHandler(this.resultTextBox_DoubleClick);
            // 
            // lblPhysicalAddress
            // 
            this.lblPhysicalAddress.AutoSize = true;
            this.lblPhysicalAddress.Location = new System.Drawing.Point(12, 168);
            this.lblPhysicalAddress.Name = "lblPhysicalAddress";
            this.lblPhysicalAddress.Size = new System.Drawing.Size(148, 13);
            this.lblPhysicalAddress.TabIndex = 7;
            this.lblPhysicalAddress.Text = "Physical Address (w/ SPARE)";
            // 
            // txtPhysicalAddress
            // 
            this.txtPhysicalAddress.Location = new System.Drawing.Point(12, 184);
            this.txtPhysicalAddress.Name = "txtPhysicalAddress";
            this.txtPhysicalAddress.ReadOnly = true;
            this.txtPhysicalAddress.Size = new System.Drawing.Size(217, 20);
            this.txtPhysicalAddress.TabIndex = 6;
            this.txtPhysicalAddress.DoubleClick += new System.EventHandler(this.resultTextBox_DoubleClick);
            // 
            // lblPageNumber
            // 
            this.lblPageNumber.AutoSize = true;
            this.lblPageNumber.Location = new System.Drawing.Point(12, 209);
            this.lblPageNumber.Name = "lblPageNumber";
            this.lblPageNumber.Size = new System.Drawing.Size(72, 13);
            this.lblPageNumber.TabIndex = 9;
            this.lblPageNumber.Text = "Page Number";
            // 
            // txtPageNumber
            // 
            this.txtPageNumber.Location = new System.Drawing.Point(12, 225);
            this.txtPageNumber.Name = "txtPageNumber";
            this.txtPageNumber.ReadOnly = true;
            this.txtPageNumber.Size = new System.Drawing.Size(217, 20);
            this.txtPageNumber.TabIndex = 8;
            this.txtPageNumber.DoubleClick += new System.EventHandler(this.resultTextBox_DoubleClick);
            // 
            // lblOffsetInPage
            // 
            this.lblOffsetInPage.AutoSize = true;
            this.lblOffsetInPage.Location = new System.Drawing.Point(12, 248);
            this.lblOffsetInPage.Name = "lblOffsetInPage";
            this.lblOffsetInPage.Size = new System.Drawing.Size(73, 13);
            this.lblOffsetInPage.TabIndex = 11;
            this.lblOffsetInPage.Text = "Offset in page";
            // 
            // txtOffsetInPage
            // 
            this.txtOffsetInPage.Location = new System.Drawing.Point(12, 264);
            this.txtOffsetInPage.Name = "txtOffsetInPage";
            this.txtOffsetInPage.ReadOnly = true;
            this.txtOffsetInPage.Size = new System.Drawing.Size(217, 20);
            this.txtOffsetInPage.TabIndex = 10;
            this.txtOffsetInPage.DoubleClick += new System.EventHandler(this.resultTextBox_DoubleClick);
            // 
            // AddressCalculator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(246, 298);
            this.Controls.Add(this.lblOffsetInPage);
            this.Controls.Add(this.txtOffsetInPage);
            this.Controls.Add(this.lblPageNumber);
            this.Controls.Add(this.txtPageNumber);
            this.Controls.Add(this.lblPhysicalAddress);
            this.Controls.Add(this.txtPhysicalAddress);
            this.Controls.Add(this.lblLogicalAddress);
            this.Controls.Add(this.txtLogicalAddress);
            this.Controls.Add(this.btnPhysicalToLogical);
            this.Controls.Add(this.btnLogicalToPhysical);
            this.Controls.Add(this.lblAddress);
            this.Controls.Add(this.txtAddress);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AddressCalculator";
            this.Text = "NAND Address Calculator";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtAddress;
        private System.Windows.Forms.Label lblAddress;
        private System.Windows.Forms.Button btnLogicalToPhysical;
        private System.Windows.Forms.Button btnPhysicalToLogical;
        private System.Windows.Forms.Label lblLogicalAddress;
        private System.Windows.Forms.TextBox txtLogicalAddress;
        private System.Windows.Forms.Label lblPhysicalAddress;
        private System.Windows.Forms.TextBox txtPhysicalAddress;
        private System.Windows.Forms.Label lblPageNumber;
        private System.Windows.Forms.TextBox txtPageNumber;
        private System.Windows.Forms.Label lblOffsetInPage;
        private System.Windows.Forms.TextBox txtOffsetInPage;
    }
}