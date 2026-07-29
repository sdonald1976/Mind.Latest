namespace Mind.Hearing;

/// <summary>
/// Drives an <see cref="IAudioSource"/> through a <see cref="Cochlea"/>, emitting one mel
/// vector per hop — the continuous vector stream the place-baseline will sit against. It
/// keeps a sliding window of the last <see cref="Cochlea.FftSize"/> samples and advances it
/// by the hop each step, so successive frames overlap the way a spectrogram's do.
/// </summary>
public sealed class HearingStream
{
    private readonly IAudioSource _source;
    private readonly Cochlea _cochlea;
    private readonly float[] _frame;
    private bool _primed;
    private long _frameCount;

    public HearingStream(IAudioSource source, Cochlea cochlea)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cochlea = cochlea ?? throw new ArgumentNullException(nameof(cochlea));
        _frame = new float[cochlea.FftSize];
    }

    public int Bands => _cochlea.Bands;

    /// <summary>Seconds of audio between successive frames (the hop).</summary>
    public double SecondsPerFrame => (double)_cochlea.HopSize / _cochlea.SampleRate;

    /// <summary>Start time of the most recent frame returned by <see cref="Next"/>.</summary>
    public TimeSpan FrameTime => TimeSpan.FromSeconds((_frameCount - 1) * SecondsPerFrame);

    /// <summary>
    /// The next mel vector, or null when the source can no longer fill a frame. The first call
    /// fills the whole window; later calls slide it forward by one hop.
    /// </summary>
    public float[]? Next()
    {
        if (!_primed)
        {
            if (!ReadFully(_frame))
            {
                return null;
            }
            _primed = true;
            _frameCount++;
            return _cochlea.Analyze(_frame);
        }

        var hop = _cochlea.HopSize;
        var keep = _frame.Length - hop;
        if (keep > 0)
        {
            Array.Copy(_frame, hop, _frame, 0, keep);
        }

        if (!ReadFully(_frame.AsSpan(keep)))
        {
            return null;
        }

        _frameCount++;
        return _cochlea.Analyze(_frame);
    }

    // Fill the whole span, looping until the source runs dry. False if it couldn't be filled.
    private bool ReadFully(Span<float> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var got = _source.Read(buffer[offset..]);
            if (got <= 0)
            {
                return false;
            }
            offset += got;
        }
        return true;
    }
}
