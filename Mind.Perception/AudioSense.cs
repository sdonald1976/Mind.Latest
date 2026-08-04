using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
    // The sound-unit codebook is 13-coefficient MFCC (see below); a stored codebook built at a
    // different width can't be compared and is discarded on load rather than silently mismatched.
    private const int FingerprintCoefficients = 13;

    private readonly PerceptionStream _stream;
    private readonly HearingOptions _hearing;
    private readonly CochleaOptions _cochlea;
    private readonly PlaceBaselineOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AudioSense> _logger;

    public AudioSense(
        PerceptionStream stream,
        IOptions<HearingOptions> hearing,
        IOptions<CochleaOptions> cochlea,
        IOptions<PlaceBaselineOptions> baseline,
        IServiceScopeFactory scopeFactory,
        ILogger<AudioSense> logger)
    {
        _stream = stream;
        _hearing = hearing.Value;
        _cochlea = cochlea.Value;
        _baseline = baseline.Value;
        _scopeFactory = scopeFactory;
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
            if (useMic)
            {
                var mic = OpenMicrophone();
                if (mic is null)
                {
                    return; // no usable input device; reason already logged
                }
                source = mic;
            }
            else
            {
                source = FileAudioSource.Load(_hearing.SourcePath!, _hearing.SampleRate, _hearing.Seconds);
            }
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
        // (voice/source grain, not words). The codebook is restored from disk so a unit id means the
        // same sound as last run — that's what keeps the facts built on those ids meaningful.
        var fingerprint = new MfccFingerprint(cochlea.Bands, coefficients: FingerprintCoefficients);
        var restored = await LoadCodebookAsync(stoppingToken);
        var units = new SoundUnitCodebook(vigilance: 0.9, capacity: 128, restore: restored);
        if (units.UnitCount > 0)
        {
            _logger.LogInformation("Recalled {Units} known sound-unit(s) from before.", units.UnitCount);
        }

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
                        // The codebook just changed (a unit matched or was minted). Persist it so the
                        // repertoire — and the ids the facts are built on — survives a restart.
                        await SaveCodebookAsync(units, stoppingToken);
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
                // Persist the final state on the way out. CancellationToken.None so a graceful
                // shutdown still commits the last of what was learned rather than aborting the write.
                await SaveCodebookAsync(units, CancellationToken.None);
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

    /// <summary>
    /// Open the configured microphone. Logs every input device the system reports (so you can see what
    /// to name), resolves the choice by name then index, and falls back to the first input with a
    /// warning if nothing matched. Returns null only when there is no input device at all.
    /// </summary>
    private MicAudioSource? OpenMicrophone()
    {
        var devices = MicAudioSource.ListDevices();
        if (devices.Count == 0)
        {
            _logger.LogError("Hearing is set to 'mic' but the system reports no input devices. Hearing will not start.");
            return null;
        }

        _logger.LogInformation("Input devices: {Devices}.",
            string.Join("; ", devices.Select(d => $"[{d.Index}] {d.Name}")));

        var chosen = MicAudioSource.Resolve(devices, _hearing.MicName, _hearing.MicDevice);
        if (chosen is null)
        {
            var asked = string.IsNullOrWhiteSpace(_hearing.MicName)
                ? $"device index {_hearing.MicDevice}"
                : $"name matching '{_hearing.MicName}'";
            var fallback = devices[0];
            _logger.LogWarning("No input matched {Asked}; falling back to [{Index}] {Name}.",
                asked, fallback.Index, fallback.Name);
            chosen = fallback;
        }

        _logger.LogInformation("Listening on microphone [{Index}] {Name}.", chosen.Value.Index, chosen.Value.Name);
        return new MicAudioSource(_hearing.SampleRate, chosen.Value.Index);
    }

    /// <summary>
    /// Load the stored codebook, or return null to start fresh. A stored codebook whose fingerprint
    /// width no longer matches (the cochlea or fingerprint was reconfigured) is discarded rather than
    /// compared apples-to-oranges — every failure here is caught so hearing always starts.
    /// </summary>
    private async Task<CodebookSnapshot?> LoadCodebookAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ICodebookStore>();
            var snapshot = await store.LoadAsync(stoppingToken);

            if (snapshot is null || snapshot.Prototypes.Length == 0)
            {
                return null; // first run, or nothing learned yet
            }

            if (!snapshot.Prototypes.All(p => p.Length == FingerprintCoefficients))
            {
                _logger.LogWarning(
                    "Stored codebook was built at a different fingerprint width; starting fresh so unit ids aren't mismatched.");
                return null;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the stored codebook. Starting with an empty repertoire.");
            return null;
        }
    }

    /// <summary>Persist the codebook. Guarded: a storage hiccup must never stop the Mind hearing.</summary>
    private async Task SaveCodebookAsync(SoundUnitCodebook units, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ICodebookStore>();
            await store.SaveAsync(units.Snapshot(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-save — the next run's load simply sees the previous good state.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist the sound-unit codebook. Learning continues in memory.");
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
