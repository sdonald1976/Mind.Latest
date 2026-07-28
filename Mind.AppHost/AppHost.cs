var builder = DistributedApplication.CreateBuilder(args);

// The Mind's always-on core: the heartbeat that lives in time.
builder.AddProject<Projects.Mind_Core>("mind-core");

builder.Build().Run();
