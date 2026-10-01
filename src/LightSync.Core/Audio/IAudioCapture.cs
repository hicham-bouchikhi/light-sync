namespace LightSync.Core.Audio;

public interface IAudioCapture : IAsyncDisposable
{
    int SampleRate { get; }

    int Channels { get; }

    int SamplesPerChannel { get; }

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Interleaved samples, reused on the next read. Empty means end of stream.</summary>
    Task<ReadOnlyMemory<float>> ReadAsync(CancellationToken cancellationToken);
}
