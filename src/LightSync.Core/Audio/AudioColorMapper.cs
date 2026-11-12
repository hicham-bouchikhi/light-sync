using LightSync.Core.Colors;
using LightSync.Core.Configuration;

namespace LightSync.Core.Audio;

public sealed class AudioColorMapper
{
    private AudioConfig options = new();
    private double phase;
    private double previousBass;

    public AudioColorMapper(AudioConfig options)
    {
        UpdateOptions(options);
    }

    public void UpdateOptions(AudioConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate().Count > 0)
        {
            throw new ArgumentException("Invalid audio configuration.", nameof(options));
        }

        Volatile.Write(ref this.options, options with { });
    }

    /// <summary>Advance once per captured block, before mapping every device.</summary>
    public void Advance(AudioFeatures features, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        var current = Volatile.Read(ref options);
        if (features.Level > current.NoiseGate)
        {
            // Colour travel uses raw energy and bass attacks, independently of gain/clipping.
            var attack = Math.Max(0, features.Bass - previousBass);
            phase = (phase + current.Motion * ((seconds * (0.08 + features.Level)) + (attack * 0.4))) % 1;
        }

        previousBass = features.Bass;
    }

    /// <summary>Updates the caller's persistent frame. Silence fades fully to black.</summary>
    public void Map(AudioFeatures features, Span<RgbColor> frame)
    {
        var options = Volatile.Read(ref this.options);
        var audible = features.Level > options.NoiseGate;
        for (var i = 0; i < frame.Length; i++)
        {
            RgbColor target;
            if (!audible)
            {
                target = RgbColor.Black;
            }
            else if (options.Mode == "rainbow")
            {
                // A soft shoulder retains intensity differences beyond the old hard clipping point.
                var intensity = features.Level * options.Gain;
                var level = intensity <= 0.5 ? intensity : 1 - (0.25 / intensity);
                var bands = features.Bass + features.Mid + features.Treble;
                var tone = bands > 0 ? features.Treble / bands : 0;
                var position = frame.Length == 1 ? 0 : (double)i / frame.Length;
                target = ColorMath.Rainbow(phase + position + (tone * 0.2), level * options.Brightness);
            }
            else if (options.Mode == "spectrum")
            {
                if (frame.Length == 1)
                {
                    target = new RgbColor(ToByte(features.Bass * options.Gain * options.Brightness),
                        ToByte(features.Mid * options.Gain * options.Brightness), ToByte(features.Treble * options.Gain * options.Brightness));
                    frame[i] = ColorMath.Smooth(frame[i], target, options.Smoothing);
                    continue;
                }

                // Each strip displays the same low -> high frequency gradient, regardless of length.
                var position = frame.Length == 1 ? 0.5 : (double)i / (frame.Length - 1);
                var bassWeight = Math.Max(0, 1 - (position * 2));
                var midWeight = 1 - Math.Abs((position * 2) - 1);
                var trebleWeight = Math.Max(0, (position * 2) - 1);
                target = new RgbColor(
                    ToByte(features.Bass * options.Gain * bassWeight * options.Brightness),
                    ToByte(features.Mid * options.Gain * midWeight * options.Brightness),
                    ToByte(features.Treble * options.Gain * trebleWeight * options.Brightness));
            }
            else
            {
                var level = Math.Clamp((options.Mode == "bass" ? features.Bass : features.Level) * options.Gain, 0, 1);
                target = new RgbColor(
                    ToByte(options.Color.R / 255.0 * level * options.Brightness),
                    ToByte(options.Color.G / 255.0 * level * options.Brightness),
                    ToByte(options.Color.B / 255.0 * level * options.Brightness));
            }

            frame[i] = ColorMath.Smooth(frame[i], target, options.Smoothing);
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
