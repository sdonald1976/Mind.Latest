using System.Globalization;
using Mind.Hearing;

// A tiny offline bench for the Mind's hearing. Point it at a media file (MP4 or WAV) and it
// shows what the Mind takes in — the ingestion, the cochlea's mel stream, and the salient
// episodes the place-baseline brackets — so the whole chain can be seen and tuned on real
// material before it is wired into the always-on service.
//
//   Mind.Hearing.Tuner <media-file> [seconds] [sampleRate]
//        [--leak=0.05] [--restingLeak=0.005] [--ratio=2.5] [--floor=0.05] [--hold=0.4] [--minEpisode=0.08]
//        [--vigilance=0.9] [--units=64] [--trajSegments=3] [--exemplars=<dir>]  (dir: clips + index.html)
//        [--wLoud=0.4] [--wPitch=0.4] [--wHarm=0.4] [--wBright=0.4]  (multi-dim salience channel weights)
//        [--words | --words=onset]  cut words at pauses (default) or at onset peaks
//        [--voiceFloor=0.02] [--gap=80] [--minWord=100] [--maxWord=800]  (pause mode, ms)
//        [--wordFloor=0.15] [--wordMinGap=120] [--wordMaxLen=400]  (onset mode, ms)

var positionals = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (positionals.Length == 0)
{
    Console.Error.WriteLine(
        "usage: Mind.Hearing.Tuner <media-file> [seconds] [sampleRate] " +
        "[--leak=] [--restingLeak=] [--ratio=] [--floor=] [--hold=]");
    return 1;
}

string? Flag(string name)
{
    var prefix = $"--{name}=";
    return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}

double FlagOr(string name, double fallback) =>
    Flag(name) is { } v && double.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : fallback;

var path = positionals[0];
double? seconds = positionals.Length > 1 && double.TryParse(positionals[1], CultureInfo.InvariantCulture, out var s)
    ? s
    : null;
var rate = positionals.Length > 2 && int.TryParse(positionals[2], out var r) ? r : 16_000;

