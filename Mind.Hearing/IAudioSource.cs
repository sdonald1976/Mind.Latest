namespace Mind.Hearing;

/// <summary>
/// A source of mono audio samples arriving in order, at a known rate. This is the
/// one seam the rest of hearing sits behind: a file today, a live microphone
/// later, both reduced to the same thing — a stream of samples in [-1, 1]. Nothing
/// downstream (the cochlea, the place-baseline) may know or care which it is, so
/// the live mic is never designed out.
/// </summary>
public interface IAudioSource
{
    /// <summary>Samples per second. Fixed for the life of the source.</summary>
    int SampleRate { get; }

    /// <summary>
    /// Fill <paramref name="buffer"/> with the next mono samples and return how many
    /// were written. Returns 0 when the source is exhausted (a file's end); a live
    /// source blocks until samples arrive. Fewer than requested is normal, not an error.
    /// </summary>
    int Read(Span<float> buffer);
}
