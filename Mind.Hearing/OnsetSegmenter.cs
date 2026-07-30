namespace Mind.Hearing;

/// <summary>A word/syllable-sized slice of the stream, as a frame range [Start, End).</summary>
public readonly record struct SoundSegment(int StartFrame, int EndFrame);

/// <summary>
/// Cuts continuous sound into word/syllable-sized segments at onset peaks in the surprise signal.
/// This is segmentation-by-surprise one level finer than memory-bracketing: within a voiced stretch,
/// surprise spikes at each syllable onset (reference project findings 016/018), and the run from one
/// onset to the next is a syllable-ish piece. Fingerprinting these — rather than a whole salient
/// phrase — is what moves sound-units from *speaker* grain toward *word* grain, so the Mind can begin
/// to tell apart the actual words being said.
/// </summary>
public sealed class OnsetSegmenter
{
    private readonly double _floor;
    private readonly int _minGap;
    private readonly int _maxLength;

    /// <param name="onsetFloor">Minimum surprise for a peak to count as an onset (ignore quiet ripples).</param>
    /// <param name="minGapFrames">Closest two onsets may be — the shortest syllable.</param>
    /// <param name="maxLengthFrames">Longest a segment may run — so a trailing silence isn't swallowed.</param>
    public OnsetSegmenter(double onsetFloor, int minGapFrames, int maxLengthFrames)
    {
        _floor = onsetFloor;
        _minGap = Math.Max(1, minGapFrames);
        _maxLength = Math.Max(1, maxLengthFrames);
    }

    /// <summary>Find onset peaks in the per-frame surprise, and return the segment between each.</summary>
    public IReadOnlyList<SoundSegment> Segment(IReadOnlyList<double> surprise)
    {
        var onsets = new List<int>();
        for (var i = 1; i < surprise.Count - 1; i++)
        {
            if (surprise[i] < _floor)
            {
                continue;
            }

            // A local maximum, spaced at least a syllable from the previous onset.
            if (surprise[i] >= surprise[i - 1] && surprise[i] > surprise[i + 1])
            {
                if (onsets.Count == 0 || i - onsets[^1] >= _minGap)
                {
                    onsets.Add(i);
                }
            }
        }

        var segments = new List<SoundSegment>(onsets.Count);
        for (var k = 0; k < onsets.Count; k++)
        {
            var start = onsets[k];
            var end = k + 1 < onsets.Count ? onsets[k + 1] : surprise.Count;
            end = Math.Min(end, start + _maxLength);
            if (end > start)
            {
                segments.Add(new SoundSegment(start, end));
            }
        }
        return segments;
    }
}
