namespace Mind.Hearing;

/// <summary>
/// A stretch of sound that departed from the place-baseline — bracketed from the moment
/// departure rose above threshold to the last moment it was still above. This is what the
/// audio loop hands upward as a salient perception; the heartbeat brackets these into
/// memories. Times are offsets from the start of the stream.
/// </summary>
/// <param name="Start">When departure first rose above threshold.</param>
/// <param name="End">The last moment still above threshold (the hold that confirmed the close doesn't count).</param>
/// <param name="PeakSalience">The strongest departure reached during the episode.</param>
/// <param name="MeanSalience">The average departure across the episode's salient frames.</param>
/// <param name="Frames">How many frames were above threshold.</param>
public sealed record SalientEpisode(
    TimeSpan Start,
    TimeSpan End,
    double PeakSalience,
    double MeanSalience,
    int Frames)
{
    public TimeSpan Duration => End - Start;
}
