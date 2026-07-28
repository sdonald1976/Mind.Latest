using System.Net.Http.Json;
using Mind.Contracts;

namespace Mind.Perception;

/// <summary>
/// Hands a formed memory to the Memory service over HTTP. This is the only
/// place in Perception that knows Memory lives across a network boundary —
/// everything upstream of it just depends on <see cref="IMemorySink"/>.
/// </summary>
public sealed class HttpMemorySink : IMemorySink
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpMemorySink> _logger;

    public HttpMemorySink(HttpClient http, ILogger<HttpMemorySink> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task StoreAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("/memories", memory, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Memory service rejected memory {MemoryId}: HTTP {Status}.",
                memory.Id, (int)response.StatusCode);
        }
    }
}
