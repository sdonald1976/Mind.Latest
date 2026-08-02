namespace Mind.Hearing;

/// <summary>
/// How the Mind holds its place-baseline and decides what departs from it. Everything that
/// shapes salience lives here, meant to be bound from configuration and tuned per input — no
/// magic numbers. Defaults are a starting point, not settled truth; the tuner exists to move
/// them against real material.
/// </summary>
/// <remarks>
/// The model is predict → compare → learn: an expectation of the sound is held and always
/// tracks toward what arrives; salience is the surprise (how much new energy appeared above
/// that expectation). A slow expectation reads departures from a steady place-hum (a quiet
/// room); a faster one reads onsets and transitions in busy sound. It is the same knob.
/// </remarks>
public sealed class PlaceBaselineOptions
{
    public const string SectionName = "PlaceBaseline";

    /// <summary>
    /// How fast the expectation follows the sound, per frame. Small is slow (~1/leak frames of
    /// memory). This is the "how quickly does stillness become the new normal" knob: too fast and
    /// it swallows the very thing it should notice (the mean-tracking trap); too slow and steady
    /// sound never settles to idle.
    /// </summary>
    public double ExpectationLeak { get; set; } = 0.05;

    /// <summary>
    /// How fast the resting level of surprise follows, per frame — the anchor the adaptive
    /// threshold scales from. Slow, so the threshold reflects the quiet baseline of change, not
    /// the events themselves.
    /// </summary>
    public double RestingLeak { get; set; } = 0.005;

    /// <summary>A frame is salient when its surprise exceeds this multiple of the resting surprise.</summary>
    public double SpikeRatio { get; set; } = 2.5;

    /// <summary>A floor under the threshold, so tiny surprises in near-silence never trip an episode.</summary>
    public double Floor { get; set; } = 0.05;

    /// <summary>How long surprise must stay below threshold before an open episode is closed, in seconds.</summary>
    public double HoldSeconds { get; set; } = 0.4;

    /// <summary>
    /// Shortest episode worth reporting, in seconds (the span from first to last salient frame).
    /// Momentary single-frame twitches shorter than this are dropped, so a salient episode is a
    /// real event and not a flicker. Set to 0 to report everything.
    /// </summary>
    public double MinEpisodeSeconds { get; set; } = 0.08;

    // How much each extra auditory channel adds to salience on top of timbre (mel). A change in
    // pitch, loudness, harmonicity, or brightness — even at constant timbre — becomes a departure
    // worth noticing; this is what makes the place-baseline multi-dimensional. 0 disables a channel
    // (mel-only, the old behaviour). Tuned per input like everything else.

    /// <summary>Weight of a loudness change in salience.</summary>
    public double LoudnessWeight { get; set; } = 0.4;

    /// <summary>Weight of a pitch (melody / voice) change in salience.</summary>
    public double PitchWeight { get; set; } = 0.4;

    /// <summary>Weight of a harmonicity change (voice↔noise) in salience.</summary>
    public double HarmonicityWeight { get; set; } = 0.4;

    /// <summary>Weight of a brightness change in salience.</summary>
    public double BrightnessWeight { get; set; } = 0.4;
}
