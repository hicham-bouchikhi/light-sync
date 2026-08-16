namespace LightSync.Core.Capture;

/// <summary>
/// One frame of BGRx pixels. <see cref="Pixels"/> points at a pooled buffer owned by the
/// capture backend and is only valid until the next frame is requested, so consumers must
/// finish with it synchronously rather than storing it.
/// </summary>
public readonly record struct CapturedFrame(
    ReadOnlyMemory<byte> Pixels,
    int Width,
    int Height,
    long Sequence,
    TimeSpan Timestamp)
{
    public const int BytesPerPixel = 4;

    public int Stride => Width * BytesPerPixel;

    public bool IsEmpty => Width == 0 || Height == 0;
}
