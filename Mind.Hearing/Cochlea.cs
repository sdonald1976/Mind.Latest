namespace Mind.Hearing;

/// <summary>
/// The cochlea: a fixed, dumb front-end that reduces a frame of sound to a small mel
/// vector. Per frame it windows the samples (Hann), takes the power spectrum (FFT), sums
/// that power through a bank of triangular mel-scale filters, and log-compresses each — so
/// loudness adds the way hearing does, and resolution packs into the low frequencies where
/// speech and music live. It learns nothing; it just refuses to hand the Mind a raw
/// waveform. See DESIGN.md, "The first sense: audio".
/// </summary>
public sealed class Cochlea
{
    private readonly Fft _fft;
    private readonly float[] _window;    // Hann window over the frame
    private readonly float[] _windowed;  // scratch: the windowed frame
    private readonly float[] _power;     // scratch: the power spectrum
    private readonly float[][] _filters; // [MelBands] triangular weights over spectrum bins

    public Cochlea(CochleaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.FftSize < 2 || (options.FftSize & (options.FftSize - 1)) != 0)
        {
            throw new ArgumentException("Cochlea FftSize must be a power of two >= 2.", nameof(options));
        }
        if (options.HopSize <= 0 || options.HopSize > options.FftSize)
        {
            throw new ArgumentException("Cochlea HopSize must be in (0, FftSize].", nameof(options));
        }
        if (options.MelBands <= 0)
        {
            throw new ArgumentException("Cochlea MelBands must be positive.", nameof(options));
        }

        var nyquist = options.SampleRate / 2.0;
        var maxHz = Math.Min(options.MaxHz, nyquist);
        if (options.MinHz < 0 || options.MinHz >= maxHz)
        {
            throw new ArgumentException("Cochlea MinHz must be in [0, MaxHz).", nameof(options));
        }

        FftSize = options.FftSize;
        HopSize = options.HopSize;
        Bands = options.MelBands;
        SampleRate = options.SampleRate;

        _fft = new Fft(FftSize);
        _window = HannWindow(FftSize);
        _windowed = new float[FftSize];
        _power = new float[_fft.SpectrumBins];
        _filters = BuildMelFilters(Bands, _fft.SpectrumBins, SampleRate, options.MinHz, maxHz);
    }

    public int FftSize { get; }
    public int HopSize { get; }
    public int Bands { get; }
    public int SampleRate { get; }

    /// <summary>Reduce one frame (exactly <see cref="FftSize"/> samples) to a mel vector.</summary>
    public float[] Analyze(ReadOnlySpan<float> frame)
    {
        if (frame.Length != FftSize)
        {
            throw new ArgumentException($"Frame must be {FftSize} samples, got {frame.Length}.", nameof(frame));
        }

        for (var i = 0; i < FftSize; i++)
        {
            _windowed[i] = frame[i] * _window[i];
        }

        _fft.PowerSpectrum(_windowed, _power);

        var mel = new float[Bands];
        for (var b = 0; b < Bands; b++)
        {
            var weights = _filters[b];
            var sum = 0f;
            for (var k = 0; k < weights.Length; k++)
            {
                if (weights[k] != 0f)
                {
                    sum += weights[k] * _power[k];
                }
            }
            mel[b] = MathF.Log(1f + sum);
        }
        return mel;
    }

    private static float[] HannWindow(int size)
    {
        var window = new float[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        }
        return window;
    }

    private static float[][] BuildMelFilters(int bands, int bins, int sampleRate, double minHz, double maxHz)
    {
        // Equally spaced points on the mel scale, converted back to Hz then to FFT bins.
        // Bin k holds frequency k·sampleRate/FftSize, and bins-1 is Nyquist, so
        // bin = hz / (sampleRate/2) · (bins-1).
        var melMin = HzToMel(minHz);
        var melMax = HzToMel(maxHz);

        var points = new int[bands + 2];
        for (var i = 0; i < points.Length; i++)
        {
            var mel = melMin + (melMax - melMin) * i / (bands + 1);
            var hz = MelToHz(mel);
            var bin = (int)Math.Floor((bins - 1) * 2.0 * hz / sampleRate);
            points[i] = Math.Clamp(bin, 0, bins - 1);
        }

        var filters = new float[bands][];
        for (var b = 0; b < bands; b++)
        {
            var left = points[b];
            var center = points[b + 1];
            var right = points[b + 2];
            var weights = new float[bins];

            // Rising edge left→center, falling edge center→right. Degenerate (collided)
            // points simply leave a band empty — expected at the very low end where bins
            // are wider than mel steps.
            for (var k = left; k <= center && k < bins; k++)
            {
                if (center > left)
                {
                    weights[k] = (float)(k - left) / (center - left);
                }
            }
            for (var k = center; k <= right && k < bins; k++)
            {
                if (right > center)
                {
                    weights[k] = (float)(right - k) / (right - center);
                }
            }

            filters[b] = weights;
        }
        return filters;
    }

    private static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);

    private static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);
}
