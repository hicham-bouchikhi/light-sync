using System.Diagnostics;
using System.Globalization;
using LightSync.Core.Devices;

namespace LightSync.Devices.Nanoleaf;

public sealed record DiscoveredNanoleaf(string Name, string Host, int Port, string? Model, string? FirmwareVersion)
{
    public override string ToString() =>
        $"{Name} at {Host}:{Port}" +
        (Model is null ? string.Empty : $" (model {Model}") +
        (FirmwareVersion is null ? string.Empty : $", firmware {FirmwareVersion}") +
        (Model is null ? string.Empty : ")");
}

/// <summary>
/// Finds Nanoleaf devices advertising <c>_nanoleafapi._tcp</c> over mDNS, so the user does not
/// have to pin down an IP address by hand.
/// </summary>
/// <remarks>
/// Delegates to <c>avahi-browse</c> rather than implementing mDNS. A hand-rolled resolver would
/// bind port 5353, which conflicts with the avahi daemon that is already running on the target
/// systems.
/// </remarks>
public static class NanoleafDiscovery
{
    public const string ServiceType = "_nanoleafapi._tcp";

    public static async Task<IReadOnlyList<DiscoveredNanoleaf>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("avahi-browse")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // -t terminate after the initial cache dump, -p parseable, -r resolve.
        startInfo.ArgumentList.Add("-tpr");
        startInfo.ArgumentList.Add(ServiceType);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new DeviceException(
                "Could not run 'avahi-browse', so Nanoleaf devices cannot be discovered. " +
                "Install avahi, or set device.settings.host explicitly.", ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        string output;
        try
        {
            var readTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            output = await readTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Browsing timed out; whatever was already resolved is still useful.
            KillQuietly(process);
            return [];
        }

        return ParseAvahiOutput(output);
    }

    /// <summary>
    /// Parses parseable-mode avahi-browse output. Resolved records start with '=' and hold
    /// semicolon-separated fields, with TXT key/value pairs in the final column.
    /// </summary>
    public static IReadOnlyList<DiscoveredNanoleaf> ParseAvahiOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Dictionary<string, DiscoveredNanoleaf> byHost = [];

        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('='))
            {
                continue;
            }

            var fields = line.Split(';');
            if (fields.Length < 9)
            {
                continue;
            }

            // 0:'=' 1:interface 2:protocol 3:name 4:type 5:domain 6:hostname 7:address 8:port 9+:TXT
            var address = fields[7];
            if (!int.TryParse(fields[8], CultureInfo.InvariantCulture, out var port))
            {
                continue;
            }

            var txt = fields.Length > 9 ? string.Join(';', fields[9..]) : string.Empty;

            var device = new DiscoveredNanoleaf(
                Name: Unescape(fields[3]),
                Host: address,
                Port: port,
                Model: ReadTxtValue(txt, "md"),
                FirmwareVersion: ReadTxtValue(txt, "srcvers"));

            // The same device is advertised once per interface and address family; keep one.
            byHost.TryAdd(device.Host, device);
        }

        return [.. byHost.Values.OrderBy(d => d.Host, StringComparer.Ordinal)];
    }

    private static string? ReadTxtValue(string txt, string key)
    {
        var needle = $"\"{key}=";
        var start = txt.IndexOf(needle, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += needle.Length;
        var end = txt.IndexOf('"', start);
        return end < 0 ? null : txt[start..end];
    }

    /// <summary>avahi escapes non-alphanumeric characters as \nnn decimal triples.</summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\'
                && i + 3 < value.Length
                && int.TryParse(value.AsSpan(i + 1, 3), CultureInfo.InvariantCulture, out var code))
            {
                builder.Append((char)code);
                i += 3;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }
    }
}
