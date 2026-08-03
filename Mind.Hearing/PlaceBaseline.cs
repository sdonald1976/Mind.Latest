namespace Mind.Hearing;

/// <summary>
/// Holds the Mind's place-baseline over the auditory bundle and brackets the salient episodes that
/// depart from it. Each frame the Mind predicts the sound (its running expectation across every
/// channel — timbre, loudness, pitch, harmonicity, brightness), compares what arrives, and learns.
/// The surprise is a departure in <em>any</em> channel: new energy above the expected timbre, plus a
/// weighted change in loudness / pitch / harmonicity / brightness. Surprise above an adaptive
/// threshold opens an episode; a return to quiet, held long enough, closes it.
/// </summary>
/// <remarks>
/// Fed one <see cref="AuditoryFrame"/> at a time. <see cref="Observe"/> returns an episode at the
/// moment one closes, otherwise null; call <see cref="Flush"/> at end-of-stream to close any still
/// open. Timbre surprise is rectified (fires on sound appearing, so a sustained sound settles back
/// to idle — DESIGN.md decision 5); the extra channels use absolute departure, since a change in
/// pitch or brightness in either direction is a real event. Setting a channel's weight to 0 falls
/// back to the timbre-only behaviour.
/// </remarks>
public sealed class PlaceBaseline
{
    private readonly PlaceBaselineOptions _options;
    private readonly double _secondsPerFrame;
    private readonly long _holdFrames;
    private readonly double _minPitchHz;
    private readonly double _maxPitchHz;
    private readonly double _nyquistHz;

    private float[]? _melExpectation;   // the expected timbre — the hum of the place
    private float[] _scalarExpectation = []; // expected loudness, pitch, harmonicity, brightness
    private double _restingSurprise;    // slow level of surprise — the adaptive threshold's anchor
    private bool _restingPrimed;

    private long _frame;                // global frame index

    // Open-episode state.
    private bool _open;
    private long _openFrame;
    private long _lastAboveFrame;
    private double _peak;
    private double _sum;
    private int _aboveFrames;

    // The episode's accumulating acoustic character (averaged over its salient frames).
    private double _sumLoudness;
    private double _sumHarmonicity;
    private double _sumBrightness;
    private double _sumPitch;
    private int _voicedFrames;
    private double[]? _sumMel; // accumulating mean spectrum, for the sound-unit fingerprint

