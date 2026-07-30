using System.Globalization;
using Mind.Hearing;

// A tiny offline bench for the Mind's hearing. Point it at a media file (MP4 or WAV) and it
// shows what the Mind takes in — the ingestion, the cochlea's mel stream, and the salient
// episodes the place-baseline brackets — so the whole chain can be seen and tuned on real
// material before it is wired into the always-on service.
//
//   Mind.Hearing.Tuner <media-file> [seconds] [sampleRate]
//        [--leak=0.05] [--restingLeak=0.005] [--ratio=2.5] [--floor=0.05] [--hold=0.4] [--minEpisode=0.08]
//        [--vigilance=0.9] [--units=64] [--exemplars=<dir>]  (dir: write listenable clips + index.html)

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

Console.WriteLine($"Loading: {Path.GetFileName(path)}");
Console.WriteLine($"  target rate : {rate} Hz{(seconds is { } limit ? $"   (first {limit:0.#}s)" : "")}");

FileAudioSource source;
try
{
    source = FileAudioSource.Load(path, rate, seconds);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    return 1;
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
var stream = new HearingStream(source, cochlea);

var frames = new List<float[]>();
while (stream.Next() is { } mel)
{
    frames.Add(mel);
}

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
};

var detector = new PlaceBaseline(options, stream.SecondsPerFrame);
var episodes = new List<SalientEpisode>();
var surprises = new double[frames.Count];
for (var i = 0; i < frames.Count; i++)
{
    if (detector.Observe(frames[i]) is { } episode)
    {
        episodes.Add(episode);
    }
    surprises[i] = detector.LastSurprise;
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
    $"place-baseline: leak {options.ExpectationLeak}, restingLeak {options.RestingLeak}, " +
    $"spike x{options.SpikeRatio}, floor {options.Floor}, hold {options.HoldSeconds}s, " +
    $"minEpisode {options.MinEpisodeSeconds}s");
Console.WriteLine(
    $"  surprise: min {minSurprise:0.000}  mean {(surprises.Length > 0 ? sumSurprise / surprises.Length : 0):0.000}  " +
    $"max {maxSurprise:0.000}   -> {episodes.Count} salient episode(s)");

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
    Console.WriteLine();
    Console.WriteLine($"salient episodes ({episodes.Count}):");
    foreach (var episode in episodes)
    {
        Console.WriteLine(
            $"  [{episode.Start.TotalSeconds,6:0.0}s -> {episode.End.TotalSeconds,6:0.0}s] " +
            $"peak {episode.PeakSalience:0.000}  mean {episode.MeanSalience:0.000}  " +
            $"{episode.Duration.TotalSeconds:0.0}s ({episode.Frames} frames)");
    }
}

// --- Sound-units: recognizing the same sound again. Fingerprint each episode three ways and
//     cluster into recurring units with a bounded, strict codebook. Compare how each method groups
//     the *same* episodes: which recognizes recurrences (fewer units, more reuse) without smearing
//     distinct sounds together. Eyeball the timestamps against the video to judge coherence. ---
if (episodes.Count > 0)
{
    var vigilance = FlagOr("vigilance", 0.9);
    var unitCapacity = (int)FlagOr("units", 64);
    var exemplarsDir = Flag("exemplars"); // when set, dump listenable clips + an index.html

    // Each episode's mel frames, sliced from the full stream by its time span.
    List<float[]> FramesFor(SalientEpisode episode)
    {
        var start = (int)Math.Round(episode.Start.TotalSeconds / stream.SecondsPerFrame);
        var end = (int)Math.Round(episode.End.TotalSeconds / stream.SecondsPerFrame);
        start = Math.Clamp(start, 0, frames.Count - 1);
        end = Math.Clamp(end, start, frames.Count - 1);
        return frames.GetRange(start, end - start + 1);
    }

    // A short WAV around an episode (with a little padding), pulled from the raw samples, so a
    // unit's members can be auditioned back-to-back.
    float[] ClipFor(SalientEpisode episode)
    {
        const double pad = 0.15;
        var all = source.Samples;
        var lo = (int)((episode.Start.TotalSeconds - pad) * rate);
        var hi = (int)((episode.End.TotalSeconds + pad) * rate);
        lo = Math.Clamp(lo, 0, all.Length);
        hi = Math.Clamp(hi, lo, all.Length);
        return all.Slice(lo, hi - lo).ToArray();
    }

    var fingerprints = new IFingerprint[]
    {
        new MelAverageFingerprint(),
        new MfccFingerprint(cochlea.Bands, coefficients: 13),
        new MfccTrajectoryFingerprint(cochlea.Bands, coefficients: 13, segments: 3),
    };

    Console.WriteLine();
    Console.WriteLine(
        $"sound-units: vigilance {vigilance} (higher = stricter), capacity {unitCapacity}, " +
        $"{episodes.Count} episodes");

    var episodeFrames = episodes.Select(FramesFor).ToList();

    var html = new System.Text.StringBuilder();
    if (exemplarsDir is not null)
    {
        html.Append("<!doctype html><meta charset=\"utf-8\"><title>sound-unit exemplars</title>");
        html.Append(
            "<style>body{font-family:sans-serif;margin:2rem;max-width:60rem}" +
            "h2{margin-top:2rem;border-top:1px solid #ccc;padding-top:1rem}h3{color:#333}" +
            ".m{margin:.25rem 0}.t{display:inline-block;width:5rem;color:#666}audio{vertical-align:middle}</style>");
        html.Append($"<h1>{Path.GetFileName(path)} &mdash; recurring sound-units</h1>");
        html.Append("<p>Each unit groups episodes the codebook judged &ldquo;the same sound.&rdquo; " +
                    "Listen down a unit &mdash; do they actually sound alike?</p>");
    }

    const int maxClipsPerUnit = 8;

    foreach (var fingerprint in fingerprints)
    {
        var codebook = new SoundUnitCodebook(vigilance, unitCapacity);
        var assignments = new int[episodes.Count];
        for (var i = 0; i < episodes.Count; i++)
        {
            assignments[i] = codebook.Assign(fingerprint.Compute(episodeFrames[i]));
        }

        var reused = codebook.Counts.Count(c => c > 1);
        Console.WriteLine();
        Console.WriteLine(
            $"  [{fingerprint.Name}] {codebook.UnitCount} units from {episodes.Count} episodes, " +
            $"{reused} recurred (>1):");

        if (exemplarsDir is not null)
        {
            html.Append($"<h2>{fingerprint.Name} &mdash; {codebook.UnitCount} units, {reused} recurred</h2>");
        }

        // Show the recurring units and the timestamps that landed on them — the interesting ones.
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

            Console.WriteLine(
                $"    unit #{unit} x{codebook.Counts[unit]}: " +
                string.Join(", ", members.Select(i => $"{episodes[i].Start.TotalSeconds:0.0}s")));

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

                    var label = $"{episodes[i].Start.TotalSeconds:0.0}s";
                    var file = Path.Combine(exemplarsDir, fingerprint.Name, $"unit-{unit}", $"{label}.wav");
                    WavWriter.WriteMono(file, ClipFor(episodes[i]), rate);
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

return 0;
