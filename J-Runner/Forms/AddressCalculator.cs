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
    public partial class AddressCalculator : Form
    {
        public AddressCalculator()
        {
            InitializeComponent();
        }

        private static int pagesz = 0x200;
        private static int pagesz_phys = 0x210;

        private void btnLogicalToPhysical_Click(object sender, EventArgs e)
        {
            int inputAddr = 0;

            try
            {
                inputAddr = Convert.ToInt32(txtAddress.Text, 16);
            }
            catch
            {
                clearOutputTextBoxesOnInputError();
                return;
            }

            int pageNumber = inputAddr / pagesz;
            int offsetInPage = inputAddr % pagesz;
            int addressPhys = (pageNumber * pagesz_phys) + offsetInPage;

            txtLogicalAddress.Text = "0x" + inputAddr.ToString("x");
            txtPhysicalAddress.Text = "0x" + addressPhys.ToString("x");
            txtPageNumber.Text = pageNumber.ToString();
            txtOffsetInPage.Text = "0x" + offsetInPage.ToString("x");
        }

        private void btnPhysicalToLogical_Click(object sender, EventArgs e)
        {
            int inputAddr = 0;

            try
            {
                inputAddr = Convert.ToInt32(txtAddress.Text, 16);
            }
            catch
            {
                clearOutputTextBoxesOnInputError();
                return;
            }

            int pageNumber = inputAddr / pagesz_phys;
            int offsetInPage = inputAddr % pagesz_phys;
            int addressLogical = (pageNumber * pagesz) + offsetInPage;

            txtLogicalAddress.Text = "0x" + addressLogical.ToString("x");
            txtPhysicalAddress.Text = "0x" + inputAddr.ToString("x");
            txtPageNumber.Text = pageNumber.ToString();
            txtOffsetInPage.Text = "0x" + offsetInPage.ToString("x");
        }

        private void clearOutputTextBoxesOnInputError()
        {
            txtLogicalAddress.Text = "Invalid Input Address";
            txtPhysicalAddress.Text = "";
            txtPageNumber.Text = "";
            txtOffsetInPage.Text = "";
        }

        private void resultTextBox_DoubleClick(object sender, EventArgs e)
        {
            Clipboard.SetText(((TextBox)sender).Text);
        }
    }
}
