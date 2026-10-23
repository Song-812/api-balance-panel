using System;
using System.Diagnostics;
using System.IO;

internal static class SmokeTests
{
    public static int Main(string[] args)
    {
        int passed = 0;
        foreach(string name in new [] { "API余额面板", "VPN流量面板" }) {
            string directory = Path.Combine(args[0],name);
            using(Process process = Process.Start(new ProcessStartInfo(Path.Combine(directory,name + ".exe")) { WorkingDirectory = directory })) {
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while(!process.HasExited && DateTime.UtcNow < deadline) {
                    process.Refresh();
                    if(process.MainWindowHandle != IntPtr.Zero) break;
                    process.WaitForExit(100);
                }
                process.Refresh();
                bool opened = !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
                Console.WriteLine((opened ? "PASS " : "FAIL ") + name + " standalone startup; PID=" + process.Id);
                if(!opened) return 1;
                bool closeRequested = process.CloseMainWindow();
                bool exited = process.WaitForExit(10000);
                Console.WriteLine((closeRequested && exited ? "PASS " : "FAIL ") + name + " normal shutdown");
                if(!closeRequested || !exited) return 1;
                passed += 2;
            }
        }
        Console.WriteLine("Standalone smoke: " + passed + " passed, 0 failed"); return 0;
    }
}
