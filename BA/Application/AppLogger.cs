// FILE: BA_Tools/Application/AppLogger.cs
using System;
using System.IO;

namespace BA.BAApplication
{
    internal static class AppLogger
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "Logs");

        public static void LogInfo(string message)
        {
            WriteEntry("INFO", message);
        }

        public static void LogError(string context, Exception ex)
        {
            WriteEntry("ERROR", $"[{context}]{Environment.NewLine}{ex}");
        }

        private static void WriteEntry(string level, string body)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                string logFile = Path.Combine(LogDirectory, $"batools_{DateTime.Now:yyyy-MM-dd}.log");
                string entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {body}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
                File.AppendAllText(logFile, entry);
            }
            catch
            {
                // Logging must never throw — swallow silently.
            }
        }
    }
}