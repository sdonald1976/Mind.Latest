using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Mind.Perception;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration: everything tunable, nothing magic. Validated on start so a
//     bad value fails loudly and immediately rather than misbehaving quietly. ---
builder.Services
    .AddOptions<HeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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
