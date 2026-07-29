namespace Mind.Hearing;

/// <summary>
/// Holds the Mind's place-baseline over a mel stream and brackets the salient episodes that
/// depart from it. Each frame the Mind predicts the sound (its running expectation), compares
/// what arrives, and learns — nudging the expectation toward it. The surprise (new energy that
/// appeared above the expectation) is the salience signal. Surprise above an adaptive threshold
/// opens an episode; a return to quiet, held long enough, closes it.
/// </summary>
/// <remarks>
/// Fed one mel frame at a time. <see cref="Observe"/> returns an episode at the moment one
/// closes, otherwise null; call <see cref="Flush"/> at end-of-stream to close any still open.
///
/// Onset-shaped by design: surprise is rectified, so it fires on sound *appearing*, not
/// vanishing — a sustained, unchanging sound settles back to idle and closes its episode
/// (DESIGN.md decision 5: change makes memory, stillness does not). Salient cessation ("the
/// fridge stops") is a deliberate later refinement.
/// </remarks>
public sealed class PlaceBaseline
{
    private readonly PlaceBaselineOptions _options;
    private readonly double _secondsPerFrame;
    private readonly long _holdFrames;

    private float[]? _expectation;    // the predicted sound — the expected hum of the place
    private double _restingSurprise;  // slow level of surprise — the adaptive threshold's anchor
    private bool _restingPrimed;

    private long _frame;              // global frame index

    // Open-episode state.
    private bool _open;
    private long _openFrame;
    private long _lastAboveFrame;
    private double _peak;
    private double _sum;
    private int _aboveFrames;

    public PlaceBaseline(PlaceBaselineOptions options, double secondsPerFrame)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (secondsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsPerFrame), "Seconds per frame must be positive.");
        }

        _options = options;
        _secondsPerFrame = secondsPerFrame;
        _holdFrames = Math.Max(1, (long)Math.Round(options.HoldSeconds / secondsPerFrame));
    }

    /// <summary>The most recent frame's surprise (rectified departure from the expectation).</summary>
    public double LastSurprise { get; private set; }

    /// <summary>The most recent frame's salience threshold (floor or adaptive, whichever is higher).</summary>
    public double LastThreshold { get; private set; }

    /// <summary>Whether an episode is currently open.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Take in one mel frame. Returns a <see cref="SalientEpisode"/> at the moment one closes,
    /// otherwise null.
    /// </summary>
    public SalientEpisode? Observe(float[] mel)
    {
        ArgumentNullException.ThrowIfNull(mel);

        // First frame: seed the expectation to what we hear, so there is no cold-start spike
        // (a from-zero expectation would read the first real sound as a huge false onset).
        if (_expectation is null)
        {
            _expectation = (float[])mel.Clone();
            LastSurprise = 0;
            LastThreshold = _options.Floor;
            _frame++;
            return null;
        }

        if (mel.Length != _expectation.Length)
        {
            throw new ArgumentException(
                $"Mel width changed from {_expectation.Length} to {mel.Length}.", nameof(mel));
        }

        // Compare: how much energy appeared *above* what was expected, per band. Rectified, so
        // sound going quiet is not itself an onset — it just lets the episode settle closed.
        var surprise = 0.0;
        for (var b = 0; b < mel.Length; b++)
        {
            var over = mel[b] - _expectation[b];
            if (over > 0)
            {
                surprise += over;
            }
        }
        surprise /= mel.Length;

        var threshold = Math.Max(_options.Floor, _options.SpikeRatio * _restingSurprise);
        var above = surprise > threshold;

        LastSurprise = surprise;
        LastThreshold = threshold;

        SalientEpisode? closed = null;

        if (above)
        {
            if (!_open)
            {
                _open = true;
                _openFrame = _frame;
                _peak = surprise;
                _sum = 0;
                _aboveFrames = 0;
            }

            _lastAboveFrame = _frame;
            _peak = Math.Max(_peak, surprise);
            _sum += surprise;
            _aboveFrames++;
        }
        else if (_open && _frame - _lastAboveFrame >= _holdFrames)
        {
            closed = CloseEpisode();
        }

        // Learn: the expectation always tracks toward what arrived (so steady sound becomes the
        // new normal and settles to idle). The resting surprise tracks its own slow level, the
        // anchor the threshold scales from.
        for (var b = 0; b < mel.Length; b++)
        {
            _expectation[b] += (float)(_options.ExpectationLeak * (mel[b] - _expectation[b]));
        }

        if (!_restingPrimed)
        {
            _restingSurprise = surprise;
            _restingPrimed = true;
        }
        else
        {
            _restingSurprise += _options.RestingLeak * (surprise - _restingSurprise);
        }

        _frame++;
        return closed;
    }

    /// <summary>Close any episode still open at end-of-stream, so nothing salient is lost.</summary>
    public SalientEpisode? Flush() => _open ? CloseEpisode() : null;

    // Build the episode and reset the open state. Returns null for an episode shorter than the
    // minimum — a momentary flicker, not a real event — so the caller emits nothing.
    private SalientEpisode? CloseEpisode()
    {
        var episode = new SalientEpisode(
            Start: TimeSpan.FromSeconds(_openFrame * _secondsPerFrame),
            End: TimeSpan.FromSeconds(_lastAboveFrame * _secondsPerFrame),
            PeakSalience: _peak,
            MeanSalience: _aboveFrames > 0 ? _sum / _aboveFrames : 0,
            Frames: _aboveFrames);

        _open = false;
        return episode.Duration.TotalSeconds >= _options.MinEpisodeSeconds ? episode : null;
    }
}
