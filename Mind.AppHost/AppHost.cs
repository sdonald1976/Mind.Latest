var builder = DistributedApplication.CreateBuilder(args);

// Postgres: durable home for the Mind's memories. A data volume keeps them
// across full restarts, not just app restarts. The generated password is
// persisted to the AppHost's user secrets, so it stays stable run to run.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var memoryDb = postgres.AddDatabase("mind-memory-db");

// Memory: stores the memories the Mind forms and serves them back for recall.
var memory = builder.AddProject<Projects.Mind_Memory>("mind-memory")
    .WithReference(memoryDb)
    .WaitFor(memoryDb);

// Perception: always-on, lives in time. Senses the world, brackets a memory as
// salience departs from and returns to idle, and hands each finished memory to
// the Memory service. WithReference injects Memory's address into Perception.
builder.AddProject<Projects.Mind_Perception>("mind-perception")
    .WithReference(memory)
    .WaitFor(memory);

builder.Build().Run();
