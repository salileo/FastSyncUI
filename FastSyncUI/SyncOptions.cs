using System;
using System.Windows;
using System.ComponentModel;
using System.IO;

namespace FastSyncUI
{
    public class SyncOptions
    {
        public string ClientName { get; private set; }
        public string ClientRoot { get; private set; }
        public bool ForceSync { get; private set; }
        public bool ClobberWriteable { get; private set; }
        public bool AutoStart { get; private set; }
        public bool ShowWindows { get; private set; }
        public string SyncFolder { get; private set; }
        public string Revision { get; private set; }
        public int ProcessThreshold { get; private set; }
        public int FileThreshold { get; private set; }
        public int BatchSize { get; private set; }

        public SyncOptions()
        {
            ClientName = string.Empty;
            ClientRoot = string.Empty;
            ForceSync = false;
            ClobberWriteable = false;
            AutoStart = false;
            ShowWindows = false;
            SyncFolder = string.Empty;
            Revision = string.Empty;

            ProcessThreshold = 20;
            FileThreshold = 500;
            BatchSize = 50;
        }

        public void SetThresholds(string process, string files, string batchsize)
        {
            try
            {
                ProcessThreshold = Int32.Parse(process);
            }
            catch (Exception)
            {
                ProcessThreshold = 20;
                MainWindow parent = App.Current.MainWindow as MainWindow;
                parent.UpdateSyncDetails(-2, -2);
            }

            try
            {
                FileThreshold = Int32.Parse(files);
            }
            catch (Exception)
            {
                FileThreshold = 500;
                MainWindow parent = App.Current.MainWindow as MainWindow;
                parent.UpdateSyncDetails(-2, -2);
            }

            try
            {
                BatchSize = Int32.Parse(batchsize);
            }
            catch (Exception)
            {
                BatchSize = 50;
                MainWindow parent = App.Current.MainWindow as MainWindow;
                parent.UpdateSyncDetails(-2, -2);
            }
        }

        public void SetShowWindows(bool value)
        {
            ShowWindows = value;
        }

        public bool ProcessCommandLineArgs()
        {
            bool success = true;
            string[] args = Environment.GetCommandLineArgs();
            Int32 index = 1; //index 0 has the executable name
            while ((args != null) && (index < args.Length))
            {
                string current = args[index];
                switch (current)
                {
                    case "/?":
                    case "-?":
                        ShowHelp(null);
                        success = false;
                        break;
                    case "/f":
                    case "-f":
                        ForceSync = true;
                        break;
                    case "/w":
                    case "-w":
                        ClobberWriteable = true;
                        break;
                    case "/a":
                    case "-a":
                        AutoStart = true;
                        break;
                    case "/v":
                    case "-v":
                        ShowWindows = true;
                        break;
                    default:
                        if (string.IsNullOrEmpty(SyncFolder) && string.IsNullOrEmpty(Revision))
                        {
                            try
                            {
                                int delim_index = current.IndexOf('@');
                                if (delim_index != -1)
                                {
                                    Revision = current.Substring(delim_index + 1);
                                    SyncFolder = current.Substring(0, delim_index);
                                }
                                else
                                {
                                    SyncFolder = current;
                                }

                                if (SyncFolder.StartsWith("//"))
                                {
                                    ShowHelp("Please use local paths only for the sync.");
                                    success = false;
                                    break;
                                }

                                SyncFolder = SyncFolder.Replace("/", "\\");

                                if (SyncFolder.StartsWith("\\"))
                                {
                                    ShowHelp("Invalid sync path specified.");
                                    success = false;
                                    break;
                                }
                                else if (SyncFolder.Contains(":"))
                                {
                                    ShowHelp("Do not use absolute paths.");
                                    success = false;
                                    break;
                                }

                                if (SyncFolder == "..")
                                {
                                    SyncFolder = Path.GetDirectoryName(Environment.CurrentDirectory);
                                }
                                else if (SyncFolder.StartsWith("..\\"))
                                {
                                    string subPart = SyncFolder.Substring(3);
                                    SyncFolder = Path.GetDirectoryName(Environment.CurrentDirectory);
                                    
                                    if (!SyncFolder.EndsWith("\\"))
                                        SyncFolder += "\\";

                                    SyncFolder += subPart;
                                }
                                else if ((SyncFolder == ".") ||
                                         (SyncFolder == "..."))
                                {
                                    SyncFolder = Environment.CurrentDirectory;
                                }
                                else if (SyncFolder.StartsWith(".\\"))
                                {
                                    string subPart = SyncFolder.Substring(2);
                                    SyncFolder = Environment.CurrentDirectory;

                                    if (!SyncFolder.EndsWith("\\"))
                                        SyncFolder += "\\";

                                    SyncFolder += subPart;
                                }
                                else
                                {
                                    string subPart = SyncFolder;
                                    SyncFolder = Environment.CurrentDirectory;

                                    if (!SyncFolder.EndsWith("\\"))
                                        SyncFolder += "\\";

                                    SyncFolder += subPart;
                                }

                                if (!string.IsNullOrEmpty(SyncFolder) && !SyncFolder.EndsWith("..."))
                                {
                                    if (!SyncFolder.EndsWith("\\"))
                                        SyncFolder += "\\";
                                    
                                    SyncFolder += "...";
                                }

                                SyncFolder = SyncFolder.ToLower().Trim();
                            }
                            catch (Exception exp)
                            {
                                ShowHelp("Arguement - " + current + ". " + exp.Message);
                                success = false;
                            }
                        }
                        else
                        {
                            ShowHelp("Invalid arguement - " + current);
                            success = false;
                        }
                        break;
                }

                if (!success)
                    break;

                index++;
            }

            if (success)
            {
                MainWindow parent = App.Current.MainWindow as MainWindow;
                parent.UpdateSyncDetails(-2, -2);
                GetClientInfo();
            }

            return success;
        }

