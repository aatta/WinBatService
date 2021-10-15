using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace WinBatService
{
    public partial class BatService : ServiceBase
    {
        private Process batProces;
        private bool isStopping = false;
        private string batFilePath;

        public BatService()
        {
            InitializeComponent();
        }

        public string CurrentServiceName { get; private set; }

        protected string GetServiceName()
        {
            // Calling System.ServiceProcess.ServiceBase::ServiceNamea allways returns
            // an empty string,
            // see https://connect.microsoft.com/VisualStudio/feedback/ViewFeedback.aspx?FeedbackID=387024

            // So we have to do some more work to find out our service name, this only works if
            // the process contains a single service, if there are more than one services hosted
            // in the process you will have to do something else

            var processId = Process.GetCurrentProcess().Id;
            var query = "SELECT * FROM Win32_Service where ProcessId = " + processId;
            
            using (var searcher = new System.Management.ManagementObjectSearcher(query))
            {
                foreach (var queryObj in searcher.Get())
                {
                    return queryObj["Name"].ToString();
                }
            }

            throw new Exception("Can not get the ServiceName");
        }

        protected void StartProcess(string command)
        {
            var processInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.WorkingDirectory = System.AppDomain.CurrentDomain.BaseDirectory;

            batProces = Process.Start(processInfo);

            batProces.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                Logger.Instance.LogInfo(e.Data);
            batProces.BeginOutputReadLine();

            batProces.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
               Logger.Instance.LogError(e.Data);
            batProces.BeginErrorReadLine();

            batProces.EnableRaisingEvents = true;
            batProces.Exited += (object sender, EventArgs e) =>
               {
                   Logger.Instance.LogError(string.Format("Batch prcess exited with exit code: {0}", ((Process)sender).ExitCode));

                   if (!isStopping)
                   {
                       StopProcess();
                   }
               };

            Logger.Instance.LogError(string.Format("Successfully started batch process with id: {0}", batProces.Id));

            //batProces.WaitForExit();

            //Logger.Instance.LogInfo(string.Format("ExitCode: {0}", batProces.ExitCode));
            //batProces.Close();
        }

        private void StopProcess()
        {
            Logger.Instance.LogInfo(string.Format("{0} stopping...", CurrentServiceName));

            try
            {
                if (batProces != null)
                {
                    batProces.CancelOutputRead();
                    batProces.CancelErrorRead();
                    batProces.Close();

                    var exitCode = -1;
                    try
                    {
                        exitCode = !batProces.HasExited ? batProces.ExitCode : -1;
                    }
                    catch { }

                    Logger.Instance.LogInfo(string.Format("Batch process was stopped with exit code: {0}", exitCode));

                    batProces.Dispose();
                }
                else
                {
                    Logger.Instance.LogInfo("Batch process wasn't started.");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError(ex.Message + "\r\n" + ex.StackTrace);
            }

            batProces = null;
        }

        protected override void OnStart(string[] args)
        {
            CurrentServiceName = GetServiceName();

            var strParams = string.Empty;

            if (args == null || args.Length == 0)
            {
                args = System.Environment.GetCommandLineArgs().Skip(1).ToArray();
            }

            if (args != null && args.Length > 0)
            {
                strParams = string.Join(", ", args);
            }

            var processId = Process.GetCurrentProcess().Id;
            Logger.Instance.LogInfo(string.Format("{0}(PID: {1}) started with parameters. {2}", CurrentServiceName, processId, strParams));

            var batFileArg = args.FirstOrDefault(ar => ar.StartsWith("--batFile"));
            if (batFileArg == null)
                throw new ArgumentException("batFile parameter wasn't supplied to the service.");

            batFilePath = batFileArg.Split("=".ToCharArray()).ElementAt(1);
            if (batFilePath == null)
                throw new ArgumentException("batFile parameter wasn't supplied to the service.");


            StartProcess(batFilePath);
        }

        protected override void OnStop()
        {
            isStopping = true;
            StopProcess();
        }

        //private void Log(string message)
        //{
        //    //System.Diagnostics.EventLog.Delete(EventLog.Source);

        //    //((ISupportInitialize)(EventLog)).BeginInit();
        //    //if (!EventLog.SourceExists(EventLog.Source))
        //    //{
        //    //    EventLog.CreateEventSource(EventLog.Source, EventLog.Log);
        //    //}
        //    //((ISupportInitialize)(EventLog)).EndInit();

        //    //EventLog.WriteEntry(message, EventLogEntryType.Information);
        //    Logger.Instance.LogInfo(message);
        //}

        //private void CustomLog(string message)
        //{
        //    System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "log.txt"), message);
        //}
    }
}
