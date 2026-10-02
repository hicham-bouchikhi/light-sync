using LightSync.Core.Configuration;

namespace LightSync.Core.Audio;

public readonly record struct ScreenBrightnessStatus(double Brightness, double Decibels,
    bool HasAudio, string? AudioError);

/// <summary>One smoothed playback envelope shared by every screen output and its preview.</summary>
public sealed class ScreenBrightness
{
    private const double QuietDecibels = -45;
    private const double LoudDecibels = -6;
    private readonly Lock gate = new();
    private ScreenConfig options;
    private double intensity;
    private double decibels = -120;
    private bool hasAudio;
    private string? audioError;
    private long frameSequence = -1;
    private bool hasFrame;
    private double frameBrightness;

    public ScreenBrightness(ScreenConfig options)
    {
        Validate(options);
        this.options = options with { };
    }

    public ScreenBrightnessStatus Status
    {
        get
        {
            lock (gate)
            {
                return new(CurrentBrightness, decibels, hasAudio, audioError);
            }
        }
    }

    public void UpdateOptions(ScreenConfig value)
    {
        Validate(value);
        lock (gate)
        {
            if (value.AudioIntensityEnabled != options.AudioIntensityEnabled)
            {
                ResetAudioState(null);
            }
            options = value with { };
        }
    }

    public void UpdateAudio(ReadOnlySpan<float> samples, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        double power = 0;
        foreach (var sample in samples)
        {
            var clean = float.IsFinite(sample) ? Math.Clamp((double)sample, -1, 1) : 0;
            power += clean * clean;
        }
        var rms = samples.IsEmpty ? 0 : Math.Sqrt(power / samples.Length);
        var level = 20 * Math.Log10(Math.Max(rms, 0.000001));
        var target = Math.Clamp((level - QuietDecibels) / (LoudDecibels - QuietDecibels), 0, 1);
        lock (gate)
        {
            if (!options.AudioIntensityEnabled)
            {
                return;
            }
            // Quick attacks retain dramatic moments; a slower release avoids flicker.
            var timeConstant = target > intensity ? 0.08 : 0.35;
            intensity += (target - intensity) * (1 - Math.Exp(-elapsedSeconds / timeConstant));
            decibels = level;
            hasAudio = true;
            audioError = null;
        }
    }

    public void ResetAudio(string? error = null)
    {
        lock (gate)
        {
            ResetAudioState(error);
        }
    }

    /// <summary>All outputs processing the same captured frame receive the same brightness.</summary>
    public double ForFrame(long sequence)
    {
        lock (gate)
        {
            if (!hasFrame || frameSequence != sequence)
            {
                frameBrightness = CurrentBrightness;
                frameSequence = sequence;
                hasFrame = true;
            }
            return frameBrightness;
        }
    }

    private double CurrentBrightness => options.AudioIntensityEnabled && options.Brightness > 0
        ? options.Brightness + ((1 - options.Brightness) * intensity) : options.Brightness;

    private void ResetAudioState(string? error)
    {
        intensity = 0;
        decibels = -120;
        hasAudio = false;
        audioError = error;
    }

    private static void Validate(ScreenConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate() is { Count: > 0 } problems)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(options));
        }
    }
}
