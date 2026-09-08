// Path: BA.PreviewWorkerSupervisor/Program.cs
using System;
using System.Diagnostics;

namespace BA.PreviewWorkerSupervisor
{
    public static class Program
    {
        // Environment specific, adjust for your actual install path and
        // journal location before scheduling this.
        private static readonly string RevitExePath = @"C:\Program Files\Autodesk\Revit 2026\Revit.exe";
        private static readonly string JournalPath = @"C:\ProgramData\BA\PreviewWorker\preview_worker.txt";
        private static readonly TimeSpan MaxRuntime = TimeSpan.FromHours(3);

        public static int Main(string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = RevitExePath,
                Arguments = $"/language ENU \"{JournalPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false
            };

            psi.EnvironmentVariables["BA_PREVIEW_WORKER_MODE"] = "1";

            using Process? process = Process.Start(psi);

            if (process == null)
            {
                Console.Error.WriteLine("Failed to start Revit process.");
                return 1;
            }

            bool exited = process.WaitForExit((int)MaxRuntime.TotalMilliseconds);

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }

                Console.Error.WriteLine("Revit exceeded max runtime and was terminated. Items left in Processing status will be reconciled on the next run.");
                return 2;
            }

            Console.WriteLine($"Revit preview worker exited with code {process.ExitCode}.");
            return process.ExitCode;
        }
    }
}