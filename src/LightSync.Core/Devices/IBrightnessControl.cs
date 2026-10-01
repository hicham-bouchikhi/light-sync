namespace LightSync.Core.Devices;

public interface IBrightnessControl
{
    /// <summary>Last reported or requested device brightness, as a percentage.</summary>
    int BrightnessPercent { get; }

    Task SetBrightnessAsync(int percent, CancellationToken cancellationToken);
}
