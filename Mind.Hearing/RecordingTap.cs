namespace Mind.Hearing;

/// <summary>
/// A pass-through <see cref="IAudioSource"/> that keeps a rolling buffer of the most recent raw
/// samples as they flow by — so a salient episode's audio can be sliced back out after the fact and
/// saved as a listenable clip. It changes nothing downstream: the cochlea, ear, and place-baseline
/// read exactly the samples they always would; this just remembers them for a short while.
/// </summary>
/// <remarks>
/// Single-threaded by contract: <see cref="Read"/> and <see cref="Slice"/> are both called on the
/// one audio loop thread, so the ring needs no lock. Samples older than the ring's capacity are
/// overwritten — a request for them comes back as much as is still retained, never an error.
/// </remarks>
public sealed class RecordingTap : IAudioSource, IDisposable
{
    private readonly IAudioSource _inner;
    private readonly float[] _ring;
    private long _total; // global index of the next sample to be written == count read so far

    public RecordingTap(IAudioSource inner, int capacitySamples)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (capacitySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitySamples), "Capacity must be positive.");
        }

        _ring = new float[capacitySamples];
    }

    public int SampleRate => _inner.SampleRate;

    public int Read(Span<float> buffer)
    {
        var got = _inner.Read(buffer);
        for (var i = 0; i < got; i++)
        {
            _ring[(int)((_total + i) % _ring.Length)] = buffer[i];
        }
        _total += got > 0 ? got : 0;
        return got;
    }

    /// <summary>
    /// The retained samples for the global index range [<paramref name="fromSample"/>,
    /// <paramref name="toSample"/>), clamped to what is still in the ring. Empty if the range is
    /// entirely in the past or in the future.
    /// </summary>
    public float[] Slice(long fromSample, long toSample)
    {
        var oldest = Math.Max(0, _total - _ring.Length);
        var from = Math.Clamp(fromSample, oldest, _total);
        var to = Math.Clamp(toSample, oldest, _total);
        var length = (int)(to - from);
        if (length <= 0)
        {
            return [];
        }

        var clip = new float[length];
        for (var i = 0; i < length; i++)
        {
            clip[i] = _ring[(int)((from + i) % _ring.Length)];
        }
        return clip;
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
