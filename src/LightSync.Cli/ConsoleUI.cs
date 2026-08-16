using System.Globalization;
using System.Text;
using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Pipeline;

namespace LightSync.Cli;

internal static class ConsoleUI
{
    private const string Reset = "\e[0m";

    private static bool SupportsColor =>
        !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static void Error(string message)
    {
        Console.Error.WriteLine(SupportsColor ? $"\e[31m{message}{Reset}" : message);
    }

    public static void Warn(string message)
    {
        Console.WriteLine(SupportsColor ? $"\e[33m{message}{Reset}" : message);
    }

    public static void Success(string message)
    {
        Console.WriteLine(SupportsColor ? $"\e[32m{message}{Reset}" : message);
    }

    public static void WriteDisplays(IReadOnlyList<DisplayInfo> displays)
    {
        Console.WriteLine($"{displays.Count} display(s) detected:");

        foreach (var display in displays)
        {
            Console.WriteLine($"  {display}");
            if (!string.IsNullOrWhiteSpace(display.Description))
            {
                Console.WriteLine($"      {display.Description}");
            }
        }
    }

    /// <summary>Renders zone colours as a strip of truecolour blocks.</summary>
    public static string ZoneStrip(ReadOnlySpan<RgbColor> zones)
    {
        var builder = new StringBuilder(zones.Length * 20);

        foreach (var zone in zones)
        {
            if (SupportsColor)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\e[48;2;{zone.R};{zone.G};{zone.B}m  ");
            }
            else
            {
                builder.Append(ColorMath.Luminance(zone) > 0.5 ? '#' : '.');
            }
        }

        if (SupportsColor)
        {
            builder.Append(Reset);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Rewrites the current line in place so a long run does not scroll thousands of lines.
    /// Falls back to periodic full lines when output is redirected.
    /// </summary>
    public static void WriteLiveStatus(string text)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        Console.Write($"\e[2K\r{text}");
    }

    public static void EndLiveStatus()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.WriteLine();
        }
    }

    public static void WriteMetrics(PipelineMetrics metrics)
    {
        Console.WriteLine($"  frames captured  {metrics.CapturedFrames}");
        Console.WriteLine($"  frames sent      {metrics.SentFrames}");
        Console.WriteLine($"  frames dropped   {metrics.DroppedFrames}");
        Console.WriteLine($"  device errors    {metrics.DeviceErrors}");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  average latency  {metrics.AverageLatency.TotalMilliseconds:0.0} ms"));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  uptime           {metrics.Uptime:hh\\:mm\\:ss}"));
    }
}
