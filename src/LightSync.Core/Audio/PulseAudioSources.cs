using System.ComponentModel;
using System.Diagnostics;

namespace LightSync.Core.Audio;

public static class PulseAudioSources
{
    public static IReadOnlyList<string> ParseMonitors(string output)
    {
        List<string> monitors = ["@DEFAULT_MONITOR@"];
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2 && fields[1].EndsWith(".monitor", StringComparison.Ordinal)
                && !monitors.Contains(fields[1], StringComparer.Ordinal))
            {
                monitors.Add(fields[1]);
            }
        }

        return monitors;
    }

    public static async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pactl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("list");
        start.ArgumentList.Add("short");
        start.ArgumentList.Add("sources");
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new IOException("Install pactl (PulseAudio tools) to list playback monitor sources.", ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            var error = await errors;
            if (process.ExitCode != 0)
            {
                throw new IOException("Could not list audio sources: " + error.Trim());
            }

            return ParseMonitors(text);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
