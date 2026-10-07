using System;
using System.Collections;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace JRunner.Forms
{
    public partial class CPUKeyGenGUI : Form
    {
        public CPUKeyGenGUI()
        {
            InitializeComponent();
        }

        private void txtGenKey_TextChanged(object sender, EventArgs e)
        {
            if (txtGenKey.TextLength == 32) btnInsertKey.Enabled = true;
            else btnInsertKey.Enabled = false;
        }

        private void btnGenKey_Click(object sender, EventArgs e)
        {
            byte[] prefixBytes = null;

            // Accept up to 13 bytes as a prefix for the generated CPU key
            if (txtGenKey.TextLength > 0 && txtGenKey.TextLength < 13 * 2)
            {
                try
                {
                    prefixBytes = Oper.StringToByteArrayPrefix(txtGenKey.Text);
                }
                catch
                {
                    if (variables.debugMode) Console.WriteLine("CPU Key Generator: couldn't convert prefix bytes to hex string");
                }
            }

            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                txtGenKey.Text = variables.superDevKey;
            }
            else
            {
                txtGenKey.Text = CpuKeyGen.GenerateKey(prefixBytes);
            }
        }

        private void btnInsertKey_Click(object sender, EventArgs e)
        {
            MainForm.mainForm.updateCpuKeyText(txtGenKey.Text);
            this.Close();
        }

        private void btnValKey_Click(object sender, EventArgs e)
        {
            byte[] keyBytes;

            try
            {
                keyBytes = Oper.StringToByteArray(txtGenKey.Text);
            }
            catch
            {
                MessageBox.Show("CPU Key contains invalid characters.", "Validate CPU Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInsertKey.Enabled = false;
                return;
            }

            if (Nand.Nand.VerifyKey(keyBytes))
            {
                MessageBox.Show("CPU Key is valid!", "Validate CPU Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                btnInsertKey.Enabled = true;
            }
            else
            {
                MessageBox.Show("CPU Key is invalid", "Validate CPU Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInsertKey.Enabled = false;
            }
        }
    }
}
