using System;
using System.Diagnostics;

namespace Starboard
{
    internal enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    internal static class Logger
    {
        private static readonly object _sync = new();

        public static LogLevel MinLevel =
#if DEBUG
            LogLevel.Debug;      // Hide Info spam while debugging
#else
            LogLevel.Info;
#endif

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level < MinLevel) return;
            lock (_sync)
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                Console.WriteLine(line);
                Debug.WriteLine(line);
            }
        }

        public static void Warn(string message) => Log(message, LogLevel.Warn);

        public static void Error(string message, Exception ex)
            => Log($"ERROR: {message}\n{ex}", LogLevel.Error);
    }
}
