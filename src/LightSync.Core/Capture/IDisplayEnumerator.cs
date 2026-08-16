namespace LightSync.Core.Capture;

public interface IDisplayEnumerator
{
    Task<IReadOnlyList<DisplayInfo>> GetDisplaysAsync(CancellationToken cancellationToken);
}
