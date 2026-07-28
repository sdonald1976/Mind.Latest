using Microsoft.EntityFrameworkCore;

namespace Mind.Memory;

/// <summary>Postgres-backed <see cref="IMemoryStore"/> over EF Core.</summary>
public sealed class EfMemoryStore : IMemoryStore
{
    private readonly MemoryDbContext _db;

    public EfMemoryStore(MemoryDbContext db) => _db = db;

    public async Task AddAsync(Mind.Contracts.Memory memory, CancellationToken cancellationToken = default)
    {
        // Idempotent by Id: a redelivered message (or a duplicate post) must not
        // double-store. If a rare race still lets a duplicate reach the database,
        // the primary key rejects it and the consumer's retry lands here to find
        // it already stored — so nothing is lost and nothing is duplicated.
        if (await _db.Memories.AnyAsync(m => m.Id == memory.Id, cancellationToken))
        {
            return;
        }

        _db.Memories.Add(StoredMemory.From(memory));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Mind.Contracts.Memory>> RecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Memories
            .OrderByDescending(m => m.EndedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.ToMemory()).ToList();
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Memories.CountAsync(cancellationToken);
}
