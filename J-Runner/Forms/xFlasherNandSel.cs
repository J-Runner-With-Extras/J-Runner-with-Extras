using System;
using System.Windows.Forms;

namespace JRunner.Forms
{
    public partial class xFlasherNandSel : Form
    {
        public delegate void ClickedSize(int size);
        public event ClickedSize SizeClick;

        public xFlasherNandSel()
        {
            InitializeComponent();
        }

        private void btn16_Click(object sender, EventArgs e)
        {
            SizeClick(16);
            this.Close();
        }

        private void btn64_Click(object sender, EventArgs e)
        {
            SizeClick(64);
            this.Close();
        }

        private void btn256_Click(object sender, EventArgs e)
        {
            SizeClick(256);
            this.Close();
        }

        private void btn512_Click(object sender, EventArgs e)
        {
            SizeClick(512);
            this.Close();
        }

        private void btn1024_Click(object sender, EventArgs e)
        {
            SizeClick(1024);
            this.Close();
        }

        public void setGroups(int bb)
        {
            SmallBlockGroup.Enabled = bb <= 0;
            btn256.Enabled = bb == 0 || bb == 2;
            btn512.Enabled = bb == 0 || bb == 3;
            btn1024.Enabled = bb == 0 || bb == 4;
        }
    }
}
