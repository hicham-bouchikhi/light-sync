namespace LightSync.Core.Audio;

public readonly record struct AudioFeatures(double Level, double Bass, double Mid, double Treble);

/// <summary>Reusable Hann-windowed radix-2 FFT. Channel powers are combined without phase cancellation.</summary>
public sealed class AudioAnalyzer
{
    public const int SpectrumBandCount = 32;

    private readonly int sampleRate;
    private readonly int size;
    private readonly double[] real;
    private readonly double[] imaginary;
    private readonly double[] window;
    private readonly int[] spectrumBins;
    private readonly double[] spectrum = new double[SpectrumBandCount];

    /// <summary>Reusable logarithmic RMS bins, overwritten by the next Analyze call.</summary>
    public ReadOnlyMemory<double> Spectrum => spectrum;

    public AudioAnalyzer(int sampleRate, int samplesPerChannel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (samplesPerChannel < 64 || !int.IsPow2(samplesPerChannel))
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerChannel), "FFT size must be a power of two, at least 64.");
        }

        this.sampleRate = sampleRate;
        size = samplesPerChannel;
        real = new double[size];
        imaginary = new double[size];
        window = new double[size];
        spectrumBins = new int[size / 2];
        var minimum = (double)sampleRate / size;
        var maximum = Math.Max(minimum * 2, Math.Min(16000, sampleRate / 2.0));
        for (var bin = 1; bin < spectrumBins.Length; bin++)
        {
            var frequency = (double)bin * sampleRate / size;
            spectrumBins[bin] = frequency > maximum ? -1 : Math.Clamp(
                (int)(Math.Log(frequency / minimum) / Math.Log(maximum / minimum) * SpectrumBandCount),
                0, SpectrumBandCount - 1);
        }
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1)));
        }
    }

    public AudioFeatures Analyze(ReadOnlySpan<float> samples, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (samples.Length != size * channels)
        {
            throw new ArgumentException("Audio block does not match the FFT size and channel count.", nameof(samples));
        }

        Array.Clear(spectrum);
        double total = 0, bass = 0, mid = 0, treble = 0;
        for (var channel = 0; channel < channels; channel++)
        {
            double mean = 0;
            for (var i = 0; i < size; i++)
            {
                mean += Clean(samples[(i * channels) + channel]);
            }

            mean /= size;
            for (var i = 0; i < size; i++)
            {
                var value = Clean(samples[(i * channels) + channel]) - mean;
                total += value * value;
                real[i] = value * window[i];
                imaginary[i] = 0;
            }

            Transform();
            for (var bin = 1; bin < size / 2; bin++)
            {
                var frequency = (double)bin * sampleRate / size;
                var power = (real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]);
                if (spectrumBins[bin] >= 0)
                {
                    spectrum[spectrumBins[bin]] += power;
                }
                if (frequency is >= 20 and < 250)
                {
                    bass += power;
                }
                else if (frequency is >= 250 and < 2000)
                {
                    mid += power;
                }
                else if (frequency is >= 2000 and <= 16000)
                {
                    treble += power;
                }
            }
        }

        // Hann mean-square is 3/8; double the positive-frequency power to include its mirror.
        var scale = 2.0 / (size * (double)size * 0.375 * channels);
        for (var i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = Math.Sqrt(spectrum[i] * scale);
        }
        return new AudioFeatures(
            Math.Sqrt(total / (size * channels)),
            Math.Sqrt(bass * scale), Math.Sqrt(mid * scale), Math.Sqrt(treble * scale));
    }

    private static double Clean(float sample) => float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;

    private void Transform()
    {
        for (int i = 1, j = 0; i < size; i++)
        {
            var bit = size >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var length = 2; length <= size; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepR = Math.Cos(angle);
            var stepI = Math.Sin(angle);
            for (var start = 0; start < size; start += length)
            {
                double wr = 1, wi = 0;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = start + offset;
                    var odd = even + (length / 2);
                    var tr = (wr * real[odd]) - (wi * imaginary[odd]);
                    var ti = (wr * imaginary[odd]) + (wi * real[odd]);
                    real[odd] = real[even] - tr;
                    imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr;
                    imaginary[even] += ti;
                    (wr, wi) = ((wr * stepR) - (wi * stepI), (wr * stepI) + (wi * stepR));
                }
            }
        }
    }
}
