using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JRunner.Forms
{
    public partial class EnterCPUKey : Form
    {
        public EnterCPUKey()
        {
            InitializeComponent();
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (false == Nand.Nand.VerifyKey(Oper.StringToByteArray(txtCpuKey.Text)))
            {
                DialogResult dr = MessageBox.Show("WARNING: Physical CPU key did not pass verification! Unless you've intentionally blown fuselines 3, 4, 5, or 6, you've likely made a typo. Continue?",
                                                  "CPU Key Verification Failed",
                                                  MessageBoxButtons.YesNo,
                                                  MessageBoxIcon.Warning);

                if (dr == DialogResult.Yes)
                {
                    cpukey = txtCpuKey.Text;
                    this.DialogResult = DialogResult.OK;
                }
            }
        }

        private void lblPhysicalCpuKey_DoubleClick(object sender, EventArgs e)
        {
            // Double click on the label to set the CPU Key for the FFFFFalcon
            txtCpuKey.Text = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
        }

        public string cpukey { get; set; }
    }
}
