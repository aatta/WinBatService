using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace WinBatService
{
    public class Logger
    {
        private readonly log4net.ILog log;
        private static Logger instance;

        public static Logger Instance
        {
            get
            {
                if (instance == null)
                    instance = new Logger();
                
                return instance;
            }
        }

        private Logger()
        {
            log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        public void LogInfo(string message)
        {
            log.Info(message);
        }

        public void LogError(string message)
        {
            log.Error(message);
        }

        public void LogWarning(string message)
        {
            log.Warn(message);
        }
    }
}
