namespace Mind.Hearing;

/// <summary>
/// A fixed-length fingerprint of a salient episode's sound, so recurrences can be recognized as the
/// same unit. Implementations trade off what they keep — pitch, time, loudness — which is exactly
/// what we want to compare on real audio before committing to one.
/// </summary>
public interface IFingerprint
{
    /// <summary>Short name, for the tuner's side-by-side comparison.</summary>
    string Name { get; }

    /// <summary>Reduce an episode's mel frames to one fingerprint vector.</summary>
    float[] Compute(IReadOnlyList<float[]> episodeFrames);
}

/// <summary>
/// Average log-mel over the episode, unit-normalized. Simple, and keeps the raw spectral shape —
/// pitch and all. The baseline to beat: two utterances of the same word at different pitches look
/// different here.
/// </summary>
public sealed class MelAverageFingerprint : IFingerprint
{
    public string Name => "mel-avg";

    public float[] Compute(IReadOnlyList<float[]> episodeFrames) =>
        Vectors.NormalizeL2(Vectors.Mean(episodeFrames));
}

/// <summary>
/// Average MFCC over the episode, unit-normalized. Cepstral, so pitch is largely stripped — the same
/// sound pitched high or low lands close together. Coefficient 0 (overall energy) is dropped by
/// default so loudness is ignored too.
/// </summary>
public sealed class MfccFingerprint : IFingerprint
{
    private readonly Mfcc _mfcc;
    private readonly bool _dropEnergy;

    public MfccFingerprint(int bands, int coefficients = 13, bool dropEnergy = true)
    {
        _mfcc = new Mfcc(bands, coefficients);
        _dropEnergy = dropEnergy;
    }

    public string Name => "mfcc";

    public float[] Compute(IReadOnlyList<float[]> episodeFrames)
    {
        var start = _dropEnergy ? 1 : 0;
        var length = _mfcc.Coefficients - start;
        var acc = new float[length];
        if (episodeFrames.Count == 0)
        {
            return acc;
        }

        foreach (var frame in episodeFrames)
        {
            var coefficients = _mfcc.Transform(frame);
            for (var k = 0; k < length; k++)
            {
                acc[k] += coefficients[start + k];
            }
        }
        for (var k = 0; k < length; k++)
        {
            acc[k] /= episodeFrames.Count;
        }
        return Vectors.NormalizeL2(acc);
    }
}

/// <summary>
/// MFCCs pooled over a few equal slices of the episode (start, middle, end...) and concatenated — a
/// coarse <em>trajectory</em> instead of a single average, so a sound's evolution over time matters
/// ("bus" and "sub" stop looking identical). Costs a wider fingerprint; still pitch-robust.
/// </summary>
public sealed class MfccTrajectoryFingerprint : IFingerprint
{
    private readonly Mfcc _mfcc;
    private readonly int _segments;
    private readonly bool _dropEnergy;

    public MfccTrajectoryFingerprint(int bands, int coefficients = 13, int segments = 3, bool dropEnergy = true)
    {
        _mfcc = new Mfcc(bands, coefficients);
        _segments = Math.Max(1, segments);
        _dropEnergy = dropEnergy;
    }

    public string Name => $"mfcc-traj{_segments}";

    public float[] Compute(IReadOnlyList<float[]> episodeFrames)
    {
        var start = _dropEnergy ? 1 : 0;
        var length = _mfcc.Coefficients - start;
        var result = new float[length * _segments];

        var n = episodeFrames.Count;
        if (n == 0)
        {
            return result;
        }

        for (var s = 0; s < _segments; s++)
        {
            var lo = (int)((long)s * n / _segments);
            var hi = (int)((long)(s + 1) * n / _segments);
            if (hi <= lo)
            {
                hi = Math.Min(lo + 1, n);
            }

            var acc = new float[_mfcc.Coefficients];
            var count = 0;
            for (var i = lo; i < hi && i < n; i++)
            {
                var coefficients = _mfcc.Transform(episodeFrames[i]);
                for (var k = 0; k < coefficients.Length; k++)
                {
                    acc[k] += coefficients[k];
                }
                count++;
            }
            if (count > 0)
            {
                for (var k = 0; k < acc.Length; k++)
                {
                    acc[k] /= count;
                }
            }

            for (var k = 0; k < length; k++)
            {
                result[s * length + k] = acc[start + k];
            }
        }

        return Vectors.NormalizeL2(result);
    }
}
