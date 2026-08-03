using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Mind.Facts;

var builder = WebApplication.CreateBuilder(args);

// Postgres-backed persistence for the distilled facts. The connection is supplied by the AppHost via
// the "mind-facts-db" reference; this Aspire integration also adds retries, health checks, and
// telemetry. Facts live in their own database — knowledge is this service's to keep, separate from
// Memory's log.
builder.AddNpgsqlDbContext<FactDbContext>("mind-facts-db");
builder.Services.AddScoped<IFactStore, EfFactStore>();

// Messaging: subscribe to the memory stream. As a separate consumer from Memory, MassTransit gives
// this service its own queue on the same exchange — so every formed memory fans out to both. This is
// the "future service subscribes to the memory stream" the bus was built for (see DESIGN.md).
var rabbitConnectionString =
    builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException(
        "RabbitMQ connection string not configured. Expected the AppHost to inject 'ConnectionStrings:rabbitmq'.");

// The distiller holds the Mind's distilled knowledge (the known sounds), folded from the memory
// stream. Singleton, so it accumulates across every memory the consumer hands it.
builder.Services.AddSingleton<Distiller>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MemoryHeardConsumer>();

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

// --- Catch everything, log everything. Standing rule: no exception, however small, goes unlogged. ---
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mind.Facts");

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

// Make sure the schema exists, then restore what the Mind already knew. Seeding the distiller from
// disk is what makes learning survive a restart: it resumes from its stored confidence rather than
// starting blank. EnsureCreated is fine while there is a single table (see DESIGN.md).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FactDbContext>();
    var distiller = scope.ServiceProvider.GetRequiredService<Distiller>();
    try
    {
        await db.Database.EnsureCreatedAsync();

        var store = scope.ServiceProvider.GetRequiredService<IFactStore>();
        var known = await store.AllAsync();
        distiller.Seed(known);

        log.LogInformation("Facts database ready; restored {Count} known sound(s).", known.Count);
    }
    catch (Exception ex)
    {
        log.LogCritical(ex, "Failed to ready the facts database.");
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

// How many standing facts does the Mind hold right now? Read from disk — the durable picture.
app.MapGet("/", async (IFactStore store) =>
    Results.Ok(new { facts = await store.CountAsync() }));

// What does the Mind know? The distilled facts on disk, strongest-held first.
app.MapGet("/facts", async (IFactStore store) =>
    Results.Ok(await store.AllAsync()));

app.Run();
