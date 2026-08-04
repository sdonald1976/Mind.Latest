namespace Mind.Perception;

/// <summary>
/// The catalogue entry for one saved sensory clip. The audio itself is a WAV file on disk (small, and
/// playable in anything); this row is the index into it — what unit it was, when, how long — plus a
/// <see cref="Label"/> a human fills in later to teach the Mind what the sound is. That label is the
/// point: it's the supervised signal grounding turns into meaning.
/// </summary>
public sealed class StoredClip
{
    public Guid Id { get; set; }

    /// <summary>The sound-unit this clip was matched to, if any — the thing you'd label once, for all its clips.</summary>
    public int? Unit { get; set; }

    public DateTimeOffset CapturedAt { get; set; }
    public double Seconds { get; set; }
    public int SampleRate { get; set; }

    /// <summary>Where the WAV lives on this machine. Clips are local to the machine that heard them.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>What the sound is, once a human says so. Null until taught.</summary>
    public string? Label { get; set; }

    public DateTimeOffset? LabeledAt { get; set; }
}
