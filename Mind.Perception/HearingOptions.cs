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

    /// <summary>Where the sound comes from: <c>"file"</c> (default) or <c>"mic"</c> (a live microphone).</summary>
    public string Source { get; set; } = "file";

    /// <summary>Which capture device when <see cref="Source"/> is "mic". 0 is the system default input.</summary>
    public int MicDevice { get; set; }

    /// <summary>
    /// The audio file, when <see cref="Source"/> is "file" — an MP4's track is extracted to mono with
    /// ffmpeg. Required for file source; ignored for the microphone.
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
