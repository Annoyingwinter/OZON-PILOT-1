using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Web;
using System.Windows.Forms;

namespace LitchiAutoUpdate
{
    public sealed class MainForm : Form
    {
        private readonly string _apiUrl;
        private Label _titleLabel;
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private TextBox _notesBox;

        public MainForm(string apiUrl)
        {
            _apiUrl = apiUrl;
            InitializeControls();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Thread worker = new Thread(RunUpdate);
            worker.IsBackground = true;
            worker.Start();
        }

        private void InitializeControls()
        {
            Text = "Auto Update";
            Width = 460;
            Height = 300;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;

            _titleLabel = new Label();
            _titleLabel.Left = 12;
            _titleLabel.Top = 16;
            _titleLabel.Width = 420;
            _titleLabel.Text = "Checking update...";

            _progressBar = new ProgressBar();
            _progressBar.Left = 12;
            _progressBar.Top = 48;
            _progressBar.Width = 420;
            _progressBar.Height = 28;

            _statusLabel = new Label();
            _statusLabel.Left = 12;
            _statusLabel.Top = 84;
            _statusLabel.Width = 420;
            _statusLabel.Text = "Waiting...";

            _notesBox = new TextBox();
            _notesBox.Left = 12;
            _notesBox.Top = 112;
            _notesBox.Width = 420;
            _notesBox.Height = 130;
            _notesBox.Multiline = true;
            _notesBox.ScrollBars = ScrollBars.Vertical;
            _notesBox.ReadOnly = true;

            Controls.Add(_titleLabel);
            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
            Controls.Add(_notesBox);
        }

        private void RunUpdate()
        {
            try
            {
                string manifest = HttpHelper.LoadUpdateManifest(_apiUrl);
                string[] parts = manifest.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 4)
                {
                    ShowError("Invalid update manifest.");
                    return;
                }

                string version = parts[0];
                string processName = parts[1];
                string zipUrl = parts[2];
                string notes = HttpUtility.UrlDecode(parts[3]).Replace("\\n", Environment.NewLine);

                SafeInvoke(delegate
                {
                    _titleLabel.Text = "Latest version: " + version;
                    _notesBox.Text = notes;
                });

                KillRunningProcess(processName);

                string zipPath = Path.Combine(Application.StartupPath, "update.zip");
                DownloadFile(zipUrl, zipPath);

                SafeInvoke(delegate { _statusLabel.Text = "Extracting update..."; });
                ZipExtractor.Extract(zipPath, Application.StartupPath);
                File.Delete(zipPath);

                string mainPath = Path.Combine(Application.StartupPath, processName);
                if (File.Exists(mainPath))
                {
                    Process.Start(new ProcessStartInfo(mainPath) { UseShellExecute = false });
                }

                SafeInvoke(delegate { _statusLabel.Text = "Update complete."; });
                Thread.Sleep(800);
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                ShowError(ex.ToString());
            }
        }

        private void DownloadFile(string url, string targetFile)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = null;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (FileStream output = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
            {
                int total = (int)response.ContentLength;
                if (total > 0)
                {
                    SafeInvoke(delegate
                    {
                        _progressBar.Minimum = 0;
                        _progressBar.Maximum = total;
                    });
                }

                byte[] buffer = new byte[8192];
                int read;
                int current = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    current += read;
                    int progress = current;
                    SafeInvoke(delegate
                    {
                        if (progress <= _progressBar.Maximum)
                        {
                            _progressBar.Value = progress;
                        }

                        if (total > 0)
                        {
                            _statusLabel.Text = string.Format("Downloading... {0:0.00}%", progress * 100d / total);
                        }
                    });
                }
            }
        }

        private static void KillRunningProcess(string processNameWithExtension)
        {
            string processFileName = processNameWithExtension;
            string matchName = processFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processFileName.Substring(0, processFileName.Length - 4)
                : processFileName;

            Process[] processes = Process.GetProcessesByName(matchName);
            int i;
            for (i = 0; i < processes.Length; i++)
            {
                try
                {
                    processes[i].Kill();
                }
                catch
                {
                }
            }
        }

        private void ShowError(string message)
        {
            SafeInvoke(delegate
            {
                MessageBox.Show(this, message, "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }

        private void SafeInvoke(MethodInvoker action)
        {
            if (InvokeRequired)
            {
                Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
