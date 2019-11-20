using System;
using System.Text;
using System.Windows;
using System.Diagnostics;

namespace FastSyncUI
{
    public delegate void ExecuteOutputAvailable(MyExecute sender, string data);

    public class MyExecute
    {
        public event ExecuteOutputAvailable OnOutputAvailable;
        public event ExecuteOutputAvailable OnErrorAvailable;

        public string Output
        {
            get { return m_output.ToString(); }
        }

        public string Error
        {
            get { return m_error.ToString(); }
        }

        public Process ProcessHandle
        {
            get { return m_process; }
        }

        private StringBuilder m_output;
        private StringBuilder m_error;
        private Process m_process;

        public MyExecute(string cmd, string arguments, bool showWindow, bool redirectOutput, bool redirectError)
        {
            m_output = new StringBuilder();
            m_error = new StringBuilder();

            m_process = new Process();
            m_process.StartInfo.FileName = cmd;
            m_process.StartInfo.Arguments = arguments;
            m_process.StartInfo.UseShellExecute = false;
            m_process.StartInfo.CreateNoWindow = !showWindow;
            m_process.StartInfo.RedirectStandardOutput = redirectOutput;
            m_process.StartInfo.RedirectStandardError = redirectError;
            m_process.OutputDataReceived += new DataReceivedEventHandler(DataOutputHandler);
            m_process.ErrorDataReceived += new DataReceivedEventHandler(DataErrorHandler);
        }

        public bool Run()
        {
            bool retval = m_process.Start();

            if (m_process.StartInfo.RedirectStandardOutput)
                m_process.BeginOutputReadLine();

            if (m_process.StartInfo.RedirectStandardError)
                m_process.BeginErrorReadLine();
            
            m_process.WaitForExit();
            m_process.Close();
            return retval;
        }

        private void DataOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (OnOutputAvailable != null)
            {
                OnOutputAvailable(this, outLine.Data);
            }
            else
            {
                try
                {
                    m_output.AppendLine(outLine.Data);
                }
                catch (Exception exp)
                {
                    Debug.Assert(false, exp.Message);
                }
            }
        }

        private void DataErrorHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (OnErrorAvailable != null)
            {
                OnErrorAvailable(this, outLine.Data);
            }
            else
            {
                try
                {
                    m_error.AppendLine(outLine.Data);
                }
                catch (Exception exp)
                {
                    Debug.Assert(false, exp.Message);
                }
            }
        }
    }
}
