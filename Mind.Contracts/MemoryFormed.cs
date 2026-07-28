namespace Mind.Contracts;

/// <summary>
/// Published when the Mind has formed a memory. This is the message that travels
/// the bus from Perception (which forms it) to Memory (which stores it), and
/// that future services can subscribe to as well.
/// </summary>
/// <param name="Memory">The finished memory.</param>
public sealed record MemoryFormed(Memory Memory);
