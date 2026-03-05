using System;
using System.Windows.Forms;

namespace TR_AoD_SaveEdit
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void AboutForm_Load(object sender, EventArgs e)
        {
            rtxInfo.Text = "A prototype savegame editor for Tomb Raider: The Angel of Darkness. To use, click 'Browse' and select " +
                "your game's root directory. If you have a Steam copy of the game, it should be located in the " +
                "'Tomb Raider (VI) The Angel of Darkness' subdirectory of your Steam folder under '\\steamapps\\common\\'." +
                Environment.NewLine + Environment.NewLine +
                "When patching a savegame, backups are automatically created in the savegame subdirectory with " +
                "'.bak' suffixed to the savegame file name.";
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void llbGitHub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/JulianOzelRose");
        }

        private void llbGitHub_MouseHover(object sender, EventArgs e)
        {
            if (sender is LinkLabel linkLabel)
            {
                ToolTip toolTip = new ToolTip();
                toolTip.InitialDelay = 500;
                toolTip.SetToolTip(linkLabel, "https://github.com/JulianOzelRose");
            }
        }
    }
}
