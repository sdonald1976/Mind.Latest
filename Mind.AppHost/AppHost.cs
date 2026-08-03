var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ: the Mind's message bus. Perception publishes formed memories here;
// Memory (and, later, other services) consume them. A data volume keeps durable
// messages across broker restarts; the management UI shows queues in the browser.
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();

var pgPassword = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: pgPassword).WithDataVolume();

var memoryDb = postgres.AddDatabase("mind-memory-db");
var factsDb = postgres.AddDatabase("mind-facts-db");
var perceptionDb = postgres.AddDatabase("mind-perception-db");

// Memory: consumes formed-memory messages, stores them, and serves recall.
var memory = builder.AddProject<Projects.Mind_Memory>("mind-memory")
    .WithReference(memoryDb)
    .WithReference(rabbitmq)
    .WaitFor(memoryDb)
    .WaitFor(rabbitmq);

// Perception: always-on, lives in time. Senses the world, brackets a memory as
// salience departs from and returns to idle, and publishes each finished memory
// to the bus. It no longer talks to Memory directly — the broker decouples them.
builder.AddProject<Projects.Mind_Perception>("mind-perception")
    .WithReference(perceptionDb)
    .WithReference(rabbitmq)
    .WaitFor(perceptionDb)
    .WaitFor(rabbitmq);

// Facts: subscribes to the same memory stream and distils standing facts from it
// (where learning lives). A second consumer alongside Memory — the pub-sub design
// paying off: it joins by listening, touching nothing else.
builder.AddProject<Projects.Mind_Facts>("mind-facts")
    .WithReference(factsDb)
    .WithReference(rabbitmq)
    .WaitFor(factsDb)
    .WaitFor(rabbitmq);

builder.Build().Run();
