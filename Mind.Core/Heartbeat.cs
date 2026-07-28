using Microsoft.Extensions.Options;

namespace Mind.Core;

/// <summary>
/// The Mind's heartbeat: an always-on loop that lives in time. It holds an idle
/// baseline for where it is, brackets a memory when salience departs from and
/// returns to that baseline, and emits the memory when it closes.
///
/// Standing rule: nothing here may be allowed to silently die. Every tick and
/// every perception is guarded, and every exception — however small — is logged.
/// </summary>
public sealed class Heartbeat : BackgroundService
{
    private readonly PerceptionStream _stream;
    private readonly MemoryStore _memories;
    private readonly HeartbeatOptions _options;
    private readonly ILogger<Heartbeat> _logger;

    // The currently-forming memory, if any, and when it last saw activity.
    // Only ever touched from the single-threaded tick loop.
    private Memory? _open;
    private DateTimeOffset _lastActivity;

    public Heartbeat(
        PerceptionStream stream,
        MemoryStore memories,
        IOptions<HeartbeatOptions> options,
        ILogger<Heartbeat> logger)
    {
        _stream = stream;
        _memories = memories;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Heartbeat started. Place={Place} Tick={TickMs}ms Idle={IdleMs}ms",
            _options.Place, _options.TickIntervalMs, _options.IdleTimeoutMs);

        using var timer = new PeriodicTimer(_options.TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // A tick is one "present moment." Nothing that happens inside a
                // single tick may take the loop down, so the body is guarded.
                try
                {
                    Tick(DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error during heartbeat tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            // If we stop mid-memory, close it so nothing is lost.
            if (_open is not null)
            {
                CloseOpenMemory(_lastActivity, "shutdown");
            }

            _logger.LogInformation("Heartbeat stopped.");
        }
    }

    private void Tick(DateTimeOffset now)
    {
        // 1) Take in whatever has been perceived since the last tick.
        while (_stream.Reader.TryRead(out var perception))
        {
            try
            {
                Perceive(perception, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to take in a perception: {What}", perception.What);
            }
        }

        // 2) If a memory is open and things have gone quiet long enough, close it.
        if (_open is not null && now - _lastActivity >= _options.IdleTimeout)
        {
            CloseOpenMemory(_lastActivity, "returned to idle");
        }
    }

    private void Perceive(Perception perception, DateTimeOffset now)
    {
        // Baseline/salience seam. Today, any perception counts as a departure
        // from a silent baseline. When the Mind learns to hold a real baseline
        // for its place, the "is this salient?" decision lives right here.
        if (!IsSalient(perception))
        {
            return;
        }

        if (_open is null)
        {
            _open = new Memory
            {
                Place = _options.Place,
                StartedAt = perception.At,
            };

            _logger.LogInformation("Memory opened at {Place}. ({MemoryId})", _open.Place, _open.Id);
        }

        _open.Perceptions.Add(perception);
        _lastActivity = now;

        _logger.LogDebug(
            "Perceived: {What} (intensity {Intensity:0.00}). Open memory now holds {Count} perception(s).",
            perception.What, perception.Intensity, _open.Perceptions.Count);
    }

    /// <summary>
    /// Whether a perception is a departure from the current idle baseline.
    /// Intentionally simple for now (see DESIGN.md — baseline-relative change
    /// detection is a refinement we make inside this piece before moving on).
    /// </summary>
    private static bool IsSalient(Perception perception) => true;

    private void CloseOpenMemory(DateTimeOffset endedAt, string reason)
    {
        var memory = _open;
        if (memory is null)
        {
            return;
        }

        memory.EndedAt = endedAt;
        _memories.Add(memory);
        _open = null;

        _logger.LogInformation(
            "Memory closed ({Reason}) at {Place}: {Count} perception(s) over {Duration}. ({MemoryId})",
            reason, memory.Place, memory.Perceptions.Count, memory.Duration, memory.Id);
    }
}
