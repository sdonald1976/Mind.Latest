using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mind.Hearing;
using Mind.Perception;

var builder = WebApplication.CreateBuilder(args);

// Perception's one piece of durable state: the sound-unit codebook. Persisting it (in its own
// "mind-perception-db") is what keeps unit ids stable across restarts — so the facts built on those
// ids stay meaningful session to session. The Aspire integration adds retries, health checks, telemetry.
builder.AddNpgsqlDbContext<PerceptionDbContext>("mind-perception-db");
builder.Services.AddScoped<ICodebookStore, EfCodebookStore>();

// The saved-clip catalogue lives in its own database, so its schema is created whole (and can grow
// and be pruned) independently of the codebook.
builder.AddNpgsqlDbContext<ClipDbContext>("mind-clips-db");
builder.Services.AddScoped<IClipStore, EfClipStore>();

// --- Configuration: everything tunable, nothing magic. Validated on start so a
//     bad value fails loudly and immediately rather than misbehaving quietly. ---
builder.Services
    .AddOptions<HeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Hearing: the source (this project) plus the cochlea and place-baseline (the Mind.Hearing
// library). Each bound from its own section so every knob is tunable without a recompile.
builder.Services
    .AddOptions<HearingOptions>()
    .Bind(builder.Configuration.GetSection(HearingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<CochleaOptions>()
    .Bind(builder.Configuration.GetSection(CochleaOptions.SectionName));
builder.Services
    .AddOptions<PlaceBaselineOptions>()
    .Bind(builder.Configuration.GetSection(PlaceBaselineOptions.SectionName));

// Messaging: publish formed memories onto the bus. The broker holds and
// redelivers each one until Memory stores and acknowledges it. Registered
// before the heartbeat so the bus is up before the first memory is published.
var rabbitConnectionString =
    builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException(
        "RabbitMQ connection string not configured. Expected the AppHost to inject 'ConnectionStrings:rabbitmq'.");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        cfg.Host(new Uri(rabbitConnectionString));
    });
});

builder.Services.AddSingleton<IMemoryPublisher, BusMemoryPublisher>();

builder.Services.AddSingleton<PerceptionStream>();
builder.Services.AddHostedService<Heartbeat>();

// The Mind's first real sense. It feeds salient episodes into the same PerceptionStream the
// heartbeat drains, so hearing and the manual /perceive poke arrive the same way.
builder.Services.AddHostedService<AudioSense>();

// A clickable API page (Swagger UI) so the service can be driven from a browser.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- Catch everything, log everything. Standing rule: no exception, however
//     small, goes unlogged. ---
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mind.Perception");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception escaped the app domain.");

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    log.LogError(e.Exception, "Unobserved task exception.");
    e.SetObserved();
};

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    log.LogError(error, "Unhandled exception while handling {Method} {Path}.",
        context.Request.Method, context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "internal error" });
}));

// Make sure the codebook schema exists before the sense starts and tries to recall it. EnsureCreated
// is fine while there is a single table; we move to EF migrations once the schema evolves (see DESIGN.md).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PerceptionDbContext>();
    var clipsDb = scope.ServiceProvider.GetRequiredService<ClipDbContext>();
    try
    {
        await db.Database.EnsureCreatedAsync();
        await clipsDb.Database.EnsureCreatedAsync();
        log.LogInformation("Perception databases ready.");
    }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Failed to ensure the perception databases exist.");
        throw;
    }
}

// Swagger UI at /swagger while developing.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints: the crudest possible "world" that can poke the Mind. This is a
//     stand-in for real senses, not throwaway — it is how the world feeds it. ---

// Where am I, and how am I tuned right now?
app.MapGet("/", (IOptions<HeartbeatOptions> options) => Results.Ok(new
{
    place = options.Value.Place,
    tickMs = options.Value.TickIntervalMs,
    idleMs = options.Value.IdleTimeoutMs,
}));

// The saved sensory clips, most recent first — the catalogue to label for teaching. The WAVs
// themselves are the files at each row's Path; this lists the index.
app.MapGet("/clips", async (IClipStore clips) => Results.Ok(await clips.RecentAsync(200)));

// Something happened to the Mind.
app.MapPost("/perceive", (PerceiveRequest request, PerceptionStream stream, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.What))
    {
        return Results.BadRequest(new { error = "'what' is required." });
    }

    var perception = new Mind.Contracts.Perception(
        What: request.What,
        At: DateTimeOffset.UtcNow,
        Intensity: request.Intensity ?? 1.0,
        Source: request.Source);

    if (!stream.Submit(perception))
    {
        logger.LogWarning("Perception dropped (stream refused write): {What}", request.What);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Accepted();
});

app.Run();

/// <summary>Body of a poke to <c>POST /perceive</c>.</summary>
internal sealed record PerceiveRequest(string What, double? Intensity, string? Source);