var fromMic = path.Equals("mic", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(fromMic ? "Source: microphone" : $"Loading: {Path.GetFileName(path)}");
Console.WriteLine($"  target rate : {rate} Hz{(seconds is { } limit ? $"   (first {limit:0.#}s)" : "")}");

FileAudioSource source;
if (fromMic)
{
    // Record a fixed slice from the mic, then analyse it exactly like a file.
    var micSeconds = seconds ?? 15;
    Console.WriteLine($"  recording {micSeconds:0.#}s — make some noise (talk, clap, go quiet)...");
    try
    {
        using var mic = new MicAudioSource(rate);
        var total = (int)(micSeconds * rate);
        var captured = new float[total];
        var got = 0;
        while (got < total)
        {
            var n = mic.Read(captured.AsSpan(got));
            if (n <= 0)
            {
                break;
            }
            got += n;
        }
        source = FileAudioSource.FromSamples(got == total ? captured : captured[..got], rate);
        Console.WriteLine($"  captured {got:N0} samples ({(double)got / rate:0.0}s)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"MIC FAILED: {ex.Message}");
        return 1;
    }
}
else
{
    try
    {
        source = FileAudioSource.Load(path, rate, seconds);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAILED: {ex.Message}");
        return 1;
    }
}

var samples = source.Samples;
var count = samples.Length;

// Peak and RMS over the whole signal — the crudest "is there real sound here" check.
var peak = 0f;
var sumSquares = 0.0;
foreach (var x in samples)
{
    var magnitude = Math.Abs(x);
    if (magnitude > peak)
    {
        peak = magnitude;
    }
    sumSquares += (double)x * x;
}
var rms = count > 0 ? Math.Sqrt(sumSquares / count) : 0;

Console.WriteLine($"  samples     : {count:N0}");
Console.WriteLine($"  duration    : {source.Duration:hh\\:mm\\:ss\\.fff}");
Console.WriteLine($"  peak        : {peak:0.000}");
Console.WriteLine($"  rms         : {rms:0.000}");

// --- The cochlea: samples -> mel-vector stream, the small signal the place-baseline sits
//     against. Loudness alone can't tell a loud-but-expected song from a novel sound. ---
source.Reset();
var cochlea = new Cochlea(new CochleaOptions { SampleRate = rate });
var ear = new Ear(cochlea);
var stream = new AuditoryStream(source, ear);

var auditoryFrames = new List<AuditoryFrame>();
while (stream.Next() is { } auditoryFrame)
{
    auditoryFrames.Add(auditoryFrame);
}
var frames = auditoryFrames.Select(f => f.Mel).ToList(); // mel view, for the melgram and units

Console.WriteLine();
Console.WriteLine(
    $"cochlea: {cochlea.Bands} mel bands, {cochlea.FftSize}-pt FFT, hop {cochlea.HopSize} " +
    $"(~{stream.SecondsPerFrame * 1000:0} ms/frame) -> {frames.Count:N0} frames");

// --- The place-baseline: predict the sound, be surprised by what departs, bracket the
//     episodes. This is the point of the whole piece. Knobs come from flags so we can sweep. ---
var options = new PlaceBaselineOptions
{
    ExpectationLeak = FlagOr("leak", new PlaceBaselineOptions().ExpectationLeak),
    RestingLeak = FlagOr("restingLeak", new PlaceBaselineOptions().RestingLeak),
    SpikeRatio = FlagOr("ratio", new PlaceBaselineOptions().SpikeRatio),
    Floor = FlagOr("floor", new PlaceBaselineOptions().Floor),
    HoldSeconds = FlagOr("hold", new PlaceBaselineOptions().HoldSeconds),
    MinEpisodeSeconds = FlagOr("minEpisode", new PlaceBaselineOptions().MinEpisodeSeconds),
    LoudnessWeight = FlagOr("wLoud", new PlaceBaselineOptions().LoudnessWeight),
    PitchWeight = FlagOr("wPitch", new PlaceBaselineOptions().PitchWeight),
    HarmonicityWeight = FlagOr("wHarm", new PlaceBaselineOptions().HarmonicityWeight),
    BrightnessWeight = FlagOr("wBright", new PlaceBaselineOptions().BrightnessWeight),
};

var detector = new PlaceBaseline(options, stream.SecondsPerFrame, ear.MinPitchHz, ear.MaxPitchHz, ear.NyquistHz);
var episodes = new List<SalientEpisode>();
var surprises = new double[auditoryFrames.Count];
double sumTimbre = 0, sumChannel = 0;
for (var i = 0; i < auditoryFrames.Count; i++)
{
    if (detector.Observe(auditoryFrames[i]) is { } episode)
    {
        episodes.Add(episode);
    }
    surprises[i] = detector.LastSurprise;
    sumTimbre += detector.LastTimbreSurprise;
    sumChannel += detector.LastChannelSurprise;
}
if (detector.Flush() is { } tail)
{
    episodes.Add(tail);
}

double minSurprise = double.MaxValue, maxSurprise = 0, sumSurprise = 0;
foreach (var value in surprises)
{
    if (value < minSurprise) minSurprise = value;
    if (value > maxSurprise) maxSurprise = value;
    sumSurprise += value;
}
if (surprises.Length == 0) minSurprise = 0;

Console.WriteLine();
Console.WriteLine(
    $"place-baseline: leak {options.ExpectationLeak}, spike x{options.SpikeRatio}, floor {options.Floor}, " +
    $"hold {options.HoldSeconds}s, minEpisode {options.MinEpisodeSeconds}s");
Console.WriteLine(
    $"  channel weights: loud {options.LoudnessWeight}, pitch {options.PitchWeight}, " +
    $"harm {options.HarmonicityWeight}, bright {options.BrightnessWeight}");
Console.WriteLine(
    $"  surprise: min {minSurprise:0.000}  mean {(surprises.Length > 0 ? sumSurprise / surprises.Length : 0):0.000}  " +
    $"max {maxSurprise:0.000}   -> {episodes.Count} salient episode(s)");
Console.WriteLine(
    $"  of which (mean): timbre {(surprises.Length > 0 ? sumTimbre / surprises.Length : 0):0.000}  " +
    $"channels {(surprises.Length > 0 ? sumChannel / surprises.Length : 0):0.000}");

if (frames.Count > 0)
{
    // Aggregate to ~0.5s rows and shade each band by its share of the run's peak, so structure
    // is visible: low bands light up for voice and song, high bands for consonants and effects,
    // quiet stretches go dark. Show each row's mean surprise and mark rows inside a salient
    // episode, so we can see whether salience fires on the real onsets and rests in the gaps.
    var framesPerRow = Math.Max(1, (int)Math.Round(0.5 / stream.SecondsPerFrame));
    var bands = cochlea.Bands;

    var melPeak = 0f;
    foreach (var frame in frames)
    {
        foreach (var value in frame)
        {
            if (value > melPeak)
            {
                melPeak = value;
            }
        }
    }
    if (melPeak <= 0f)
    {
        melPeak = 1f;
    }

    const string ramp = " .:-=+*#%@";
    Console.WriteLine();
    Console.WriteLine("mel spectrogram (each row ~0.5s; low freq -> high, left -> right):");

    var row = 0;
    for (var i = 0; i < frames.Count; i += framesPerRow)
    {
        var end = Math.Min(i + framesPerRow, frames.Count);
        var line = new char[bands];
        for (var b = 0; b < bands; b++)
        {
            var sum = 0f;
            for (var j = i; j < end; j++)
            {
                sum += frames[j][b];
            }
            var norm = sum / (end - i) / melPeak;
            var index = (int)Math.Clamp(norm * (ramp.Length - 1), 0, ramp.Length - 1);
            line[b] = ramp[index];
        }

        var rowStart = i * stream.SecondsPerFrame;
        var rowEnd = end * stream.SecondsPerFrame;
        var surprise = 0.0;
        for (var j = i; j < end; j++)
        {
            surprise += surprises[j];
        }
        surprise /= end - i;

        var salient = episodes.Any(e => e.Start.TotalSeconds < rowEnd && e.End.TotalSeconds >= rowStart);
        var marker = salient ? " <<<" : "";

        Console.WriteLine($"  {row * 0.5,5:0.0}s |{new string(line)}| s={surprise:0.000}{marker}");
        row++;
        if (row > 60)
        {
            Console.WriteLine("  ... (truncated)");
            break;
        }
    }
}

// The episodes themselves — start, end, how strong, how long. These are what become salient
// perceptions when the sense is graduated into the always-on service.
if (episodes.Count > 0)
{
    const int maxEpisodeLines = 30; // a full-length file has thousands; don't flood the console
    Console.WriteLine();
    Console.WriteLine($"salient episodes ({episodes.Count}):");
    for (var i = 0; i < episodes.Count; i++)
    {
        if (i >= maxEpisodeLines)
        {
            Console.WriteLine($"  ... +{episodes.Count - maxEpisodeLines} more");
            break;
        }
        var episode = episodes[i];
        Console.WriteLine(
            $"  [{episode.Start.TotalSeconds,6:0.0}s -> {episode.End.TotalSeconds,6:0.0}s] " +
            $"peak {episode.PeakSalience:0.000}  {episode.Duration.TotalSeconds:0.0}s " +
            $"({episode.Frames} frames) — {SoundDescriptor.Describe(episode.Character)}");
    }
}

// --- Sound-units: recognizing the same sound again. Cluster segments into recurring units with a
//     bounded, strict codebook, and (optionally) dump listenable exemplars. Two grains:
//       default  -> one segment per salient EPISODE (phrase-sized; tends to group by speaker/source)
//       --words  -> cut phrases at onset peaks into WORD/syllable-sized segments and cluster those
//                   (the road toward telling the actual words apart).
//     Ear the exemplars to judge whether a unit's members really are the same sound. ---
if (episodes.Count > 0)
{
    var vigilance = FlagOr("vigilance", 0.9);
    var unitCapacity = (int)FlagOr("units", 64);
    var exemplarsDir = Flag("exemplars"); // when set, dump listenable clips + an index.html
    var wordFlag = args.FirstOrDefault(a => a == "--words" || a.StartsWith("--words=", StringComparison.Ordinal));
    var wordMode = wordFlag is not null;
    var useOnset = wordFlag is not null && wordFlag.Contains("onset", StringComparison.Ordinal);

    int Frames(double milliseconds) => Math.Max(1, (int)Math.Round(milliseconds / 1000.0 / stream.SecondsPerFrame));

    // The segments to cluster, as (frame range, start time): whole salient episodes, or word-sized
    // pieces. Two ways to cut words: at pauses (default — the quiet gaps between spoken words, which
    // also leaves continuous music as one run-on rather than fake words), or at onset peaks
    // (--words=onset — every syllable onset, which also fires on beats and noise).
    var segments = new List<(int Start, int End, double Time)>();
    if (wordMode && useOnset)
    {
        var onsetFloor = FlagOr("wordFloor", 0.15);
        var onsets = new OnsetSegmenter(onsetFloor, Frames(FlagOr("wordMinGap", 120)), Frames(FlagOr("wordMaxLen", 400)));
        foreach (var segment in onsets.Segment(surprises))
        {
            segments.Add((segment.StartFrame, segment.EndFrame, segment.StartFrame * stream.SecondsPerFrame));
        }
    }
    else if (wordMode)
    {
        // Per-frame loudness (RMS over each hop) — the signal the pauses show up in.
        var hop = cochlea.HopSize;
        var window = cochlea.FftSize;
        var pcm = source.Samples;
        var energy = new double[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var from = i * hop;
            double sum = 0;
            var n = 0;
            for (var j = from; j < from + window && j < pcm.Length; j++)
            {
                sum += (double)pcm[j] * pcm[j];
                n++;
            }
            energy[i] = n > 0 ? Math.Sqrt(sum / n) : 0;
        }

        var voiceFloor = FlagOr("voiceFloor", 0.02);
        var pauses = new PauseSegmenter(
            voiceFloor, Frames(FlagOr("gap", 80)), Frames(FlagOr("minWord", 100)), Frames(FlagOr("maxWord", 800)));
        foreach (var segment in pauses.Segment(energy))
        {
            segments.Add((segment.StartFrame, segment.EndFrame, segment.StartFrame * stream.SecondsPerFrame));
        }
    }
    else
    {
        foreach (var episode in episodes)
        {
            var start = Math.Clamp((int)Math.Round(episode.Start.TotalSeconds / stream.SecondsPerFrame), 0, frames.Count - 1);
            var end = Math.Clamp((int)Math.Round(episode.End.TotalSeconds / stream.SecondsPerFrame), start, frames.Count - 1);
            segments.Add((start, end, episode.Start.TotalSeconds));
        }
    }

    // A segment's mel frames, and a short padded WAV around it (for the exemplar page).
    List<float[]> FramesFor((int Start, int End, double Time) segment)
    {
        var start = Math.Clamp(segment.Start, 0, frames.Count - 1);
        var end = Math.Clamp(segment.End, start + 1, frames.Count);
        return frames.GetRange(start, end - start);
    }

    float[] ClipFor((int Start, int End, double Time) segment)
    {
        const double pad = 0.15;
        const double minSeconds = 0.6; // word pieces are tiny; give each enough length to actually hear
        var all = source.Samples;
        var lo = (int)((segment.Time - pad) * rate);
        var hi = (int)((segment.End * stream.SecondsPerFrame + pad) * rate);

        var minSamples = (int)(minSeconds * rate);
        if (hi - lo < minSamples)
        {
            var grow = (minSamples - (hi - lo)) / 2;
            lo -= grow;
            hi += grow;
        }

        lo = Math.Clamp(lo, 0, all.Length);
        hi = Math.Clamp(hi, lo, all.Length);
        return all.Slice(lo, hi - lo).ToArray();
    }

    // mel-avg only ever groups by texture, so word mode uses the pitch-robust methods only. The
    // trajectory keeps N slices across a word (its sound *sequence*) — more slices = finer word
    // shape, which is what distinguishes "the" from "to" that averaging blurs away.
    var trajSegments = (int)FlagOr("trajSegments", 3);
    var fingerprints = wordMode
        ? new IFingerprint[]
        {
            new MfccFingerprint(cochlea.Bands, coefficients: 13),
            new MfccTrajectoryFingerprint(cochlea.Bands, coefficients: 13, segments: trajSegments),
        }
        : new IFingerprint[]
        {
            new MelAverageFingerprint(),
            new MfccFingerprint(cochlea.Bands, coefficients: 13),
            new MfccTrajectoryFingerprint(cochlea.Bands, coefficients: 13, segments: trajSegments),
        };

    var grain = wordMode ? (useOnset ? "word-onsets" : "word-pauses") : "salient-episodes";
    Console.WriteLine();
    Console.WriteLine(
        $"sound-units [{grain}]: vigilance {vigilance} (higher = stricter), capacity {unitCapacity}, " +
        $"{segments.Count} segments");

    var segmentFrames = segments.Select(FramesFor).ToList();

    var html = new System.Text.StringBuilder();
    if (exemplarsDir is not null)
    {
        html.Append("<!doctype html><meta charset=\"utf-8\"><title>sound-unit exemplars</title>");
        html.Append(
            "<style>body{font-family:sans-serif;margin:2rem;max-width:60rem}" +
            "h2{margin-top:2rem;border-top:1px solid #ccc;padding-top:1rem}h3{color:#333}" +
            ".m{margin:.25rem 0}.t{display:inline-block;width:5rem;color:#666}audio{vertical-align:middle}</style>");
        html.Append($"<h1>{Path.GetFileName(path)} &mdash; recurring sound-units ({grain})</h1>");
        html.Append("<p>Each unit groups segments the codebook judged &ldquo;the same sound.&rdquo; " +
                    "Listen down a unit &mdash; do they actually sound alike?</p>");
    }

    const int maxClipsPerUnit = 8;

    foreach (var fingerprint in fingerprints)
    {
        var codebook = new SoundUnitCodebook(vigilance, unitCapacity);
        var assignments = new int[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            assignments[i] = codebook.Assign(fingerprint.Compute(segmentFrames[i]));
        }

        var reused = codebook.Counts.Count(c => c > 1);
        var full = codebook.UnitCount >= unitCapacity
            ? "  <- FULL: codebook capped, distinct sounds force-merged; raise --units"
            : "";
        Console.WriteLine();
        Console.WriteLine(
            $"  [{fingerprint.Name}] {codebook.UnitCount} units from {segments.Count} segments, " +
            $"{reused} recurred (>1):{full}");

        if (exemplarsDir is not null)
        {
            html.Append($"<h2>{fingerprint.Name} &mdash; {codebook.UnitCount} units, {reused} recurred</h2>");
        }

        for (var unit = 0; unit < codebook.UnitCount; unit++)
        {
            if (codebook.Counts[unit] < 2)
            {
                continue;
            }

            var members = new List<int>();
            for (var i = 0; i < assignments.Length; i++)
            {
                if (assignments[i] == unit)
                {
                    members.Add(i);
                }
            }

            // Console: cap the printed timestamps so a busy word run stays readable.
            var shown = members.Take(12).Select(i => $"{segments[i].Time:0.0}s");
            var more = members.Count > 12 ? $", +{members.Count - 12}" : "";
            Console.WriteLine($"    unit #{unit} x{codebook.Counts[unit]}: {string.Join(", ", shown)}{more}");

            if (exemplarsDir is not null)
            {
                html.Append($"<h3>unit #{unit} &times;{codebook.Counts[unit]}</h3>");
                var written = 0;
                foreach (var i in members)
                {
                    if (written >= maxClipsPerUnit)
                    {
                        html.Append($"<div class=\"m\"><em>&hellip; {members.Count - maxClipsPerUnit} more</em></div>");
                        break;
                    }

                    var label = $"{segments[i].Time:0.0}s";
                    var file = Path.Combine(exemplarsDir, fingerprint.Name, $"unit-{unit}", $"{written:00}_{label}.wav");
                    WavWriter.WriteMono(file, ClipFor(segments[i]), rate);
                    var relative = Path.GetRelativePath(exemplarsDir, file).Replace('\\', '/');
                    html.Append(
                        $"<div class=\"m\"><span class=\"t\">{label}</span>" +
                        $"<audio controls preload=\"none\" src=\"{relative}\"></audio></div>");
                    written++;
                }
            }
        }
    }

    if (exemplarsDir is not null)
    {
        Directory.CreateDirectory(exemplarsDir);
        var indexPath = Path.Combine(exemplarsDir, "index.html");
        File.WriteAllText(indexPath, html.ToString());
        Console.WriteLine();
        Console.WriteLine($"exemplars written -> open {indexPath}");
    }
}

// --- The richer auditory bundle, per channel: verify each tracks something real (pitch moves with
//     the voice; harmonicity high in speech, low in hiss/silence; brightness up on sibilants). These
//     are the channels the place-baseline above now holds an expectation over. ---
Console.WriteLine();
Console.WriteLine(
    $"auditory channels: {auditoryFrames.Count:N0} frames, pitch range {ear.MinPitchHz:0}-{ear.MaxPitchHz:0} Hz");

if (auditoryFrames.Count > 0)
{
    var voicedTotal = auditoryFrames.Count(f => f.Voiced);
    Console.WriteLine($"  voiced: {voicedTotal:N0} frames ({100.0 * voicedTotal / auditoryFrames.Count:0}%)");
    Console.WriteLine("  per second:  loud | pitch  | harm | bright");

    var perRow = Math.Max(1, (int)Math.Round(1.0 / stream.SecondsPerFrame));
    var row = 0;
    for (var i = 0; i < auditoryFrames.Count; i += perRow)
    {
        var end = Math.Min(i + perRow, auditoryFrames.Count);
        double loud = 0, harm = 0, bright = 0, pitch = 0;
        var voiced = 0;
        for (var j = i; j < end; j++)
        {
            var f = auditoryFrames[j];
            loud += f.Loudness;
            harm += f.Harmonicity;
            bright += f.BrightnessHz;
            if (f.Voiced)
            {
                pitch += f.PitchHz;
                voiced++;
            }
        }

        var n = end - i;
        var pitchText = voiced > 0 ? $"{pitch / voiced,4:0}Hz" : "  -- ";
        Console.WriteLine($"  {row,5}s | {loud / n:0.000} | {pitchText} | {harm / n:0.00} | {bright / n,5:0}Hz");
        row++;
        if (row > 60)
        {
            Console.WriteLine("  ... (truncated)");
            break;
        }
    }
}

return 0;