    public PlaceBaseline(
        PlaceBaselineOptions options,
        double secondsPerFrame,
        double minPitchHz = 70,
        double maxPitchHz = 400,
        double nyquistHz = 8000)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (secondsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsPerFrame), "Seconds per frame must be positive.");
        }

        _options = options;
        _secondsPerFrame = secondsPerFrame;
        _holdFrames = Math.Max(1, (long)Math.Round(options.HoldSeconds / secondsPerFrame));
        _minPitchHz = minPitchHz;
        _maxPitchHz = maxPitchHz;
        _nyquistHz = nyquistHz;
    }

    /// <summary>The most recent frame's total surprise.</summary>
    public double LastSurprise { get; private set; }

    /// <summary>The timbre (mel) part of the most recent surprise — for diagnostics.</summary>
    public double LastTimbreSurprise { get; private set; }

    /// <summary>The extra-channels part of the most recent surprise — for diagnostics.</summary>
    public double LastChannelSurprise { get; private set; }

    /// <summary>The most recent frame's salience threshold (floor or adaptive, whichever is higher).</summary>
    public double LastThreshold { get; private set; }

    /// <summary>Whether an episode is currently open.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Take in one auditory frame. Returns a <see cref="SalientEpisode"/> at the moment one closes,
    /// otherwise null.
    /// </summary>
    public SalientEpisode? Observe(AuditoryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var mel = frame.Mel;
        var scalars = frame.ScalarChannels(_minPitchHz, _maxPitchHz, _nyquistHz);

        // First frame: seed every expectation to what we hear, so there is no cold-start spike.
        if (_melExpectation is null)
        {
            _melExpectation = (float[])mel.Clone();
            _scalarExpectation = (float[])scalars.Clone();
            _sumMel = new double[mel.Length];
            LastSurprise = 0;
            LastTimbreSurprise = 0;
            LastChannelSurprise = 0;
            LastThreshold = _options.Floor;
            _frame++;
            return null;
        }

        if (mel.Length != _melExpectation.Length)
        {
            throw new ArgumentException(
                $"Mel width changed from {_melExpectation.Length} to {mel.Length}.", nameof(frame));
        }

        // Timbre: how much energy appeared *above* what was expected, per band (rectified).
        var timbre = 0.0;
        for (var b = 0; b < mel.Length; b++)
        {
            var over = mel[b] - _melExpectation[b];
            if (over > 0)
            {
                timbre += over;
            }
        }
        timbre /= mel.Length;

        // Extra channels: weighted absolute departure — a change either way is an event.
        var channels =
            _options.LoudnessWeight * Math.Abs(scalars[0] - _scalarExpectation[0]) +
            _options.PitchWeight * Math.Abs(scalars[1] - _scalarExpectation[1]) +
            _options.HarmonicityWeight * Math.Abs(scalars[2] - _scalarExpectation[2]) +
            _options.BrightnessWeight * Math.Abs(scalars[3] - _scalarExpectation[3]);

        var surprise = timbre + channels;
        var threshold = Math.Max(_options.Floor, _options.SpikeRatio * _restingSurprise);
        var above = surprise > threshold;

        LastSurprise = surprise;
        LastTimbreSurprise = timbre;
        LastChannelSurprise = channels;
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
                _sumLoudness = _sumHarmonicity = _sumBrightness = _sumPitch = 0;
                _voicedFrames = 0;
                Array.Clear(_sumMel!);
            }

            _lastAboveFrame = _frame;
            _peak = Math.Max(_peak, surprise);
            _sum += surprise;
            _aboveFrames++;

            _sumLoudness += frame.Loudness;
            _sumHarmonicity += frame.Harmonicity;
            _sumBrightness += frame.BrightnessHz;
            if (frame.Voiced)
            {
                _sumPitch += frame.PitchHz;
                _voicedFrames++;
            }
            for (var b = 0; b < mel.Length; b++)
            {
                _sumMel![b] += mel[b];
            }
        }
        else if (_open && _frame - _lastAboveFrame >= _holdFrames)
        {
            closed = CloseEpisode();
        }

        // Learn: every expectation tracks toward what arrived (so steady sound becomes the new
        // normal and settles to idle). The resting surprise tracks its own slow level.
        var leak = (float)_options.ExpectationLeak;
        for (var b = 0; b < mel.Length; b++)
        {
            _melExpectation[b] += leak * (mel[b] - _melExpectation[b]);
        }
        for (var c = 0; c < _scalarExpectation.Length; c++)
        {
            _scalarExpectation[c] += leak * (scalars[c] - _scalarExpectation[c]);
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
        var n = Math.Max(1, _aboveFrames);
        var character = new AuditoryCharacter(
            Loudness: (float)(_sumLoudness / n),
            Harmonicity: (float)(_sumHarmonicity / n),
            BrightnessHz: (float)(_sumBrightness / n),
            PitchHz: _voicedFrames > 0 ? (float)(_sumPitch / _voicedFrames) : 0f);

        var meanMel = new float[_sumMel!.Length];
        for (var b = 0; b < meanMel.Length; b++)
        {
            meanMel[b] = (float)(_sumMel[b] / n);
        }

        var episode = new SalientEpisode(
            Start: TimeSpan.FromSeconds(_openFrame * _secondsPerFrame),
            End: TimeSpan.FromSeconds(_lastAboveFrame * _secondsPerFrame),
            PeakSalience: _peak,
            MeanSalience: _aboveFrames > 0 ? _sum / _aboveFrames : 0,
            Frames: _aboveFrames,
            Character: character,
            MeanMel: meanMel);

        _open = false;
        return episode.Duration.TotalSeconds >= _options.MinEpisodeSeconds ? episode : null;
    }
}
