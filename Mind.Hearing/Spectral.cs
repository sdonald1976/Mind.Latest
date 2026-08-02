namespace Mind.Hearing;

/// <summary>
/// Cheap, fixed descriptors of a frame: loudness (RMS) and brightness (spectral centroid). Both
/// learn nothing — they just read what's in the signal. Part of the auditory-nerve bundle.
/// </summary>
public static class Spectral
{
    /// <summary>RMS loudness of a frame — 0..1 for samples in [-1, 1].</summary>
    public static float Rms(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0)
        {
            return 0f;
        }

        double sum = 0;
        foreach (var x in frame)
        {
            sum += (double)x * x;
        }
        return (float)Math.Sqrt(sum / frame.Length);
    }

    /// <summary>
    /// Spectral centroid (brightness) in Hz — the power-weighted mean frequency of the spectrum. Low =
    /// dark/muffled, high = bright/sharp (sibilants, cymbals). Reads the same power spectrum the mel
    /// bands were summed from.
    /// </summary>
    public static float Centroid(ReadOnlySpan<float> power, int sampleRate, int fftSize)
    {
        double weighted = 0, total = 0;
        for (var k = 0; k < power.Length; k++)
        {
            var frequency = (double)k * sampleRate / fftSize;
            weighted += frequency * power[k];
            total += power[k];
        }
        return total > 1e-12 ? (float)(weighted / total) : 0f;
    }
}
