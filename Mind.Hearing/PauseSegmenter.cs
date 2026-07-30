namespace Mind.Hearing;

/// <summary>
/// Cuts speech into word-sized pieces at the <em>pauses</em> between words — the brief dips to near
/// silence that separate spoken words in clear (especially child-directed) speech. A voiced run
/// bracketed by quiet is one word-ish piece. Keying on that gap structure also biases away from
/// continuous music and noise, which have no such gaps and stay one long run rather than fragmenting
/// into fake "words." It does NOT truly tell speech from music, though: background music that fills
/// the gaps between words defeats it — that is the honest hard case.
/// </summary>
public sealed class PauseSegmenter
{
    private readonly double _voiceFloor;
    private readonly int _minGap;
    private readonly int _minLength;
    private readonly int _maxLength;

    /// <param name="voiceFloor">Loudness (RMS) above which a frame counts as voiced, not a pause.</param>
    /// <param name="minGapFrames">How much quiet ends a word — the gap between words.</param>
    /// <param name="minLengthFrames">Shortest run worth keeping (drops lip-smacks and clicks).</param>
    /// <param name="maxLengthFrames">Longest a run may go before it's cut anyway (run-on / music safety).</param>
    public PauseSegmenter(double voiceFloor, int minGapFrames, int minLengthFrames, int maxLengthFrames)
    {
        _voiceFloor = voiceFloor;
        _minGap = Math.Max(1, minGapFrames);
        _minLength = Math.Max(1, minLengthFrames);
        _maxLength = Math.Max(_minLength, maxLengthFrames);
    }

    /// <summary>Segment a per-frame loudness signal into voiced runs separated by pauses.</summary>
    public IReadOnlyList<SoundSegment> Segment(IReadOnlyList<double> energy)
    {
        var segments = new List<SoundSegment>();
        var start = -1;
        var quiet = 0;

        for (var i = 0; i < energy.Count; i++)
        {
            if (energy[i] > _voiceFloor)
            {
                if (start < 0)
                {
                    start = i;
                }
                quiet = 0;

                // Safety cap: a run that never pauses (a long word run-on, or music) is cut anyway.
                if (i - start >= _maxLength)
                {
                    segments.Add(new SoundSegment(start, i));
                    start = i;
                }
            }
            else if (start >= 0)
            {
                quiet++;
                if (quiet >= _minGap)
                {
                    var end = i - quiet + 1; // close at the start of the pause, not its end
                    if (end - start >= _minLength)
                    {
                        segments.Add(new SoundSegment(start, end));
                    }
                    start = -1;
                }
            }
        }

        if (start >= 0 && energy.Count - start >= _minLength)
        {
            segments.Add(new SoundSegment(start, energy.Count));
        }

        return segments;
    }
}
