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

    // A touch of lead-in on a saved clip, so its attack isn't cut off when you play it back.
    private const double ClipPreRollSeconds = 0.1;

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

        // Keep a rolling buffer of recent raw audio, so each salient episode can be sliced back out and
        // saved as a listenable clip. Sized to the longest clip we'd keep plus the detector's hold, so
        // an episode's audio is still retained at the moment it closes. Absent when not saving clips.
        RecordingTap? tap = null;
        if (_hearing.SaveClips)
        {
            var capacitySamples = (int)Math.Ceiling(
                (_hearing.MaxClipSeconds + _baseline.HoldSeconds + 1.0) * source.SampleRate);
            tap = new RecordingTap(source, capacitySamples);
            source = tap;
            _logger.LogInformation("Saving episode clips to {ClipDir}.", Path.GetFullPath(_hearing.ClipPath));
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
                        await EmitAsync(episode, startedAt, sourceLabel, fingerprint, units,
                            tap, source.SampleRate, ear.FftSize, stoppingToken);
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
                    await EmitAsync(tail, startedAt, sourceLabel, fingerprint, units,
                        tap, source.SampleRate, ear.FftSize, CancellationToken.None);
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

    private async Task EmitAsync(
        SalientEpisode episode,
        DateTimeOffset startedAt,
        string sourceLabel,
        MfccFingerprint fingerprint,
        SoundUnitCodebook units,
        RecordingTap? tap,
        int sampleRate,
        int fftSize,
        CancellationToken cancellationToken)
    {
        // Identity: which recurring sound-unit is this? Same id again = "the same sound."
        var unit = units.Assign(fingerprint.Compute([episode.MeanMel]));
        var timesHeard = units.Counts[unit];
        var at = startedAt + episode.Start;

        // Keep a listenable clip of this moment (if we're saving them) and link the perception to it,
        // so it can be replayed and labelled later — the raw material for teaching.
        var clipId = tap is null
            ? null
            : await SaveClipAsync(episode, unit, at, tap, sampleRate, fftSize, cancellationToken);

        // `What` describes what the sound was *like* (loud/tonal/bright...), not what it *was*.
        // Unit carries coarse identity; Intensity the salience; Source the sense; ClipId the recording.
        var what = SoundDescriptor.Describe(episode.Character);
        var perception = new Mind.Contracts.Perception(
            What: what,
            At: at,
            Intensity: episode.PeakSalience,
            Source: sourceLabel,
            Unit: unit,
            ClipId: clipId);

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

    /// <summary>
    /// Slice this episode's audio out of the recording tap, write it as a WAV, and catalogue the row —
    /// so the moment can be replayed and labelled later. Returns the clip id, or null if nothing was
    /// saved. Guarded end to end: a clip that can't be written must never stop the Mind hearing.
    /// </summary>
    private async Task<Guid?> SaveClipAsync(
        SalientEpisode episode,
        int unit,
        DateTimeOffset at,
        RecordingTap tap,
        int sampleRate,
        int fftSize,
        CancellationToken cancellationToken)
    {
        try
        {
            // Map episode time to raw sample indices. A little pre-roll keeps the attack; one analysis
            // window of tail includes the last salient frame in full. Cap the span so a rare long
            // episode can't write a huge file.
            var from = (long)((episode.Start.TotalSeconds - ClipPreRollSeconds) * sampleRate);
            var to = (long)(episode.End.TotalSeconds * sampleRate) + fftSize;
            var maxSamples = (long)(_hearing.MaxClipSeconds * sampleRate);
            if (to - from > maxSamples)
            {
                to = from + maxSamples;
            }

            var samples = tap.Slice(from, to);
            if (samples.Length == 0)
            {
                return null; // nothing retained to save — skip quietly
            }

            var id = Guid.NewGuid();
            var fileName = $"{at:yyyy-MM-dd'T'HH-mm-ss-fff}_unit{unit}_{id:N}.wav";
            var path = Path.GetFullPath(Path.Combine(_hearing.ClipPath, fileName));
            WavWriter.WriteMono(path, samples, sampleRate);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IClipStore>();
            await store.AddAsync(new StoredClip
            {
                Id = id,
                Unit = unit,
                CapturedAt = at,
                Seconds = samples.Length / (double)sampleRate,
                SampleRate = sampleRate,
                Path = path,
            }, cancellationToken);

            return id;
        }
        catch (OperationCanceledException)
        {
            return null; // shutting down mid-save — the perception just goes out without a clip link
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save a clip of the heard sound. The perception is kept without one.");
            return null;
        }
    }
}
