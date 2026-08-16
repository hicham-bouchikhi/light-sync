using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightSync.Core.Capture.Wayland;

/// <summary>
/// Discovers displays from <c>hyprctl monitors -j</c>. The desktop portal exposes no monitor
/// list before the user has picked a source, so the compositor is asked directly.
/// </summary>
public sealed class HyprlandDisplayEnumerator : IDisplayEnumerator
{
    public async Task<IReadOnlyList<DisplayInfo>> GetDisplaysAsync(CancellationToken cancellationToken) =>
        ParseMonitors(await RunHyprctlAsync(cancellationToken));

    /// <summary>
    /// Parses <c>hyprctl monitors -j</c> output. Separate from process execution so the
    /// mapping can be tested against recorded output.
    /// </summary>
    public static IReadOnlyList<DisplayInfo> ParseMonitors(string json)
    {
        HyprlandMonitor[]? monitors;
        try
        {
            monitors = JsonSerializer.Deserialize(json, HyprctlJsonContext.Default.HyprlandMonitorArray);
        }
        catch (JsonException ex)
        {
            throw new DisplayEnumerationException("Could not parse the output of 'hyprctl monitors -j'.", ex);
        }

        if (monitors is null || monitors.Length == 0)
        {
            throw new DisplayEnumerationException("Hyprland reported no connected monitors.");
        }

        return [.. monitors.Select(ToDisplayInfo)];
    }

    private static DisplayInfo ToDisplayInfo(HyprlandMonitor monitor)
    {
        // hyprctl reports the transformed, pre-scale pixel size, which is exactly the
        // coordinate space the portal reports capture rectangles in.
        return new DisplayInfo(
            Id: monitor.Id,
            Name: monitor.Name ?? $"monitor-{monitor.Id}",
            Description: monitor.Description ?? string.Empty,
            X: monitor.X,
            Y: monitor.Y,
            Width: monitor.Width,
            Height: monitor.Height,
            RefreshRate: monitor.RefreshRate,
            Scale: monitor.Scale <= 0 ? 1.0 : monitor.Scale);
    }

    private static async Task<string> RunHyprctlAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("hyprctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("monitors");
        startInfo.ArgumentList.Add("-j");

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new DisplayEnumerationException(
                "Could not run 'hyprctl'. Display detection currently requires Hyprland.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new DisplayEnumerationException(
                $"'hyprctl monitors -j' exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}

public sealed class DisplayEnumerationException : Exception
{
    public DisplayEnumerationException()
    {
    }

    public DisplayEnumerationException(string message)
        : base(message)
    {
    }

    public DisplayEnumerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class HyprlandMonitor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("refreshRate")]
    public double RefreshRate { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; }
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(HyprlandMonitor[]))]
internal sealed partial class HyprctlJsonContext : JsonSerializerContext;
