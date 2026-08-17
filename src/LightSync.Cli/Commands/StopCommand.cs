using System.Runtime.InteropServices;

namespace LightSync.Cli.Commands;

internal static partial class StopCommand
{
    /// <summary>
    /// Asks a running instance to shut down so the pipeline drains and the device is released,
    /// rather than killing it outright.
    /// </summary>
    /// <remarks>
    /// SIGTERM specifically, not SIGINT. A shell that starts a job in the background sets
    /// SIGHUP, SIGINT and SIGQUIT to ignored in the child, and the runtime correctly leaves an
    /// already-ignored signal alone — so SIGINT would be silently discarded by exactly the
    /// backgrounded instances this command exists to stop. SIGTERM is never inherited that way.
    /// </remarks>
    public static int Run()
    {
        if (PidFile.ReadRunningPid() is not { } pid)
        {
            ConsoleUI.Warn("No running light-sync instance found.");
            return 1;
        }

        if (Kill(pid, Sigterm) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            ConsoleUI.Error($"Could not signal process {pid} (errno {error}).");
            return 1;
        }

        ConsoleUI.Success($"Asked light-sync (pid {pid}) to stop.");
        return 0;
    }

    private const int Sigterm = 15;

    // Pinned to the system directories so the loader cannot be steered at a libc placed
    // somewhere earlier on the search path.
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
    private static partial int Kill(int pid, int signal);
}
