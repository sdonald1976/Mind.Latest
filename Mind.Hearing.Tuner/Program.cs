using System.Globalization;
using Mind.Hearing;

// A tiny offline bench for the Mind's hearing. Point it at a media file (MP4 or WAV)
// and it reports what the Mind would take in — proving the ingestion path
// (ffmpeg -> WAV -> mono samples) on real material before any of it is wired into
// the always-on service. Increment 1: just get sound in and look at it.
//
//   Mind.Hearing.Tuner <media-file> [seconds] [sampleRate]

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Mind.Hearing.Tuner <media-file> [seconds] [sampleRate]");
    return 1;
}

var path = args[0];
double? seconds = args.Length > 1 && double.TryParse(args[1], CultureInfo.InvariantCulture, out var s) ? s : null;
var rate = args.Length > 2 && int.TryParse(args[2], out var r) ? r : 16_000;

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

// A coarse one-row-per-second loudness envelope, so we can *see* the shape of the
// sound over time (speech vs. song vs. quiet). This is not salience yet — salience is
// change from a baseline, which comes with the cochlea and the place-baseline — but it
// is the first look at where salience will live.
Console.WriteLine();
Console.WriteLine("per-second loudness (rms):");

var second = 0;
for (var i = 0; i < count; i += rate)
{
    var end = Math.Min(i + rate, count);
    var windowSquares = 0.0;
    for (var j = i; j < end; j++)
    {
        windowSquares += (double)samples[j] * samples[j];
    }

    var windowRms = Math.Sqrt(windowSquares / (end - i));
    var bar = new string('#', (int)Math.Clamp(windowRms * 100, 0, 60));
    Console.WriteLine($"  {second,4}s | {windowRms:0.000} {bar}");

    second++;
    if (second > 120)
    {
        Console.WriteLine("  ... (truncated at 120s)");
        break;
    }
}

// --- The cochlea: samples -> mel-vector stream, the small signal the place-baseline
//     will sit against. Loudness (above) can't tell a loud-but-expected song from a
//     novel sound; the mel bands are what will. ---
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

if (frames.Count > 0)
{
    // Aggregate to ~0.5s rows and shade each band by its share of the run's peak, so the
    // structure is visible: low bands light up for voice and song, high bands for
    // consonants and effects, and quiet stretches go dark.
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

        Console.WriteLine($"  {row * 0.5,5:0.0}s |{new string(line)}|");
        row++;
        if (row > 60)
        {
            Console.WriteLine("  ... (truncated)");
            break;
        }
    }
}

return 0;
