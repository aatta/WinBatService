using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Timers;

namespace WinBatService
{
    public partial class BatService : ServiceBase
    {
        private Process batProces;
        private bool isStopping = false;
        private string batFilePath;
        private System.Timers.Timer aTimer;
        private ElapsedEventHandler timerEventHandler;

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

                    try
                    {
                        KillProcessAndChildrens(batProces.Id);
                    }
                    catch (Exception iex)
                    {
                        Logger.Instance.LogError("Error while killing process hierarchy.\r\n" + iex.Message + "\r\n" + iex.StackTrace);
                    }

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
                    Logger.Instance.LogInfo("Batch process wasn't running.");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError(ex.Message + "\r\n" + ex.StackTrace);
            }

            batProces = null;
        }

        private void KillProcessAndChildrens(int pid)
        {
            var processSearcher = new ManagementObjectSearcher
              ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection processCollection = processSearcher.Get();

            // We must kill child processes first!
            if (processCollection != null)
            {
                foreach (var mo in processCollection)
                {
                    KillProcessAndChildrens(Convert.ToInt32(mo["ProcessID"])); //kill child processes(also kills childrens of childrens etc.)
                }
            }

            // Then kill parents.
            try
            {
                Logger.Instance.LogInfo(string.Format("Killing process with processId:{0}.", pid));

                var proc = Process.GetProcessById(pid);
                if (!proc.HasExited) proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
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

            /***********Start monitoring********************/
            // Create a timer with a ten second interval.
            var interal = TimeSpan.FromMinutes(1).TotalMilliseconds;
            aTimer = new Timer(interal);

            // Create event handler
            timerEventHandler = new ElapsedEventHandler(OnTimedEvent);

            // Hook up the Elapsed event for the timer.
            aTimer.Elapsed += timerEventHandler;

            // Set the Interval to 2 seconds (2000 milliseconds).
            aTimer.Interval = interal;
            aTimer.Enabled = true;
        }

        protected override void OnStop()
        {
            isStopping = true;
            StopProcess();

            /****************Stop monitoring****************/
            aTimer.Elapsed -= timerEventHandler;
            aTimer.Enabled = false;
            aTimer.Close();
            aTimer.Dispose();
            aTimer = null;
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            if (batProces == null)
            {
                Logger.Instance.LogInfo(string.Format("Monitor is starting process({0}) again...", batFilePath));

                StartProcess(batFilePath);
            }
            else
            {
                Logger.Instance.LogInfo(string.Format("Monitor found out that process({0}) is running...", batFilePath));
            }
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
