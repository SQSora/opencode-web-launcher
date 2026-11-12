using System;
using System.IO;
using System.Text;

namespace OpenCodeLauncher
{
    internal enum LogLevel
    {
        Info,
        Warn,
        Error
    }

    internal static class Logger
    {
        private static readonly object Sync = new object();

        public static event Action<string> LineWritten;

        public static void Info(string message)
        {
            Write(message, LogLevel.Info);
        }

        public static void Warn(string message)
        {
            Write(message, LogLevel.Warn);
        }

        public static void Error(string message)
        {
            Write(message, LogLevel.Error);
        }

        public static void Write(string message, LogLevel level)
        {
            var line = string.Format(
                "[{0}] [{1}] {2}",
                DateTime.Now.ToString("HH:mm:ss"),
                level.ToString().ToUpperInvariant(),
                SecretMasker.Mask(message));

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }

            var handler = LineWritten;
            if (handler == null) return;
            try
            {
                handler(line);
            }
            catch
            {
            }
        }
    }
}
