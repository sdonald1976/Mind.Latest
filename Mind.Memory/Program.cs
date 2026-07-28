using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Mind.Memory;

var builder = WebApplication.CreateBuilder(args);

// Postgres-backed persistence. The connection is supplied by the AppHost via
// the "mind-memory-db" reference; this Aspire integration also adds retries,
// health checks, and telemetry for the database.
builder.AddNpgsqlDbContext<MemoryDbContext>("mind-memory-db");

builder.Services.AddScoped<IMemoryStore, EfMemoryStore>();

// Messaging: consume formed-memory messages off the bus. On a transient failure
// each message is retried; if it keeps failing the broker dead-letters it, so a
// memory is never silently dropped. Storing is idempotent, so redelivery is safe.
var rabbitConnectionString =
    builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException(
        "RabbitMQ connection string not configured. Expected the AppHost to inject 'ConnectionStrings:rabbitmq'.");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MemoryFormedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(rabbitConnectionString));
        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(context);
    });
});

// A clickable API page (Swagger UI) so the service can be driven from a browser.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- Catch everything, log everything. Standing rule: no exception, however
//     small, goes unlogged. ---
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mind.Memory");

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

// Make sure the schema exists before we serve requests. EnsureCreated is fine
// while there is a single table; we move to EF migrations once the schema
// starts to evolve (see DESIGN.md).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
    try
    {
        await db.Database.EnsureCreatedAsync();
        log.LogInformation("Memory database ready.");
    }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Failed to ensure the memory database exists.");
        throw;
    }
}

// Swagger UI at /swagger while developing.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints. ---

// How many memories are held right now?
app.MapGet("/", async (IMemoryStore store) =>
    Results.Ok(new { memoriesFormed = await store.CountAsync() }));

// Receive a finished memory from the Perception service.
app.MapPost("/memories", async (Mind.Contracts.Memory memory, IMemoryStore store, ILogger<Program> logger) =>
{
    await store.AddAsync(memory);
    logger.LogInformation(
        "Stored memory {MemoryId} from {Place}: {Count} perception(s) over {Duration}.",
        memory.Id, memory.Place, memory.Perceptions.Count, memory.Duration);
    return Results.Accepted();
});

// Recall the most recent memories.
app.MapGet("/memories", async (IMemoryStore store) =>
    Results.Ok(await store.RecentAsync(100)));

app.Run();
