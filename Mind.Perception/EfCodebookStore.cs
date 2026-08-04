using Microsoft.EntityFrameworkCore;
using Mind.Hearing;

namespace Mind.Perception;

/// <summary>Postgres-backed <see cref="ICodebookStore"/> over EF Core.</summary>
public sealed class EfCodebookStore : ICodebookStore
{
    private readonly PerceptionDbContext _db;

    public EfCodebookStore(PerceptionDbContext db) => _db = db;

    public async Task<CodebookSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.Codebooks
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == StoredCodebook.SingletonId, cancellationToken);

        return row?.Snapshot;
    }

    public async Task SaveAsync(CodebookSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        // Upsert the single row through the change tracker (one SaveChanges, one transaction), so a
        // reader never catches it half-written and it stays safe under Aspire's retrying strategy.
        var row = await _db.Codebooks
            .FirstOrDefaultAsync(c => c.Id == StoredCodebook.SingletonId, cancellationToken);

        if (row is null)
        {
            _db.Codebooks.Add(new StoredCodebook
            {
                Id = StoredCodebook.SingletonId,
                Snapshot = snapshot,
                UnitCount = snapshot.Prototypes.Length,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Snapshot = snapshot;
            row.UnitCount = snapshot.Prototypes.Length;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
