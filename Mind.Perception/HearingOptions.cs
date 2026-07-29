using System.ComponentModel.DataAnnotations;

namespace Mind.Perception;

/// <summary>
/// Where the Mind's hearing gets its sound and how it is fed. The cochlea and place-baseline
/// have their own option sections (bound from <c>Cochlea</c> and <c>PlaceBaseline</c>); this
/// covers only the source. Everything configurable, per the standing rules.
/// </summary>
public sealed class HearingOptions
{
    public const string SectionName = "Hearing";

    /// <summary>Whether the Mind listens at all. Off by default so the service runs without a source.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The audio source. A media file today — an MP4's track is extracted to mono with ffmpeg —
    /// with a live microphone to follow behind the same seam. Required when <see cref="Enabled"/>.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>Sample rate the source is read at, in Hz.</summary>
    [Range(8_000, 48_000)]
    public int SampleRate { get; set; } = 16_000;

    /// <summary>
    /// Optional limit on how many seconds to take from the start — handy for hearing a slice
    /// rather than a whole 40-minute clip while we settle in. Null takes the whole source.
    /// </summary>
    public double? Seconds { get; set; }
}
