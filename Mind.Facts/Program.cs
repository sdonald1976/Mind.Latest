using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Mind.Facts;

var builder = WebApplication.CreateBuilder(args);

// Messaging: subscribe to the memory stream. As a separate consumer from Memory, MassTransit gives
// this service its own queue on the same exchange — so every formed memory fans out to both. This is
// the "future service subscribes to the memory stream" the bus was built for (see DESIGN.md).
var rabbitConnectionString =
    builder.Configuration.GetConnectionString("rabbitmq")
    ?? throw new InvalidOperationException(
        "RabbitMQ connection string not configured. Expected the AppHost to inject 'ConnectionStrings:rabbitmq'.");

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

// Swagger UI at /swagger while developing.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints. Nothing distilled yet — increment 1 only listens; GET / is a placeholder for the
//     coming fact API. ---
app.MapGet("/", () => Results.Ok(new
{
    facts = 0,
    note = "listening to the memory stream; the distiller is the next step.",
}));

app.Run();
