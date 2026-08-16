using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Mapping;
using LightSync.Core.Processing;

namespace LightSync.Cli.Commands;

/// <summary>
/// Everything the commands share. Constructed once in Program so no service-locator or
/// reflection-based container is needed, which keeps the AOT build clean.
/// </summary>
internal sealed class CommandContext
{
    public CommandContext(string configPath)
    {
        ConfigPath = configPath;
        Displays = new HyprlandDisplayEnumerator();
        Adapters = new DeviceAdapterFactory();
    }

    public string ConfigPath { get; }

    public IDisplayEnumerator Displays { get; }

    public IDeviceAdapterFactory Adapters { get; }

    public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
        ConfigurationLoader.LoadValidatedAsync(ConfigPath, cancellationToken);

    public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
        ConfigurationLoader.SaveAsync(ConfigPath, config, cancellationToken);

    public static ZoneMapper BuildMapper(MappingConfig mapping) => new(
        mapping.ZoneCount,
        mapping.ParsedLayout,
        mapping.ParsedDirection,
        mapping.Reverse,
        mapping.CustomOrder);

    public static ColorProcessor BuildProcessor(AppConfig config) =>
        new(BuildMapper(config.Mapping), config.Processing.ToOptions());

    /// <summary>
    /// The frame size to ask the capture backend for. Sampling a handful of pixel columns per
    /// zone is plenty for an average, and keeping the frame tiny is what makes 60 FPS cheap.
    /// </summary>
    public static (int Width, int Height) TargetFrameSize(AppConfig config)
    {
        const int columnsPerZone = 4;
        const int rows = 8;

        var layout = config.Mapping.ParsedLayout;

        var width = layout == ZoneLayout.Vertical
            ? config.Mapping.ZoneCount * columnsPerZone
            : rows * columnsPerZone;
        var height = layout == ZoneLayout.Vertical
            ? rows
            : config.Mapping.ZoneCount * columnsPerZone;

        // Never ask for more pixels than the region actually has.
        return (
            Math.Max(1, Math.Min(width, config.Capture.Width)),
            Math.Max(1, Math.Min(height, config.Capture.Height)));
    }

    public static CaptureRequest BuildRequest(AppConfig config)
    {
        var (width, height) = TargetFrameSize(config);
        return new CaptureRequest(
            config.Capture.DisplayId,
            config.Capture.Area,
            config.Capture.Fps,
            width,
            height);
    }
}
