var builder = DistributedApplication.CreateBuilder(args);

// Memory: stores the memories the Mind forms and serves them back for recall.
var memory = builder.AddProject<Projects.Mind_Memory>("mind-memory");

// Perception: always-on, lives in time. Senses the world, brackets a memory as
// salience departs from and returns to idle, and hands each finished memory to
// the Memory service. WithReference injects Memory's address into Perception.
builder.AddProject<Projects.Mind_Perception>("mind-perception")
    .WithReference(memory)
    .WaitFor(memory);

builder.Build().Run();