        public void ShowHelp(String error)
        {
            string msg = "";

            if (!string.IsNullOrEmpty(error))
                msg += "Error : " + error + "\n\n";

            msg += "Fastsync - Helps to perform a quick sync of the selected folder.\n\n";

            msg += "Usage:\n";
            msg += "- fastsync [option]\n";
            msg += "- fastsync [option] <folder>\n";
            msg += "- fastsync [option] <folder>@<cl_number>\n";
            msg += "Note - The folder selection is identical to the command line version of sd.exe\n";

            msg += "\nOptions:\n";
            msg += "-? = Help (this) menu.\n";
            msg += "-f = Force sync.\n";
            msg += "-w = Clobber writeable files.\n";
            msg += "-a = Auto start.\n";
            msg += "-v = Verbose (shows the cmd windows).\n";

            MessageBox.Show(App.Current.MainWindow, msg, "Help");
        }

        private void GetClientInfo()
        {
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.WorkerSupportsCancellation = false;
            worker.DoWork += new DoWorkEventHandler(ClientInfo_DoWork);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ClientInfo_Complete);
            worker.RunWorkerAsync();
        }

        private void ClientInfo_DoWork(object sender, DoWorkEventArgs workArgs)
        {
            string cmd = "info";
            MyExecute exec = new MyExecute("sd.exe", cmd, false, true, true);
            if (exec.Run() && !string.IsNullOrEmpty(exec.Output))
            {
                string[] delim = new string[1];
                delim[0] = "\r\n";
                string[] parts = exec.Output.Split(delim, StringSplitOptions.RemoveEmptyEntries);
                foreach (string entry in parts)
                {
                    if (entry.StartsWith("Client name:"))
                    {
                        ClientName = entry.Replace("Client name: ", "").ToLower();
                    }
                    else if (entry.StartsWith("Client root:"))
                    {
                        ClientRoot = entry.Replace("Client root: ", "").ToLower();
                    }
                }
            }
        }

        private void ClientInfo_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            MainWindow parent = App.Current.MainWindow as MainWindow;
            if (string.IsNullOrEmpty(ClientName) || string.IsNullOrEmpty(ClientRoot))
            {
                MessageBox.Show(parent, "Could not get the client details. Closing application.");
                parent.Close();
            }
            else
            {
                if (string.IsNullOrEmpty(SyncFolder))
                    SyncFolder = ClientRoot + "\\...";

                parent.UpdateSyncDetails(-2, -2);

                if (AutoStart)
                    parent.SyncButtonClick(null, null);
            }
        }
    }
}
