using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FastSyncUI
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static string m_version = ProductVersion.VersionInfo.Version;

        private bool isSyncing;
        private SyncManager manager;
        private SyncOptions options;

        public SyncOptions Options
        {
            get { return options; }
        }

        public bool IsSyncing
        {
            get { return isSyncing; }
            set
            {
                isSyncing = value;
                if (isSyncing)
                {
                    SyncButton.Content = "Stop Sync";
                    ProcessThreshold.IsEnabled = false;
                    FilesThreshold.IsEnabled = false;
                    BatchSize.IsEnabled = false;
                }
                else
                {
                    SyncButton.Content = "Start Sync";
                    ProcessThreshold.IsEnabled = true;
                    FilesThreshold.IsEnabled = true;
                    BatchSize.IsEnabled = true;
                }
            }
        }

        public MainWindow()
        {
            GenericUpdater.Updater.DoUpdate(m_version);

            InitializeComponent();
            this.Title += " " + m_version;

            FailedTab.Visibility = System.Windows.Visibility.Hidden;
            options = new SyncOptions();

            //Initialize the UI
            IsSyncing = false;
            UpdateSyncDetails(-2, -2);

            //Process the parameters
            if (!options.ProcessCommandLineArgs())
                this.Close();
        }

        public void SyncButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsSyncing)
            {
                if (manager != null)
                    manager.Stop();

                IsSyncing = false;
            }
            else
            {
                if (string.IsNullOrEmpty(options.SyncFolder))
                {
                    MessageBox.Show(this, "No folder selected to sync.");
                    return;
                }

                options.SetThresholds(ProcessThreshold.Text, FilesThreshold.Text, BatchSize.Text);
                if (manager == null)
                    manager = new SyncManager(this);

                FailedTab.Visibility = System.Windows.Visibility.Hidden;

                manager.Start();
                IsSyncing = true;
            }
        }

        public void UpdateSyncDetails(int total_count, int left_count)
        {
            ClientName.Text = options.ClientName;
            CLNumber.Text = "@" + (string.IsNullOrEmpty(options.Revision) ? "Head" : options.Revision);
            ForceSync.Text = options.ForceSync.ToString();
            ClobberWriteable.Text = options.ClobberWriteable.ToString();
            FileCountTotal.Text = (total_count >= 0) ? total_count.ToString() : ((total_count == -1) ? "<Calculating>" : "<Pending>");
            FileCountLeft.Text = (left_count >= 0) ? left_count.ToString() : ((left_count == -1) ? "<Calculating>" : "<Pending>");
            ClientRoot.Text = options.ClientRoot;
            SyncFolder.Text = options.SyncFolder;

            ProcessThreshold.Text = options.ProcessThreshold.ToString();
            FilesThreshold.Text = options.FileThreshold.ToString();
            BatchSize.Text = options.BatchSize.ToString();
            ShowWindows.IsChecked = options.ShowWindows;

            if (string.IsNullOrEmpty(options.ClientName) || string.IsNullOrEmpty(options.ClientRoot))
                SyncButton.IsEnabled = false;
            else
                SyncButton.IsEnabled = true;
        }

        private void ShowWindows_Clicked(object sender, RoutedEventArgs e)
        {
            options.SetShowWindows(ShowWindows.IsChecked == true);
        }

        private void HelpButtonClick(object sender, RoutedEventArgs e)
        {
            options.ShowHelp(null);
        }
    }
}
