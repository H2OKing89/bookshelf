using System;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Common.Instrumentation
{
    public static class GlobalExceptionHandlers
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(GlobalExceptionHandlers));
        public static void Register()
        {
            AppDomain.CurrentDomain.UnhandledException += HandleAppDomainException;
            TaskScheduler.UnobservedTaskException += HandleTaskException;
        }

        private static void HandleTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var exception = e.Exception;
            var exceptionText = CleanseException(exception);

            Console.WriteLine("Task Error: {0}", exceptionText);
            Logger.Error(exception, "Task Error");
        }

        private static void HandleAppDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            if (exception == null)
            {
                return;
            }

            var exceptionText = CleanseException(exception);

            if (exception is NullReferenceException &&
                exceptionText?.Contains("Microsoft.AspNet.SignalR.Transports.TransportHeartbeat.ProcessServerCommand") == true)
            {
                Logger.Warn("SignalR Heartbeat interrupted");
                return;
            }

            Console.WriteLine("EPIC FAIL: {0}", exceptionText);
            Logger.Fatal(exception, "EPIC FAIL.");
        }

        private static string CleanseException(Exception exception)
        {
            var logEvent = new LogEventInfo { Exception = exception };
            return CleanseLogMessage.Cleanse(logEvent, entry => entry.Exception.ToString());
        }
    }
}
