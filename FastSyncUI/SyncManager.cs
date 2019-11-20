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
        private class Folder
        {
            public string Name { get; set; }
            public int Count { get; set; }
            public bool Optimize { get; set; }
            public Folder(string name, int count)
            {
                Name = name;
                Count = count;
                Optimize = true;
            }
        }

        private MainWindow parent;
        private Queue<Folder> syncFolders;
        private List<Process> processes;
        private Dictionary<BackgroundWorker, List<Folder>> workers;
        private object processMutex;
        private bool processingFoldersDone;
        private int totalSyncCount;
        private int pendingSyncCount;
        private bool syncForceStopped;
        private DateTime syncStartTime;

        private ObservableCollection<string> runningList;
        private ObservableCollection<string> pendingList;
        private ObservableCollection<string> completedList;
        private ObservableCollection<string> failedList;
        private ObservableCollection<string> totalList;

        public SyncManager(MainWindow wnd)
        {
            parent = wnd;
            syncFolders = new Queue<Folder>();
            processes = new List<Process>();
            workers = new Dictionary<BackgroundWorker, List<Folder>>();
            processMutex = new object();
            syncForceStopped = false;

            runningList = new ObservableCollection<string>();
            pendingList = new ObservableCollection<string>();
            completedList = new ObservableCollection<string>();
            failedList = new ObservableCollection<string>();
            totalList = new ObservableCollection<string>();

            parent.RunningList.ItemsSource = runningList;
            parent.PendingList.ItemsSource = pendingList;
            parent.CompletedList.ItemsSource = completedList;
            parent.FailedList.ItemsSource = failedList;
            parent.TotalList.ItemsSource = totalList;

            totalSyncCount = -2;
            pendingSyncCount = -2;
        }

        public void Start()
        {
            Stop();

            syncStartTime = DateTime.Now;

            totalSyncCount = -1;
            pendingSyncCount = -1;
            parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);

            runningList.Clear();
            pendingList.Clear();
            completedList.Clear();
            failedList.Clear();
            totalList.Clear();

            processingFoldersDone = false;
            syncForceStopped = false;

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = false;
            worker.DoWork += new DoWorkEventHandler(InitiateSync_DoWork);
            worker.ProgressChanged += new ProgressChangedEventHandler(InitiateSync_ProgressChanged);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(InitiateSync_Complete);

            runningList.Add("Gathering sync list ...");
            worker.RunWorkerAsync();
        }

        public void Stop()
        {
            syncForceStopped = true;

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

        private class InitiateSyncResult
        {
            public bool ForceSplit { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
        }

        private void InitiateSync_DoWork(object sender, DoWorkEventArgs workArgs)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            List<string> foldersToProcess = new List<string>();
            foldersToProcess.Add(parent.Options.SyncFolder);

            InitiateSyncResult result = new InitiateSyncResult();
            result.Output = string.Empty;
            result.Error = string.Empty;

            while (foldersToProcess.Count != 0)
            {
                string folder = foldersToProcess[0];
                foldersToProcess.RemoveAt(0);

                worker.ReportProgress(10, folder);

                string cmd = "sync -n ";
                cmd += (parent.Options.ForceSync) ? "-f " : "";
                cmd += folder;
                cmd += string.IsNullOrEmpty(parent.Options.Revision) ? "" : ("@" + parent.Options.Revision);

                MyExecute exec = new MyExecute("sd.exe", cmd, false, true, true);

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
                {
                    string output = exec.Output.Trim();
                    string error = exec.Error.Trim();

                    if (string.IsNullOrEmpty(output))
                    {
                        if (string.IsNullOrEmpty(error))
                        {
                            //nothing to sync
                        }
                        else
                        {
                            if (error.Contains("up-to-date") ||
                                error.Contains("no such file"))
                            {
                                //nothing to sync
                            }
                            else if (error.Contains("Request too large"))
                            {
                                //need to breakup the request
                                List<string> subfolders = GetSubFolders(folder);
                                foldersToProcess.AddRange(subfolders);

                                result.ForceSplit = true;
                            }
                            else
                            {
                                //got an unhandled error
                                result.Error += folder + ": " + error + "\n";
                            }
                        }
                    }
                    else
                    {
                        result.Output += "\r\n" + output;
                    }
                }
                else
                {
                    result.Output = string.Empty;
                    result.Error = "Failed to execute sd.exe.";
                    break;
                }
            }

            result.Output = result.Output.Trim();
            result.Error = result.Error.Trim();

            workArgs.Result = result;
        }

        private List<string> GetSubFolders(string folder)
        {
            List<string> subFolders = new List<string>();

            if (folder.EndsWith("*"))
                return subFolders;

            string originalFolder = folder.Replace("\\...", "");
            folder = originalFolder + "\\*";
            subFolders.Add(folder);

            string cmd = "dirs ";
            cmd += folder;

            MyExecute exec = new MyExecute("sd.exe", cmd, false, true, true);

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
            {
                string output = exec.Output.Trim();
                string error = exec.Error.Trim();

                if (string.IsNullOrEmpty(output))
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        //no additional subfolders
                    }
                    else
                    {
                        //error in getting the subfolders
                        subFolders.Clear();
                    }
                }
                else
                {
                    string[] delim = new string[1];
                    delim[0] = "\r\n";
                    string[] folders = output.Split(delim, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string fld in folders)
                    {
                        int index = fld.LastIndexOf('/');
                        string fldname = fld.Substring(index + 1);
                        subFolders.Add(originalFolder + "\\" + fldname + "\\...");
                    }
                }
            }
            else
            {
                subFolders.Clear();
            }

            return subFolders;
        }

        void InitiateSync_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            runningList.Clear();
            runningList.Add("Gathering sync list for " + (string)e.UserState);
        }

        private void InitiateSync_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            runningList.Clear();
            pendingList.Clear();
            completedList.Clear();
            failedList.Clear();
            totalList.Clear();

            if (syncForceStopped)
            {
                totalSyncCount = -2;
                pendingSyncCount = -2;
                parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);
                return;
            }

            if (e.Error != null)
            {
                Debug.Assert(false, "Failed to get sync list. \n" + e.Error.Message);
                totalSyncCount = 0;
                pendingSyncCount = 0;
                parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);
                parent.IsSyncing = false;
                return;
            }

            InitiateSyncResult result = e.Result as InitiateSyncResult;
            if (string.IsNullOrEmpty(result.Output))
            {
                if (string.IsNullOrEmpty(result.Error))
                    MessageBox.Show(parent, "Nothing to sync.");
                else
                    MessageBox.Show(parent, "Failed to get sync list:\n" + result.Error);

                totalSyncCount = 0;
                pendingSyncCount = 0;
                parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);
                parent.IsSyncing = false;
                return;
            }

            string[] delim = new string[1];
            delim[0] = "\r\n";
            string[] syncFiles = (result.Output as string).Split(delim, StringSplitOptions.RemoveEmptyEntries);

            totalSyncCount = syncFiles.Length;
            pendingSyncCount = syncFiles.Length;
            parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);

            if (result.ForceSplit || (syncFiles.Length > parent.Options.FileThreshold))
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
                ProcessFileList(syncList, parent.Options.ClientRoot + "\\");
