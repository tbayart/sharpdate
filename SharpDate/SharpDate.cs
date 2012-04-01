using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.Xml.Linq;
using System.Diagnostics;
using System.IO;

namespace SharpDate
{
    public partial class SharpDate : Form
    {

        #region Fields & Delegates

        private Version pLocalVersion;
        BackgroundWorker bwUpdateProgram = new BackgroundWorker();

        public delegate DownloadAction DownloadProgressDelegate(int percProgress);
        #endregion

        #region Enumerators
        public enum DownloadAction
        {
            Continue,
            Cancel
        }

        public enum BwResult
        {
            Success,
            Cancelled,
            Error
        }
        #endregion

        #region Properties

        public int[] ProcessesToKill { get; set; }
        public string MainExe { get; set; }
        public Version LocalVersion
        {
            get { return pLocalVersion; }
            set { pLocalVersion = value; }
        }
        public string[] ApiURLs { get; set; }
        public UpdateInfo CurrentUpdateInfo { get; set; }

        #endregion

        public class UpdateInfo
        {
            public string Name { get; set; }
            public Version Version { get; set; }
            public bool Beta { get; set; }
            public string Changelog { get; set; }
            public string DownloadURL { get; set; }
            public string CompleteDownloadURL { get; set; }
        }

        public SharpDate()
        {
            InitializeComponent();

            //Get command line args and save into the data

            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i <= args.Count() - 1; i++)
            {
                switch (args[i])
                {
                    case "-pids":
                        ProcessesToKill = args[i + 1].Split(',').Select(int.Parse).ToArray();
                        break;

                    case "-apiurls":
                        ApiURLs = args[i + 1].Split('|');
                        break;
                        
                    case "-mainexe":
                        MainExe = args[i + 1];
                        break;

                    case "-version":
                        if (!Version.TryParse(args[i + 1], out pLocalVersion))
                        {
                            //Failed to parse version, exit with error
                            MessageBox.Show("Invalid version string!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Environment.Exit(0);
                        }
                        break;
                }
            }
            if (ApiURLs == null || ProcessesToKill == null)
            {
                //Wrong arguments, notify and quit
                MessageBox.Show("Wrong arguments specified!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            bwUpdateProgram.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwUpdateProgram_RunWorkerCompleted);
            bwUpdateProgram.DoWork += new DoWorkEventHandler(bwUpdateProgram_DoWork);
            bwUpdateProgram.ProgressChanged += new ProgressChangedEventHandler(bwUpdateProgram_ProgressChanged);
            bwUpdateProgram.WorkerReportsProgress = true;
            bwUpdateProgram.WorkerSupportsCancellation = true;

            btnUpdate.Select();

            if (UpdateAvailable())
            {
                lblNewUpdate.Text = string.Format("Version {0} is available for download", CurrentUpdateInfo.Version.ToString());
                txtChangelog.Text = CurrentUpdateInfo.Changelog;
                btnUpdate.Text = "Update";
            }
            else
            {
                Environment.Exit(0);
            }
        }

        private bool UpdateAvailable()
        {
            if (LocalVersion == null)
            {
                if (File.Exists(MainExe))
                {
                    FileVersionInfo fi = FileVersionInfo.GetVersionInfo(MainExe);
                    LocalVersion = Version.Parse(fi.FileVersion);
                }
                else
                {
                    return false;
                }
            }
            CurrentUpdateInfo = GetUpdateInfo(ApiURLs);

            Version newVersion;

            if (CurrentUpdateInfo != null)
            {
                newVersion = CurrentUpdateInfo.Version;
            }
            else
            {
                MessageBox.Show("Failed to check for updates, maybe the update server is down?", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return newVersion > LocalVersion;
        }

        private void ExitPrograms(int[] pids)
        {
            for(int i = 0; i < pids.Count(); i++)
            {
                Process prc = Process.GetProcessById(pids[i]);
                prc.CloseMainWindow();
            }
        }

        private UpdateInfo GetUpdateInfo(string[] APIUrls)
        {
            WebClient client = new WebClient();
            string xml;

            try
            {
                xml = client.DownloadString(new Uri(APIUrls[0]));
            }
            catch (WebException)
            {
                //Failed to connect, try the other URL
                try
                {
                    xml = client.DownloadString(new Uri(APIUrls[1]));
                }
                catch (WebException)
                {
                    return null;
                }
            }
            XDocument xdoc = XDocument.Parse(xml);
            XElement xe = xdoc.Root.Element("program");

            UpdateInfo newUpdateInfo = new UpdateInfo();

            newUpdateInfo.Name = xe.Element("name").Value;
            newUpdateInfo.Version = Version.Parse(xe.Element("version") != null ? xe.Element("version").Value : "0");
            newUpdateInfo.Beta = xe.Element("beta") != null && xe.Element("beta").Value.Equals("1");
            newUpdateInfo.Changelog = xe.Element("changelog") != null ? xe.Element("changelog").Value : string.Empty;
            newUpdateInfo.DownloadURL = xe.Element("downloadURL") != null ? xe.Element("downloadURL").Value : string.Empty;
            newUpdateInfo.CompleteDownloadURL = xe.Element("completeDownloadURL") != null ? xe.Element("completeDownloadURL").Value : string.Empty;

            return newUpdateInfo;
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            bwUpdateProgram.RunWorkerAsync();
            btnUpdate.Enabled = false;
        }

        private DownloadAction downloadFileProgressChanged(int percentage)
        {
            bwUpdateProgram.ReportProgress(percentage);
            return bwUpdateProgram.CancellationPending ? DownloadAction.Cancel : DownloadAction.Continue;
        }

        /// <summary>
        /// Downloads the specified URL. Returns true if success.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="localPath">The local path.</param>
        /// <param name="progressDelegate">The progress delegate.</param>
        /// <returns></returns>
        public static bool Download(string url, string localPath, DownloadProgressDelegate progressDelegate)
        {
            long remoteSize;
            string fullLocalPath; // Full local path including file name if only directory was provided.

            try
            {
                // Get the name of the remote file.
                Uri remoteUri = new Uri(url);
                string fileName = Path.GetFileName(remoteUri.LocalPath);

                fullLocalPath = Path.GetFileName(localPath).Length == 0 ? Path.Combine(localPath, fileName) : localPath;

                // Have to get size of remote object through the webrequest as not available on remote files,
                // although it does work on local files.
                using (WebResponse response = WebRequest.Create(url).GetResponse())
                using (response.GetResponseStream()) remoteSize = response.ContentLength;

            }
            catch (Exception ex)
            {
                throw new ApplicationException(string.Format("Error connecting to URI (Exception={0})", ex.Message), ex);
            }

            int bytesReadTotal = 0;

            try
            {
                using (WebClient client = new WebClient())
                using (Stream streamRemote = client.OpenRead(new Uri(url)))
                using (Stream streamLocal = new FileStream(fullLocalPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] byteBuffer = new byte[1024 * 1024 * 2]; // 2 meg buffer although in testing only got to 10k max usage.
                    int perc = 0;
                    int bytesRead;
                    while ((bytesRead = streamRemote.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                    {
                        bytesReadTotal += bytesRead;
                        streamLocal.Write(byteBuffer, 0, bytesRead);
                        int newPerc = (int)(bytesReadTotal / (double)remoteSize * 100);
                        if (newPerc > perc)
                        {
                            perc = newPerc;
                            if (progressDelegate != null)
                            {
                                if (progressDelegate(perc) == DownloadAction.Cancel)
                                {
                                    streamLocal.Close();

                                    try
                                    {
                                        File.Delete(fullLocalPath);
                                    }
                                    catch (Exception)
                                    {
                                        return false;
                                    }

                                    return false;
                                }
                            }
                        }
                    }
                }

                progressDelegate(100);
                return true; //File succeeded downloading
            }
            catch (Exception ex)
            {
                throw new ApplicationException(string.Format("Error downloading file (Exception={0})", ex.Message), ex);
            }

        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (bwUpdateProgram.IsBusy)
            {
                bwUpdateProgram.CancelAsync();
            }
            else
            {
                Application.Exit();
            }
        }

        private void CleanUp(string dir)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception)
                {
                }
            }

            //Clean-up succeeded
        }

        private void SharpDate_FormClosing(Object sender, FormClosingEventArgs e)
        {
            if (bwUpdateProgram.IsBusy)
            {
                bwUpdateProgram.CancelAsync();
            }
            else
            {
                Application.Exit();
            }
        }

        #region Backgroundworker
        private void bwUpdateProgram_DoWork(object sender, DoWorkEventArgs e)
        {
            //Create workdir
            string workDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(workDir);
            }
            catch (Exception ex)
            {
                e.Result = new object[] { BwResult.Error, ex };
                return;
            }

            //Download the updatefile

            DownloadProgressDelegate progressUpdate = new DownloadProgressDelegate(downloadFileProgressChanged);
            bool result;

            try
            {
                string url;
                if (CurrentUpdateInfo.CompleteDownloadURL != string.Empty)
                {
                    url = CurrentUpdateInfo.CompleteDownloadURL;
                }
                else
                {
                    url = CurrentUpdateInfo.DownloadURL + CurrentUpdateInfo.Version + ".exe";
                }

                result = Download(url, Path.Combine(workDir, CurrentUpdateInfo.Version + ".exe"), progressUpdate);
            }
            catch (Exception ex)
            {
                e.Result = new object[] {workDir, BwResult.Error, ex };
                return;
            }

            if (result)
            {
                //Download succeeded, notify the user and start the setup
                e.Result = new object[] { workDir, BwResult.Success, Path.Combine(workDir, CurrentUpdateInfo.Version + ".exe") };
            }
            else
            {
                bwUpdateProgram.ReportProgress(99);
                //Something went wrong OR the user cancelled
                e.Result = new object[] {workDir, BwResult.Cancelled };
            }
        }

        private void bwUpdateProgram_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pbarDownloadProgress.Value = e.ProgressPercentage;
        }

        private void bwUpdateProgram_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            object[] result = (object[])e.Result;

            if ((BwResult)result[1] == BwResult.Success)
            {
                MessageBox.Show("The program will now be closed to prepare for the update, \r\nso please save all your work before proceeding!", "Update Downloaded Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ExitPrograms(ProcessesToKill);
                Process.Start((string)result[1]);
                Application.Exit();
            }
            else if ((BwResult)result[1] == BwResult.Error)
            {
                CleanUp((string)result[0]);
                MessageBox.Show("Error Occured: \r\n" + result[1], "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if ((BwResult)result[1] == BwResult.Cancelled)
            {
                CleanUp((string)result[0]);
                Application.Exit();
            }
        }
        #endregion
    }
}
