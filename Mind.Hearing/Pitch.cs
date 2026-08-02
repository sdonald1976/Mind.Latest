namespace Mind.Hearing;

/// <summary>
/// Estimates pitch (fundamental frequency) and harmonicity from a frame, by normalized
/// autocorrelation: a voiced sound repeats at its pitch period, so the lag with the strongest
/// self-similarity is the period, and the height of that peak is how tonal (voice/note) vs. noisy
/// (hiss/clatter) the sound is. Cheap and learning-nothing — one computation yields both channels.
/// </summary>
/// <remarks>
/// Caveat: bare autocorrelation makes octave errors (a strong peak at half or double the true
/// period). Good enough to hear melody and voicing; not a hardened pitch tracker. YIN would reduce
/// the octave errors if this ever needs to be sharper.
/// </remarks>
public static class Pitch
{
    /// <summary>
    /// Detect (pitch in Hz, harmonicity 0..1). Pitch is 0 when the frame is unvoiced (harmonicity
    /// below <paramref name="voicingThreshold"/>); harmonicity is still reported so callers can tell
    /// tonal from noisy even when there's no clear pitch.
    /// </summary>
    public static (float Hz, float Harmonicity) Detect(
        ReadOnlySpan<float> frame,
        int sampleRate,
        double minHz = 70,
        double maxHz = 400,
        double voicingThreshold = 0.3)
    {
        var minLag = Math.Max(1, (int)(sampleRate / maxHz));
        var maxLag = Math.Min(frame.Length - 1, (int)(sampleRate / minHz));
        if (maxLag <= minLag)
        {
            return (0f, 0f);
        }

        var bestNac = 0.0;
        var bestLag = 0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            double dot = 0, head = 0, tail = 0;
            var n = frame.Length - lag;
            for (var i = 0; i < n; i++)
            {
                double a = frame[i];
                double b = frame[i + lag];
                dot += a * b;
                head += a * a;
                tail += b * b;
            }

            var denom = Math.Sqrt(head * tail);
            var nac = denom > 1e-9 ? dot / denom : 0; // normalized cross-correlation, in [-1, 1]
            if (nac > bestNac)
            {
                bestNac = nac;
                bestLag = lag;
            }
        }

        var harmonicity = (float)Math.Clamp(bestNac, 0, 1);
        if (bestLag == 0 || bestNac < voicingThreshold)
        {
            return (0f, harmonicity);
        }
        return ((float)sampleRate / bestLag, harmonicity);
    }
}
