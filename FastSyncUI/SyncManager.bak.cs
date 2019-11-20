//#define USE_DEPOT_FILE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows;
using System.Collections.ObjectModel;

namespace FastSyncUI
{
    public class SyncManager
    {
        private const int processThreshold = 10;
        private const int filesThreshold = 500;

        private MainWindow parent;
        private Queue<string> syncFolders;
        private List<Process> processes;
        private List<BackgroundWorker> workers;
        private object processMutex;
        private bool processingFoldersDone;

        private ObservableCollection<string> runningList;
        private ObservableCollection<string> pendingList;
        private ObservableCollection<string> completedList;
        private ObservableCollection<string> totalList;

        public SyncManager(MainWindow wnd)
        {
            parent = wnd;
            syncFolders = new Queue<string>();
            processes = new List<Process>();
            workers = new List<BackgroundWorker>();
            processMutex = new object();

            runningList = new ObservableCollection<string>();
            pendingList = new ObservableCollection<string>();
            completedList = new ObservableCollection<string>();
            totalList = new ObservableCollection<string>();

            parent.c_sync_list_running.ItemsSource = runningList;
            parent.c_sync_list_pending.ItemsSource = pendingList;
            parent.c_sync_list_completed.ItemsSource = completedList;
            parent.c_sync_list_total.ItemsSource = totalList;
        }

