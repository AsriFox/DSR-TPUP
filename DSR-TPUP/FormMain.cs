using DSR_TPUP.Core;
using System.Diagnostics;
using System.Media;
using TeximpNet.DDS;

namespace DSR_TPUP
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public partial class FormMain : Form
    {
        private static readonly string explorer = Environment.GetEnvironmentVariable("WINDIR") + @"\explorer.exe";
        private static readonly Properties.Settings settings = Properties.Settings.Default;

        private TPUP? tpup;
        private Thread? tpupThread;
        private bool abort = false;

        public FormMain()
        {
            InitializeComponent();
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            Text = "DSR Texture Packer & Unpacker " + Application.ProductVersion;

            /// TODO: use Microsoft.Extensions.Configuration package
            Location = settings.WindowLocation;
            Size = settings.WindowSize;
            if (settings.WindowMaximized)
                WindowState = FormWindowState.Maximized;

            txtGameDir.Text = settings.GameDir;
            txtUnpackDir.Text = settings.UnpackDir;
            txtRepackDir.Text = settings.RepackDir;
            cbxPreserveConverted.Checked = settings.PreserveConverted;
            txtConvertFile.Text = settings.ConvertFile;
            tclMain.SelectedIndex = settings.TabSelected;
            spcLogs.SplitterDistance = settings.SplitterDistance;

            nudThreads.Maximum = Environment.ProcessorCount;
            if (settings.Threads == 0 || settings.Threads > Environment.ProcessorCount)
                settings.Threads = Environment.ProcessorCount;
            nudThreads.Value = settings.Threads;

            enableControls(true);

            // Force common formats to the top
            foreach (DXGIFormat format in Main.DXGI_FORMATS_COMMON)
                cmbConvertFormat.Items.Add(new ConvertFormatItem(format));
            cmbConvertFormat.Items.Add("--------------------------------------------------");
            foreach (DXGIFormat format in Main.SortFormatsCustom())
                cmbConvertFormat.Items.Add(new ConvertFormatItem(format));
            cmbConvertFormat.SelectedIndex = 0;

            var release = await Main.CheckForUpdates(Application.ProductVersion);
            if (release == null)
            {
                lblUpdate.Text = "Update status unknown";
            }
            else if (release?.Item1 == false)
            {
                lblUpdate.Text = "App up to date";
            }
            else
            {
                lblUpdate.Visible = false;
                LinkLabel.Link link = new()
                {
                    LinkData = release?.Item2 ?? throw new NullReferenceException("Release link must not be null")
                };
                llbUpdate.Links.Add(link);
                llbUpdate.Visible = true;
            }
        }

        private void llbUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(e.Link?.LinkData?.ToString() ?? throw new NullReferenceException("Update link must not be null"));
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (tpupThread?.IsAlive ?? false)
            {
                if (tpup == null)
                    return;
                tpup.Stop();
                e.Cancel = true;
                abort = true;
            }
            else
            {
                settings.WindowMaximized = WindowState == FormWindowState.Maximized;
                if (WindowState == FormWindowState.Normal)
                {
                    settings.WindowLocation = Location;
                    settings.WindowSize = Size;
                }
                else
                {
                    settings.WindowLocation = RestoreBounds.Location;
                    settings.WindowSize = RestoreBounds.Size;
                }

                settings.GameDir = txtGameDir.Text;
                settings.UnpackDir = txtUnpackDir.Text;
                settings.RepackDir = txtRepackDir.Text;
                settings.PreserveConverted = cbxPreserveConverted.Checked;
                settings.ConvertFile = txtConvertFile.Text;
                settings.TabSelected = tclMain.SelectedIndex;
                settings.SplitterDistance = spcLogs.SplitterDistance;
                settings.Threads = (int)nudThreads.Value;
            }
        }

        private void btnGameBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select your game install directory";
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtGameDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            folderBrowserDialog1.ShowNewFolderButton = false;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtGameDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnGameExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtGameDir.Text))
                Process.Start(explorer, Path.GetFullPath(txtGameDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        #region Unpack
        private void btnUnpackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to unpack textures into";
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtUnpackDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            folderBrowserDialog1.ShowNewFolderButton = true;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtUnpackDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnUnpackExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtUnpackDir.Text))
                Process.Start(explorer, Path.GetFullPath(txtUnpackDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        private void btnUnpack_Click(object sender, EventArgs e)
        {
            string unpackDir;
            try
            {
                unpackDir = Path.GetFullPath(txtUnpackDir.Text);
            }
            catch (ArgumentException)
            {
                MessageBox.Show("Invalid output path:\n" + txtUnpackDir.Text,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!Directory.Exists(txtGameDir.Text))
            {
                MessageBox.Show("Game directory not found:\n" + txtGameDir.Text,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                DialogResult result = DialogResult.OK;
                if (Directory.Exists(txtUnpackDir.Text))
                {
                    result = MessageBox.Show("The contents of this directory will be deleted:\n" + unpackDir,
                        "Warning!", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
                }

                if (result == DialogResult.OK)
                {
                    bool proceed = true;

                    try
                    {
                        if (Directory.Exists(unpackDir))
                        {
                            appendLog("Deleting unpack directory...");
                            Directory.Delete(unpackDir, true);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        MessageBox.Show("Unpack directory could not be deleted. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        proceed = false;
                    }

                    try
                    {
                        if (proceed)
                        {
                            Directory.CreateDirectory(unpackDir);
                            File.WriteAllText(unpackDir + "\\tpup_test.txt",
                                "Test file to see if TPUP can write to this directory.");
                            File.Delete(unpackDir + "\\tpup_test.txt");
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        MessageBox.Show("Unpack directory could not be written to. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        proceed = false;
                    }

                    if (proceed)
                    {
                        enableControls(false);
                        txtLog.Clear();
                        txtError.Clear();
                        pbrProgress.Value = 0;
                        pbrProgress.Maximum = 0;
                        tpup = Main.Unpack(txtGameDir.Text, unpackDir, (int)nudThreads.Value);
                        tpupThread = new Thread(tpup.Start);
                        tpupThread.Start();
                    }
                }
            }
        }
        #endregion

        #region Repack
        private void btnRepackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to load texture overrides from";
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtRepackDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtRepackDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnRepackExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtRepackDir.Text))
                Process.Start(explorer, Path.GetFullPath(txtRepackDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        private void btnRepack_Click(object sender, EventArgs e)
        {
            string gameDir = txtGameDir.Text;
            string repackDir = txtRepackDir.Text;

            if (!Directory.Exists(gameDir))
            {
                MessageBox.Show("Game directory not found:\n" + gameDir,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (!Directory.Exists(repackDir))
            {
                MessageBox.Show("Override directory not found:\n" + repackDir,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                bool proceed = true;
                try
                {
                    File.WriteAllText(repackDir + "\\tpup_test.txt",
                        "Test file to see if TPUP can write to this directory.");
                    File.Delete(repackDir + "\\tpup_test.txt");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show("Repack directory could not be written to. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    proceed = false;
                }

                if (proceed)
                {
                    enableControls(false);
                    txtLog.Clear();
                    txtError.Clear();
                    pbrProgress.Value = 0;
                    pbrProgress.Maximum = 0;
                    tpup = Main.Repack(gameDir, repackDir, (int)nudThreads.Value, cbxPreserveConverted.Checked);
                    tpupThread = new Thread(tpup.Start);
                    tpupThread.Start();
                }
            }
        }
        #endregion

        #region Convert
        private void btnConvertBrowse_Click(object sender, EventArgs e)
        {
            try
            {
                openFileDialog1.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(txtConvertFile.Text));
            }
            // Oh well
            catch (ArgumentException) { }
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
                txtConvertFile.Text = openFileDialog1.FileName;
        }

        private void btnConvertExplore_Click(object sender, EventArgs e)
        {
            if (File.Exists(txtConvertFile.Text))
            {
                string convertDir = Path.GetDirectoryName(Path.GetFullPath(txtConvertFile.Text)) 
                    ?? throw new NullReferenceException("Directory to explore must not be null");
                Process.Start(explorer, convertDir);
            }
            else
                SystemSounds.Hand.Play();
        }

        private async void btnConvert_Click(object sender, EventArgs e)
        {
            try
            {
                if (cmbConvertFormat.SelectedItem is not ConvertFormatItem formatItem)
                    return;
                await Main.Convert(txtConvertFile.Text, formatItem.Format);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        private void btnAbort_Click(object sender, EventArgs e)
        {
            if (tpup == null)
                return;
            tpup.Stop();
            btnAbort.Enabled = false;
        }

        private async void btnRestore_Click(object sender, EventArgs e)
        {
            try
            {
                txtLog.Clear();
                txtError.Clear();
                uint found = await Task.Run(() => Main.Restore(txtGameDir.Text));
                if (found > 0)
                    appendLog(found + " backups restored.");
                else
                    appendLog("No backups found.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void updateLogs()
        {
            if (tpup == null)
                return;

            while (tpup.Log.TryDequeue(out string? line))
                appendLog(line);

            while (tpup.Error.TryDequeue(out string? line))
                appendError(line);

            if (pbrProgress.Maximum == 0)
            {
                pbrProgress.Maximum = tpup.GetProgressMax();
            }
            else
            {
                pbrProgress.Value = tpup.GetProgress();
                lblProgress.Text = string.Format("Progress ({0}/{1})", pbrProgress.Value, pbrProgress.Maximum);
            }
        }

        private void tmrCheckThread_Tick(object sender, EventArgs e)
        {
            if (tpupThread != null)
            {
                if (tpupThread.IsAlive)
                {
                    updateLogs();
                }
                else
                {
                    // Make sure to clear out any leftover messages
                    updateLogs();

                    tpup = null;
                    tpupThread = null;
                    pbrProgress.Maximum = 0;
                    pbrProgress.Value = 0;
                    lblProgress.Text = "Progress";
                    enableControls(true);

                    if (abort)
                        Close();
                    else
                        SystemSounds.Asterisk.Play();
                }
            }
        }

        private void cmbConvertFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Please don't select the separator
            if (cmbConvertFormat.SelectedItem is not ConvertFormatItem)
                cmbConvertFormat.SelectedIndex--;
        }

        private void enableControls(bool enable)
        {
            txtGameDir.Enabled = enable;
            btnGameBrowse.Enabled = enable;
            btnGameExplore.Enabled = enable;
            tclMain.Enabled = enable;
            nudThreads.Enabled = enable;
            btnAbort.Enabled = !enable;
            btnRestore.Enabled = enable;
        }

        private void appendLog(string line)
        {
            if (txtLog.TextLength > 0)
                txtLog.AppendText("\r\n" + line);
            else
                txtLog.AppendText(line);
        }

        private void appendError(string line)
        {
            if (txtError.TextLength > 0)
                txtError.AppendText("\r\n\r\n" + line);
            else
                txtError.AppendText(line);
        }

        private class ConvertFormatItem
        {
            public DXGIFormat Format;

            public ConvertFormatItem(DXGIFormat format)
            {
                Format = format;
            }

            public override string ToString()
            {
                return TPUP.PrintDXGIFormat(Format);
            }
        }
    }
}
