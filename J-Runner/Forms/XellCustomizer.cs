using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Net.Configuration;

namespace JRunner
{
    public partial class XellCustomizer : Form
    {
        string _flashFilePath;
        bool flashHasEcc;
        byte[] flashData;

        int pagesz = 0x200;
        int pagesz_phys = 0x210;
        int blockType = 0;

        // This is the default "White on Blue" XeLL theme
        Color bgcolor = Color.FromArgb(78, 68, 216), fgcolor = Color.White;

        private void setColor()
        {
            try
            {
                txtXellPreview.BackColor = bgcolor;
                txtXellPreview.ForeColor = fgcolor;
            }
            catch
            {
                txtXellPreview.BackColor = bgcolor = Color.FromArgb(78, 68, 216);
                txtXellPreview.ForeColor = fgcolor = Color.White;
            }
        }

        public XellCustomizer()
        {
            InitializeComponent();
        }

        public DialogResult InitializeAndShowDialog(string flashFilePath)
        {
            _flashFilePath = flashFilePath;

            try
            {
                // Read the image
                flashData = File.ReadAllBytes(flashFilePath);
            }
            catch
            {
                Console.WriteLine("Customize XeLL theme error: unable to read input file.");
                return DialogResult.Cancel;
            }

            // Determine whether this image has ECC
            if (flashData.Length == 17301504 || flashData.Length == 69206016 || flashData.Length == 1351680)
            {
                flashHasEcc = true;
            }
            else if (flashData.Length == 50331648 || flashData.Length == 1310720)
            {
                // Flash data doesn't have ECC, pagesz_phys = pagesz
                flashHasEcc = false;
                pagesz_phys = pagesz;
            }
            else
            {
                Console.WriteLine("Customize XeLL theme error: invalid image size.");
                return DialogResult.Cancel;
            }

            // If the flash has ECC data, determine the block type so ECC data can be recalculated
            if (flashHasEcc)
            {
                byte[] sparedata = flashData.Skip(0x4400).Take(0x10).ToArray();

                // Block Types
                // 0 = Small block NAND (XSB)
                // 1 = Small block NAND on BB controller (PSB/KSB)
                // 2 = Big block NAND on BB controller (PSB/KSB)
                blockType = Nand.Nand.identifylayout(sparedata);
            }

            if (0 != flashData[0x5F])
            {
                bgcolor = Color.FromArgb(BitConverter.ToInt32(flashData.Skip(0x50).Take(0x4).ToArray(), 0));
                fgcolor = Color.FromArgb(BitConverter.ToInt32(flashData.Skip(0x54).Take(0x4).ToArray(), 0));
                chkEnableColours.Checked = true;
            }

            txtCopyrightString.Text = Encoding.ASCII.GetString(flashData.Skip(0x12).Take(0x36).ToArray());

            return this.ShowDialog();
        }

        private void btnSelectBgcolor_Click(object sender, EventArgs e)
        {
            xellColorDialog.ShowDialog();
            bgcolor = xellColorDialog.Color;
            setColor();
        }

        private void btnSelectFgcolor_Click(object sender, EventArgs e)
        {
            xellColorDialog.ShowDialog();
            fgcolor = xellColorDialog.Color;
            setColor();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            byte[] flashFirstPage = flashData.Take(pagesz_phys).ToArray();

            // If the flash has ECC data, determine the block type so ECC data can be recalculated
            if (flashHasEcc)
            {
                flashFirstPage = Nand.Nand.unecc(flashFirstPage);
            }

            // Set the colour
            if (chkEnableColours.Checked)
            {
                flashFirstPage[0x5F] = 0x1;

                byte[] bgbytes = BitConverter.GetBytes(bgcolor.ToArgb());
                byte[] fgbytes = BitConverter.GetBytes(fgcolor.ToArgb());

                Buffer.BlockCopy(bgbytes, 0, flashFirstPage, 0x50, 0x4);
                Buffer.BlockCopy(fgbytes, 0, flashFirstPage, 0x54, 0x4);
            }
            else
            {
                flashFirstPage[0x5F] = 0x0;
            }

            // Set the copyright string, we're going to clip the string at 0x47
            // so it isn't any longer than the stock NAND copyright string
            byte[] copyrightStringBytes = Encoding.ASCII.GetBytes(txtCopyrightString.Text);
            Array.Resize(ref copyrightStringBytes, 0x36);
            Buffer.BlockCopy(copyrightStringBytes, 0, flashFirstPage, 0x12, 0x36);
            flashFirstPage[0x47] = 0x0;

            if (flashHasEcc)
            {
                flashFirstPage = Nand.Nand.addecc_v2(flashFirstPage, true, 0, blockType);
            }

            Buffer.BlockCopy(flashFirstPage, 0, flashData, 0, pagesz_phys);

            File.WriteAllBytes(_flashFilePath, flashData);
        }

        private void chkEnableColours_CheckedChanged(object sender, EventArgs e)
        {
            btnSelectBgcolor.Enabled = chkEnableColours.Checked;
            btnSelectFgcolor.Enabled = chkEnableColours.Checked;

            if (chkEnableColours.Checked)
            {
                setColor();
            }
            else
            {
                txtXellPreview.ForeColor = Color.White;
                txtXellPreview.BackColor = Color.FromArgb(78, 68, 216);
            }
        }
    }
}
