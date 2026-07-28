using Microsoft.Extensions.Options;
using Mind.Contracts;

namespace Mind.Perception;

/// <summary>
/// The Mind's heartbeat: an always-on loop that lives in time. It holds an idle
/// baseline for where it is, brackets a memory when salience departs from and
/// returns to that baseline, forms the finished memory, and hands it to the
/// Memory service to store.
///
/// Standing rule: nothing here may be allowed to silently die. Every tick and
/// every perception is guarded, and every exception — however small — is logged.
/// </summary>
/// <remarks>
/// The <c>Perception</c> contract type is fully qualified because this service's
/// namespace shares its leaf name; an unqualified name would bind to the
/// namespace, not the type.
/// </remarks>
public sealed class Heartbeat : BackgroundService
{
    private readonly PerceptionStream _stream;
    private readonly IMemorySink _memories;
    private readonly HeartbeatOptions _options;
    private readonly ILogger<Heartbeat> _logger;

    // The currently-forming memory, if any, and when it last saw activity.
    // Only ever touched from the single-threaded tick loop.
    private OpenMemory? _open;
    private DateTimeOffset _lastActivity;

    public Heartbeat(
        PerceptionStream stream,
        IMemorySink memories,
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
                    await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
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
                await CloseOpenMemoryAsync(_lastActivity, "shutdown", CancellationToken.None);
            }

            _logger.LogInformation("Heartbeat stopped.");
        }
    }

    private async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
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
            await CloseOpenMemoryAsync(_lastActivity, "returned to idle", cancellationToken);
        }
    }

    private void Perceive(Mind.Contracts.Perception perception, DateTimeOffset now)
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
            _open = new OpenMemory
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
    private static bool IsSalient(Mind.Contracts.Perception perception) => true;

    private async Task CloseOpenMemoryAsync(DateTimeOffset endedAt, string reason, CancellationToken cancellationToken)
    {
        var open = _open;
        if (open is null)
        {
            return;
        }

        // Clear the open slot first so a new memory can begin forming while this
        // finished one is being handed off.
        _open = null;

        var memory = open.ToMemory(endedAt);

        _logger.LogInformation(
            "Memory closed ({Reason}) at {Place}: {Count} perception(s) over {Duration}. ({MemoryId})",
            reason, memory.Place, memory.Perceptions.Count, memory.Duration, memory.Id);

        try
        {
            await _memories.StoreAsync(memory, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hand memory {MemoryId} to the Memory service.", memory.Id);
        }
    }

    /// <summary>A memory while it is still forming — mutable, private to the loop.</summary>
    private sealed class OpenMemory
    {
        public Guid Id { get; } = Guid.NewGuid();
        public required string Place { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public List<Mind.Contracts.Perception> Perceptions { get; } = new();

        public Mind.Contracts.Memory ToMemory(DateTimeOffset endedAt) =>
            new(Id, Place, StartedAt, endedAt, Perceptions.AsReadOnly());
    }
}
