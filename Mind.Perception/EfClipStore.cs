using Microsoft.EntityFrameworkCore;

namespace Mind.Perception;

/// <summary>Postgres-backed <see cref="IClipStore"/> over EF Core.</summary>
public sealed class EfClipStore : IClipStore
{
    private readonly PerceptionDbContext _db;

    public EfClipStore(PerceptionDbContext db) => _db = db;

    public async Task AddAsync(StoredClip clip, CancellationToken cancellationToken = default)
    {
        _db.Clips.Add(clip);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredClip>> RecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await _db.Clips
            .OrderByDescending(c => c.CapturedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Clips.CountAsync(cancellationToken);
}
