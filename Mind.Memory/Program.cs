using Microsoft.AspNetCore.Diagnostics;
using Mind.Contracts;
using Mind.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MemoryStore>();

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

// --- Endpoints. ---

// How many memories are held right now?
app.MapGet("/", (MemoryStore memories) => Results.Ok(new { memoriesFormed = memories.Count }));

// Receive a finished memory from the Perception service.
app.MapPost("/memories", (Memory memory, MemoryStore store, ILogger<Program> logger) =>
{
    store.Add(memory);
    logger.LogInformation(
        "Stored memory {MemoryId} from {Place}: {Count} perception(s) over {Duration}.",
        memory.Id, memory.Place, memory.Perceptions.Count, memory.Duration);
    return Results.Accepted();
});

// Recall the most recent memories.
app.MapGet("/memories", (MemoryStore store) => Results.Ok(store.Recent));

app.Run();
