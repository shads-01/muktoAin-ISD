using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MuktoAin.Web.Services;

/// <summary>
/// Development-only watchdog that prevents orphaned server processes from holding ports
/// after terminal exits, Ctrl+C cancellation, or crashed dotnet watch sessions.
/// </summary>
public static class DevProcessWatchdog
{
    private static int _initialized;

    public static void Initialize(IHostEnvironment env)
    {
        if (!env.IsDevelopment())
        {
            return;
        }

        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        // 1. Reclaim port: kill any existing stale MuktoAin.Web instances
        KillStaleInstances();

        // 2. Immediate exit on Ctrl+C (prevent background services from delaying shutdown)
        Console.CancelKeyPress += (_, _) =>
        {
            Environment.Exit(0);
        };

        // 3. Monitor parent process (dotnet / dotnet watch) so child dies when parent dies
        if (OperatingSystem.IsWindows())
        {
            MonitorParentProcessOnWindows();
        }
    }

    private static void KillStaleInstances()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var processName = current.ProcessName;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (process.Id != current.Id)
                {
                    try
                    {
                        Console.WriteLine($"[DevProcessWatchdog] Reclaiming port: terminating stale {processName} (PID {process.Id})...");
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch
                    {
                        // Stale process might have already exited
                    }
                }
            }
        }
        catch
        {
            // Ignore any permission or enumeration issues in dev
        }
    }

    private static void MonitorParentProcessOnWindows()
    {
        try
        {
            var parentPid = GetParentProcessIdWindows();
            if (parentPid <= 0)
            {
                return;
            }

            var parent = Process.GetProcessById(parentPid);
            var parentName = parent.ProcessName.ToLowerInvariant();

            // Monitor any parent launcher (dotnet, shells, IDE terminals, etc.) except explorer/services
            if (parentName.Contains("explorer") || parentName.Contains("services"))
            {
                return;
            }

            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (parent.HasExited)
                        {
                            Environment.Exit(0);
                        }
                    }
                    catch
                    {
                        Environment.Exit(0);
                    }

                    Thread.Sleep(500);
                }
            })
            {
                IsBackground = true,
                Name = "DevParentProcessWatchdog"
            };

            thread.Start();
        }
        catch
        {
            // Ignore if parent could not be resolved or monitored
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    private static int GetParentProcessIdWindows()
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(
                Process.GetCurrentProcess().Handle,
                0, // ProcessBasicInformation
                ref pbi,
                Marshal.SizeOf(pbi),
                out _);

            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        catch
        {
        }

        return -1;
    }
}
