namespace Mind.Hearing;

/// <summary>
/// How the cochlea turns sound into vectors. Everything that shapes the front-end lives
/// here, meant to be bound from configuration and tuned per input — no magic numbers
/// baked into the code. Defaults are a sensible starting point for 16 kHz speech/music,
/// not settled truth; the tuner exists to move them.
/// </summary>
public sealed class CochleaOptions
{
    public const string SectionName = "Cochlea";

    /// <summary>Samples per second of the incoming audio.</summary>
    public int SampleRate { get; set; } = 16_000;

    /// <summary>FFT window, in samples (power of two). 512 ≈ 32 ms at 16 kHz.</summary>
    public int FftSize { get; set; } = 512;

    /// <summary>How far the window slides each step, in samples. 160 ≈ 10 ms → 100 frames/sec.</summary>
    public int HopSize { get; set; } = 160;

    /// <summary>Number of mel bands — the width of each vector the cochlea emits.</summary>
    public int MelBands { get; set; } = 20;

    /// <summary>Lowest frequency the mel filterbank covers, in Hz.</summary>
    public double MinHz { get; set; } = 50;

    /// <summary>Highest frequency the mel filterbank covers, in Hz (clamped to Nyquist).</summary>
    public double MaxHz { get; set; } = 8_000;
}
