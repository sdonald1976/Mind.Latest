namespace Mind.Hearing;

/// <summary>
/// Drives an <see cref="IAudioSource"/> through an <see cref="Ear"/>, emitting one
/// <see cref="AuditoryFrame"/> per hop — the richer counterpart to <see cref="HearingStream"/>
/// (which emits only mel). Same sliding-window discipline: keeps the last <see cref="Ear.FftSize"/>
/// samples and advances by the hop so successive frames overlap.
/// </summary>
public sealed class AuditoryStream
{
    private readonly IAudioSource _source;
    private readonly Ear _ear;
    private readonly float[] _frame;
    private bool _primed;

    public AuditoryStream(IAudioSource source, Ear ear)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ear = ear ?? throw new ArgumentNullException(nameof(ear));
        _frame = new float[ear.FftSize];
    }

    public int Bands => _ear.Bands;

    public double SecondsPerFrame => _ear.SecondsPerFrame;

    /// <summary>The next auditory frame, or null when the source can no longer fill a frame.</summary>
    public AuditoryFrame? Next()
    {
        if (!_primed)
        {
            if (!ReadFully(_frame))
            {
                return null;
            }
            _primed = true;
            return _ear.Hear(_frame);
        }

        var hop = _ear.HopSize;
        var keep = _frame.Length - hop;
        if (keep > 0)
        {
            Array.Copy(_frame, hop, _frame, 0, keep);
        }

        if (!ReadFully(_frame.AsSpan(keep)))
        {
            return null;
        }

        return _ear.Hear(_frame);
    }

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
