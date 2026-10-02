using System.Diagnostics;
using LightSync.Application;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbLaunchLoggingTests
{
    [Fact]
    public async Task WritesBothStreamsToLiteralLogPathAndPreservesExitCode()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "POSIX file descriptor redirection applies to Unix launches.");
        var directory = Directory.CreateTempSubdirectory("light-sync-launch-log-");
        try
        {
            var logPath = Path.Combine(directory.FullName, "log 'space' $(touch injected) `touch injected`.txt");
            var command = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            command.ArgumentList.Add("-c");
            command.ArgumentList.Add("printf '%s\\n' \"$@\"; printf '%s\\n' 'stub stderr' >&2; exit 9");
            command.ArgumentList.Add("stub");
            command.ArgumentList.Add("argument with spaces and 'quotes'");
            var start = OpenRgbServerLauncher.CreateLoggedStartInfo(command, logPath);
            start.WorkingDirectory = directory.FullName;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(9, process.ExitCode);
            Assert.Empty(await stdout);
            Assert.Empty(await stderr);
            var text = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
            Assert.Contains("argument with spaces and 'quotes'", text, StringComparison.Ordinal);
            Assert.Contains("stub stderr", text, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(directory.FullName, "injected")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ChildKeepsLoggingAfterLauncherDisposesItsHandle()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "POSIX file descriptor redirection applies to Unix launches.");
        var directory = Directory.CreateTempSubdirectory("light-sync-launch-lifetime-");
        try
        {
            var logPath = Path.Combine(directory.FullName, "openrgb.log");
            var command = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            command.ArgumentList.Add("-c");
            command.ArgumentList.Add("i=0; while [ \"$i\" -lt 5000 ]; do printf '%s\\n' 'stdout line' 'stderr line' >&2; "
                + "i=$((i+1)); done; printf '%s\\n' 'child completed'");
            var start = OpenRgbServerLauncher.CreateLoggedStartInfo(command, logPath);
            var process = Process.Start(start)!;
            process.Dispose();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var text = string.Empty;
            while (!text.Contains("child completed", StringComparison.Ordinal))
            {
                await Task.Delay(10, deadline.Token);
                if (File.Exists(logPath))
                {
                    text = await File.ReadAllTextAsync(logPath, deadline.Token);
                }
            }
            Assert.True(text.Length > 64 * 1024);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
