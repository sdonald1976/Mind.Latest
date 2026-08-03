using System.Diagnostics;
using Microsoft.Extensions.Options;
using Mind.Hearing;

namespace Mind.Perception;

/// <summary>
/// The Mind's first real sense. It reads an audio source (a file today, a live microphone later —
/// the same seam), reduces it through a fixed cochlea to a mel stream, holds a place-baseline over
/// it, and drops each salient episode into the <see cref="PerceptionStream"/> as a perception. The
/// heartbeat then brackets clusters of these into memories, exactly as it does an HTTP poke.
///
/// It runs on its own fast loop, paced to real time so the Mind hears the source as it plays —
/// living in time, not gulping a file. Standing rule: guarded end to end, never allowed to take the
/// service down; if the source fails, hearing stops and says so, and the Mind lives on without it.
/// </summary>
public sealed class AudioSense : BackgroundService
{
    private readonly PerceptionStream _stream;
    private readonly HearingOptions _hearing;
    private readonly CochleaOptions _cochlea;
    private readonly PlaceBaselineOptions _baseline;
    private readonly ILogger<AudioSense> _logger;

    public AudioSense(
        PerceptionStream stream,
        IOptions<HearingOptions> hearing,
        IOptions<CochleaOptions> cochlea,
        IOptions<PlaceBaselineOptions> baseline,
        ILogger<AudioSense> logger)
    {
        _stream = stream;
        _hearing = hearing.Value;
        _cochlea = cochlea.Value;
        _baseline = baseline.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hearing.Enabled)
        {
            _logger.LogInformation("Hearing is off. The Mind runs without it.");
            return;
        }

        var useMic = string.Equals(_hearing.Source, "mic", StringComparison.OrdinalIgnoreCase);
        if (!useMic && string.IsNullOrWhiteSpace(_hearing.SourcePath))
        {
            _logger.LogInformation("Hearing is on but no file is configured and Source is not 'mic'. Nothing to hear.");
            return;
        }

        IAudioSource source;
        try
        {
            source = useMic
                ? new MicAudioSource(_hearing.SampleRate, _hearing.MicDevice)
                : FileAudioSource.Load(_hearing.SourcePath!, _hearing.SampleRate, _hearing.Seconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the audio source. Hearing will not start.");
            return;
        }

        // The cochlea's rate must match the source we actually loaded; everything else is config.
        var cochlea = new Cochlea(new CochleaOptions
        {
            SampleRate = source.SampleRate,
            FftSize = _cochlea.FftSize,
            HopSize = _cochlea.HopSize,
            MelBands = _cochlea.MelBands,
            MinHz = _cochlea.MinHz,
            MaxHz = _cochlea.MaxHz,
        });
        var ear = new Ear(cochlea);
        var hearing = new AuditoryStream(source, ear);
        var detector = new PlaceBaseline(
            _baseline, hearing.SecondsPerFrame, ear.MinPitchHz, ear.MaxPitchHz, ear.NyquistHz);

        // Identity: fingerprint each episode (pitch-robust MFCC of its mean spectrum) and cluster
        // into recurring sound-units, so a perception can carry "the same sound again." Coarse
        // (voice/source grain, not words) and in-memory for now — units reset when the Mind restarts.
        var fingerprint = new MfccFingerprint(cochlea.Bands, coefficients: 13);
        var units = new SoundUnitCodebook(vigilance: 0.9, capacity: 128);

        var sourceLabel = useMic ? "audio:mic" : $"audio:{Path.GetFileNameWithoutExtension(_hearing.SourcePath)}";

        _logger.LogInformation(
            "Hearing started. Source={Source} Rate={Rate}Hz Bands={Bands}",
            useMic ? "microphone" : _hearing.SourcePath, source.SampleRate, cochlea.Bands);

        var startedAt = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        long frameIndex = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                AuditoryFrame? frame;
                try
                {
                    frame = hearing.Next();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read the next audio frame. Hearing stops.");
                    break;
                }

                if (frame is null)
                {
                    break; // source exhausted
                }

                frameIndex++;

                try
                {
                    if (detector.Observe(frame) is { } episode)
                    {
                        Emit(episode, startedAt, sourceLabel, fingerprint, units);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to take in an audio frame.");
                }

                // Pace to real time: don't run ahead of the sound. A file read flat-out would
                // otherwise blast through in a burst and collapse every episode into one wall-clock
                // instant, and the Mind is supposed to live in time.
                var leadMs = frameIndex * hearing.SecondsPerFrame * 1000.0 - clock.Elapsed.TotalMilliseconds;
                if (leadMs > 20)
                {
                    try
                    {
                        await Task.Delay((int)leadMs, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            try
            {
                if (detector.Flush() is { } tail)
                {
                    Emit(tail, startedAt, sourceLabel, fingerprint, units);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush the final audio episode.");
            }
        }
        finally
        {
            (source as IDisposable)?.Dispose(); // release the microphone, if that's what we opened
            _logger.LogInformation(
                "Hearing finished. Heard {Frames} frames over {Elapsed}.", frameIndex, clock.Elapsed);
        }
    }

    private void Emit(
        SalientEpisode episode,
        DateTimeOffset startedAt,
        string sourceLabel,
        MfccFingerprint fingerprint,
        SoundUnitCodebook units)
    {
        // Identity: which recurring sound-unit is this? Same id again = "the same sound."
        var unit = units.Assign(fingerprint.Compute([episode.MeanMel]));
        var timesHeard = units.Counts[unit];

        // `What` describes what the sound was *like* (loud/tonal/bright...), not what it *was*.
        // Unit carries coarse identity; Intensity the salience; Source the sense.
        var what = SoundDescriptor.Describe(episode.Character);
        var perception = new Mind.Contracts.Perception(
            What: what,
            At: startedAt + episode.Start,
            Intensity: episode.PeakSalience,
            Source: sourceLabel,
            Unit: unit);

        if (_stream.Submit(perception))
        {
            _logger.LogInformation(
                "Heard {What} at {At:mm\\:ss\\.f} (peak {Intensity:0.00}, {Duration:0.0}s) — unit #{Unit} ({Recur}).",
                what, episode.Start, episode.PeakSalience, episode.Duration.TotalSeconds, unit,
                timesHeard > 1 ? $"heard {timesHeard}×" : "new");
        }
        else
        {
            _logger.LogWarning("Perception stream refused a heard sound (shutting down?).");
        }
    }
}