#endif
            }
            else if (syncFiles.Length > 0)
            {
                SyncFolder(parent.Options.SyncFolder, syncFiles.Length);
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
                        SyncFolder(currentFolder + "*", subFiles.Count);
                }
                else
                {
                    if ((subFiles != null) && (subFiles.Count > 0))
                    {
                        if (subFiles.Count > parent.Options.FileThreshold)
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
                            SyncFolder(currentFolder + key + "/...", subFiles.Count);
#else
                            SyncFolder(currentFolder + key + "\\...", subFiles.Count);
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

        private void SyncFolder(string path, int count)
        {
            Folder folder = new Folder(path, count);
            SyncFolder(folder);
        }

        private void SyncFolder(Folder folder)
        {
            pendingList.Add(folder.Name);
            totalList.Add(folder.Name);
            syncFolders.Enqueue(folder);
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
                        if (syncForceStopped)
                            MessageBox.Show(parent, "Sync cancelled.");
                        else
                        {
                            TimeSpan diff = DateTime.Now - syncStartTime;
                            int syncTime = (int)diff.TotalMinutes;
                            string timeTaken = "Time taken - ";
                            if (syncTime < 1)
                                timeTaken += "less than a minute.";
                            else if (syncTime == 1)
                                timeTaken += "1 minute.";
                            else
                                timeTaken += syncTime.ToString() + " minutes.";

                            string msg = "Done syncing" + ((failedList.Count > 0) ? " but with some errors." : ".") + "\n";
                            MessageBox.Show(parent, msg + timeTaken);
                        }
                    }

                    return;
                }

                if (workers.Count < parent.Options.ProcessThreshold)
                {
                    List<Folder> folders = new List<Folder>();
                    int totalSize = 0;
                    bool optimize = true;

                    //always pick up the first entry
                    {
                        Folder folder = syncFolders.Dequeue();
                        runningList.Add(folder.Name);
                        pendingList.Remove(folder.Name);
                        folders.Add(folder);
                        totalSize += folder.Count;
                        optimize = folder.Optimize;
                    }

                    if (optimize)
                    {
                        //now look to optimize by combining small syncs
                        while ((totalSize < parent.Options.BatchSize) && (syncFolders.Count != 0))
                        {
                            Folder top = syncFolders.Peek();
                            if (!top.Optimize)
                                break;

                            int newTotal = totalSize + top.Count;
                            if ((newTotal < parent.Options.BatchSize) ||
                                ((newTotal - parent.Options.BatchSize) < (parent.Options.BatchSize - totalSize)))
                            {
                                Folder folder = syncFolders.Dequeue();
                                runningList.Add(folder.Name);
                                pendingList.Remove(folder.Name);
                                folders.Add(folder);
                                totalSize += folder.Count;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    BackgroundWorker worker = new BackgroundWorker();
                    worker.WorkerReportsProgress = false;
                    worker.WorkerSupportsCancellation = false;
                    worker.DoWork += new DoWorkEventHandler(FolderSync_DoWork);
                    worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FolderSync_Complete);

                    workers.Add(worker, folders);
                    worker.RunWorkerAsync(folders);
                }
            }
        }

        private void FolderSync_DoWork(object sender, DoWorkEventArgs workArgs)
        {
            List<Folder> folders = workArgs.Argument as List<Folder>;
            workArgs.Result = folders;
            
            string cmd = "sync ";
            cmd += (parent.Options.ForceSync) ? "-f " : "";
            cmd += (parent.Options.ClobberWriteable) ? "-w " : "";

            foreach (Folder folder in folders)
            {
                cmd += "\"" + folder.Name + "\"";
                cmd += string.IsNullOrEmpty(parent.Options.Revision) ? "" : ("@" + parent.Options.Revision);
                cmd += " ";
            }

            MyExecute exec = null;
            string error = string.Empty;
            bool retval = false;

            int retryCount = 2;
            while (retryCount != 0)
            {
                //We will never redirect output as it lowers the performance.
                exec = new MyExecute("sd.exe", cmd, parent.Options.ShowWindows, false, true);

                lock (processMutex)
                {
                    processes.Add(exec.ProcessHandle);
                }

                retval = exec.Run();
                retryCount--;

                lock (processMutex)
                {
                    processes.Remove(exec.ProcessHandle);
                }

                error = exec.Error.Trim();

                if ((retval == true) && (string.IsNullOrEmpty(error) || error.Contains("up-to-date")))
                    break;
            }

            if (retval == false)
                throw (new Exception("Execution failed"));

            if (!string.IsNullOrEmpty(error.Trim()) && !error.Contains("up-to-date"))
                throw (new Exception(error));
        }

        private void FolderSync_Complete(object sender, RunWorkerCompletedEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            List<Folder> folders = workers[worker];

            if (syncForceStopped)
            {
                foreach (Folder folder in folders)
                {
                    runningList.Remove(folder.Name);
                    pendingList.Insert(0, folder.Name);
                }
            }
            else if (e.Error != null)
            {
                Debug.Assert(false, e.Error.Message);

                if (folders.Count > 1)
                {
                    //try the sync again on the individual folders to localize the failure folder
                    foreach (Folder folder in folders)
                    {
                        runningList.Remove(folder.Name);
                        folder.Optimize = false;

                        SyncFolder(folder);
                    }
                }
                else
                {
                    string folderList = string.Empty;
                    foreach (Folder folder in folders)
                    {
                        folderList = folder.Name + "\n";

                        runningList.Remove(folder.Name);
                        failedList.Add(folder.Name);
                        parent.FailedTab.Visibility = System.Windows.Visibility.Visible;
                    }

                    string errormessage = "Failed to sync some folder - \n" + folderList + "\nError - \n" + e.Error.Message;
                    MessageBox.Show(parent, errormessage);
                }
            }
            else if (!e.Cancelled)
            {
                foreach (Folder folder in folders)
                {
                    pendingSyncCount -= folder.Count;
                    pendingSyncCount = Math.Max(pendingSyncCount, 0);
                    parent.UpdateSyncDetails(totalSyncCount, pendingSyncCount);

                    runningList.Remove(folder.Name);
                    completedList.Add(folder.Name);
                }
            }

            workers.Remove(worker);

            //start a new sync when an existing one gets completed
            StartNewSync();
        }
    }
}