        public void Stop()
        {
            lock (processMutex)
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception exp)
                    {
                        Debug.Assert(false, "Failed to kill process - " + process.StartInfo.Arguments + "\n" + exp.Message);
                    }
                }
            }

            syncFolders.Clear();
        }

        public void Start()
        {
            Stop();

            parent.UpdateSyncDetails(-1);

            runningList.Clear();
            pendingList.Clear();
            completedList.Clear();
            totalList.Clear();

            processingFoldersDone = false;

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.WorkerSupportsCancellation = false;
            worker.DoWork += new DoWorkEventHandler(InitiateSync_DoWork);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(InitiateSync_Complete);

            runningList.Add("Gathering sync list ...");
            worker.RunWorkerAsync();
        }

        private void InitiateSync_DoWork(object sender, DoWorkEventArgs workArgs)
        {
            string cmd = "sync -n ";
            cmd += (SyncOptions.ForceSync) ? "-f " : "";
            cmd += "./...";
            cmd += (SyncOptions.CLNumber != 0) ? ("@" + SyncOptions.CLNumber) : "";

            MyExecute exec = new MyExecute("sd.exe", cmd, false, true);

            lock (processMutex)
            {
                processes.Add(exec.ProcessHandle);
            }

            bool retval = exec.Run();

            lock (processMutex)
            {
                processes.Remove(exec.ProcessHandle);
            }

            if (retval == true)
                workArgs.Result = exec.Output;
        }

        private void InitiateSync_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Debug.Assert(false, "Failed to get sync list. \n" + e.Error.Message);
                return;
            }
            
            if (string.IsNullOrEmpty(e.Result as string))
            {
                MessageBox.Show("Failed to get sync list. Probably nothing to sync");
                return;
            }

            string[] delim = new string[1];
            delim[0] = "\r\n";
            string[] syncFiles = (e.Result as string).Split(delim, StringSplitOptions.RemoveEmptyEntries);

            parent.UpdateSyncDetails(syncFiles.Length);
            runningList.Clear(); 

            if (syncFiles.Length > filesThreshold)
            {
                List<string> syncList = new List<string>();
                foreach (string file in syncFiles)
                {
#if USE_DEPOT_FILE
                    int index = file.IndexOf('#');
                    string depotFile = (index > 0) ? file.Remove(index) : file;
                    syncList.Add(depotFile.ToLower());
#else
                    int index = file.IndexOf('#');
                    index = file.IndexOf(':', index);
                    index = (index > 0) ? (index - 1) : 0;
                    string localFile = file.Substring(index);
                    syncList.Add(localFile.ToLower());
#endif
                }

#if USE_DEPOT_FILE
                ProcessFileList(syncList, "//depot/");
#else
                ProcessFileList(syncList, SyncOptions.ClientRoot + "\\");
#endif
            }
            else if (syncFiles.Length > 0)
            {
                SyncFolder("./...");
            }
            else
            {
                MessageBox.Show("Nothing to sync");
                parent.IsSyncing = false;
            }

            processingFoldersDone = true;
        }

        private void ProcessFileList(List<string> syncList, string currentFolder)
        {
            if (syncList.Count <= 0)
                return;

            Dictionary<string, List<string>> map = SplitList(syncList, currentFolder);
            foreach (string key in map.Keys)
            {
                List<string> subFiles = map[key];
                if (key == "")
                {
                    if ((subFiles != null) && (subFiles.Count > 0))
                        SyncFolder(currentFolder + "*");
                }
                else
                {
                    if ((subFiles != null) && (subFiles.Count > 0))
                    {
                        if (subFiles.Count > filesThreshold)
                        {
#if USE_DEPOT_FILE
                            ProcessFileList(subFiles, currentFolder + key + "/");
#else
                            ProcessFileList(subFiles, currentFolder + key + "\\");
#endif
                        }
                        else
                        {
#if USE_DEPOT_FILE
                            SyncFolder(currentFolder + key + "/...");
#else
                            SyncFolder(currentFolder + key + "\\...");
#endif
                        }
                    }
                    else
                    {
                        Debug.Assert(false, "List cannot be empty as a map entry for this exists. Parent=" + currentFolder + ", Child=" + key);
                    }
                }
            }
        }

        private Dictionary<string, List<string>> SplitList(List<string> fileList, string folder)
        {
            Dictionary<string, List<string>> retval = new Dictionary<string, List<string>>();

            string lastSearchString = null;
            List<string> lastSearchList = null;

            foreach (string fullPath in fileList)
            {
                string subPath = fullPath.Replace(folder, "");

#if USE_DEPOT_FILE
                string[] pathComponents = subPath.Split('/');
#else
                string[] pathComponents = subPath.Split('\\');
#endif

                string subFolder = null;
                if (pathComponents.Length == 1)
                {
                    //this is a file
                    subFolder = "";
                }
                else
                {
                    subFolder = pathComponents[0];
                }

                if ((lastSearchString == null) || (lastSearchString != subFolder))
                {
                    List<string> files = null;
                    if (retval.ContainsKey(subFolder))
                    {
                        files = retval[subFolder];
                    }
                    else
                    {
                        files = new List<string>();
                        retval[subFolder] = files;
                    }

                    lastSearchString = subFolder;
                    lastSearchList = files;
                }

                lastSearchList.Add(fullPath);
            }

            return retval;
        }

        private void SyncFolder(string path)
        {
            pendingList.Add(path);
            totalList.Add(path);
            syncFolders.Enqueue(path);
            StartNewSync();
        }

        private void StartNewSync()
        {
            lock (processMutex)
            {
                if (syncFolders.Count <= 0)
                {
                    if ((processingFoldersDone) && (workers.Count == 0))
                    {
                        parent.IsSyncing = false;
                        MessageBox.Show("Done syncing");
                    }

                    return;
                }

                if (workers.Count < processThreshold)
                {
                    string folder = syncFolders.Dequeue();

                    BackgroundWorker worker = new BackgroundWorker();
                    worker.WorkerReportsProgress = false;
                    worker.WorkerSupportsCancellation = false;
                    worker.DoWork += new DoWorkEventHandler(FolderSync_DoWork);
                    worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FolderSync_Complete);

                    runningList.Add(folder);
                    pendingList.Remove(folder);

                    workers.Add(worker);
                    worker.RunWorkerAsync(folder);
                }
            }
        }

        private void FolderSync_DoWork(object sender, DoWorkEventArgs workArgs)
        {
            string path = workArgs.Argument as string;
            workArgs.Result = path;
            
            string cmd = "sync ";
            cmd += (SyncOptions.ForceSync) ? "-f " : "";
            cmd += "\"" + path + "\"";
            cmd += (SyncOptions.CLNumber != 0) ? ("@" + SyncOptions.CLNumber) : "";

            //We will never redirect output as it lowers the performance.
            MyExecute exec = new MyExecute("sd.exe", cmd, SyncOptions.ShowWindows, false);

            /* Don't add the data to the UI as it slows down the process
            if (redirectOutput)
            {
                exec.OnOutputAvailable += new ExecuteOutputAvailable(exec_OnOutputAvailable);
                exec.OnErrorAvailable += new ExecuteOutputAvailable(exec_OnErrorAvailable);
            }
            */

            lock (processMutex)
            {
                processes.Add(exec.ProcessHandle);
            }

            bool retval = exec.Run();

            lock (processMutex)
            {
                processes.Remove(exec.ProcessHandle);
            }

            if (retval == false)
                throw (new Exception("Execution failed"));
        }

        private void FolderSync_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            string path = e.Result as string;
            if (e.Error != null)
            {
                Debug.Assert(false, e.Error.Message);
                UpdateError("Failed to sync folder - " + path + "\n" + e.Error.Message);
            }

            runningList.Remove(path);
            completedList.Add(path);

            workers.Remove(sender as BackgroundWorker);

            //start a new sync when an existing one gets completed
            StartNewSync();
        }

        private void exec_OnOutputAvailable(MyExecute sender, string data)
        {
            App.Current.Dispatcher.BeginInvoke(new UpdateLogFunc(UpdateOutput), data);
        }

        void exec_OnErrorAvailable(MyExecute sender, string data)
        {
            App.Current.Dispatcher.BeginInvoke(new UpdateLogFunc(UpdateError), data);
        }

        private delegate void UpdateLogFunc(string output);
        private void UpdateOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;

            try
            {
                parent.c_sync_output.Items.Add(output);
                parent.c_sync_output.ScrollIntoView(output);

                string line = output.ToLower();
                if (!(line.Contains(" - updating " + SyncOptions.ClientRoot) ||
                      line.Contains(" - added as " + SyncOptions.ClientRoot) ||
                      line.Contains(" - deleted as " + SyncOptions.ClientRoot) ||
                      line.Contains(" - refreshing " + SyncOptions.ClientRoot)))
                {
                    UpdateError(output);
                }
            }
            catch (Exception exp)
            {
                MessageBox.Show("Exception : " + exp.Message);
            }
        }

        private void UpdateError(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;

            try
            {
                parent.c_sync_error.Items.Add(output);
                parent.c_sync_error.ScrollIntoView(output);
            }
            catch (Exception exp)
            {
                MessageBox.Show("Exception : " + exp.Message);
            }
        }
    }
}
