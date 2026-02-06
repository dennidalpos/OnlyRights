using System;
using System.IO;

namespace NtfsAudit.App.Logging
{
    public class Logger
    {
        private readonly string _logPath;
        private readonly object _lock = new object();

        public Logger(string logPath)
        {
            _logPath = logPath;
        }

        public event Action<string> ErrorReported;

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
            if (ErrorReported != null)
            {
                ErrorReported(message);
            }
        }

        private void Write(string level, string message)
        {
            var line = string.Format("{0:O} [{1}] {2}", DateTime.Now, level, message);
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
            }
        }
    }
}
