using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Windows;
using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.IO;

namespace GenericUpdater
{
    public class Updater
    {
        private static string baseSiteAddress = "http://toolbox/fastsync";

        public static void DoUpdate(string currentVersion)
        {
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.WorkerSupportsCancellation = false;
            worker.DoWork += new DoWorkEventHandler(worker_DoWork);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);

            worker.RunWorkerAsync(currentVersion);
        }

        static void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            string currentVersion = e.Argument as string;
            e.Result = false;

            StringBuilder sb = null;
            HttpWebResponse response = null;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(baseSiteAddress);
                request.UseDefaultCredentials = true;
                response = (HttpWebResponse)request.GetResponse();
                Stream responseStream = response.GetResponseStream();
                sb = new StringBuilder();
                Int32 bufSize = 1024 * 100;
                Int32 bytesRead = 0;
                byte[] buf = new byte[bufSize];

                long total = 0;
                if (responseStream.CanSeek)
                {
                    total = responseStream.Length;
                }
                else
                {
                    total = 500 * 1024; //assuming 500 KB
                }

                while ((bytesRead = responseStream.Read(buf, 0, bufSize)) != 0)
                    sb.Append(Encoding.UTF8.GetString(buf, 0, bytesRead));

                response.Close();
            }
            catch (Exception)
            {
                if (response != null)
                    response.Close();

                e.Result = false;
                return;
            }

            response = null;

            string html_data = sb.ToString();
            string availableVersion = string.Empty;

            try
            {
                //simply check if the current release is still on the home page.
                if (!html_data.Contains(currentVersion))
                    e.Result = true;

                //first get to the version section
                //List<string> version_section_1 = PartitionString(html_data, false, "downloadRelease", "AllRelease");
                //if (version_section_1.Count != 1)
                //    throw new Exception("Error while parsing versions.");

                //List<string> version_section_2 = PartitionString(version_section_1[0], false, "ReleaseId", "</a>");
                //if (version_section_2.Count != 1)
                //    throw new Exception("Error while parsing versions.");

                //List<string> version_section_3 = PartitionString(version_section_2[0], false, ">", " ");
                //if (version_section_3.Count != 1)
                //    throw new Exception("Error while parsing versions.");

                //availableVersion = version_section_3[0];
                //if (currentVersion != availableVersion)
                //    e.Result = true;
            }
            catch (Exception)
            {
                e.Result = false;
                return;
            }
        }

        static void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if(e.Result is bool)
            {
                if ((bool)e.Result)
                {
                    string msg = "An update of FastSync is available on toolbox. Do you want to download it?";
                    MessageBoxResult result = MessageBox.Show(msg, "Updater", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start("explorer.exe", baseSiteAddress);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }

        public static List<string> PartitionString(string data, Boolean add_tags, string start_tag, string end_tag)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(start_tag))
                return result;

            Int32 start_tag_pos_1 = data.IndexOf(start_tag);
            while (start_tag_pos_1 >= 0)
            {
                string sub_data = string.Empty;
                Int32 start_tag_pos_2 = data.IndexOf(start_tag, (start_tag_pos_1 + start_tag.Length));

                if (start_tag_pos_2 >= 0)
                    sub_data = data.Substring(start_tag_pos_1, (start_tag_pos_2 - start_tag_pos_1));
                else
                    sub_data = data.Substring(start_tag_pos_1);

                if (!string.IsNullOrEmpty(end_tag))
                {
                    Int32 end_tag_pos_1 = sub_data.IndexOf(end_tag, start_tag.Length);
                    if (end_tag_pos_1 >= 0)
                    {
                        sub_data = sub_data.Substring(0, (end_tag_pos_1 + end_tag.Length));

                        if (!add_tags)
                            sub_data = sub_data.Substring(start_tag.Length, (sub_data.Length - start_tag.Length - end_tag.Length));

                        result.Add(sub_data);
                    }
                }
                else
                {
                    if (!add_tags)
                        sub_data = sub_data.Substring(start_tag.Length);

                    result.Add(sub_data);
                }

                start_tag_pos_1 = start_tag_pos_2;
            }

            return result;
        }
    }
}
