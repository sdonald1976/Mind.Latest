using Microsoft.EntityFrameworkCore;
using Mind.Contracts;

namespace Mind.Facts;

/// <summary>Postgres-backed <see cref="IFactStore"/> over EF Core.</summary>
public sealed class EfFactStore : IFactStore
{
    private readonly FactDbContext _db;

    public EfFactStore(FactDbContext db) => _db = db;

    public async Task ReplaceAsync(IReadOnlyList<Fact> facts, CancellationToken cancellationToken = default)
    {
        // Bring the table into line with the current known set. Facts are a standing picture, not a
        // log: units still known are updated, new ones added, and any the Mind has forgotten pruned.
        // Reconciling through the change tracker means one SaveChanges — a single transaction, so a
        // reader never catches the table mid-write, and it stays safe under Aspire's retrying strategy.
        var incoming = facts
            .Where(f => f.Unit is not null)
            .ToDictionary(f => f.Unit!.Value);

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.Facts.ToListAsync(cancellationToken);

        foreach (var row in existing)
        {
            if (incoming.TryGetValue(row.Unit, out var fact))
            {
                row.Kind = fact.Kind;
                row.Statement = fact.Statement;
                row.Confidence = fact.Confidence;
                row.Evidence = fact.Evidence;
                row.UpdatedAt = now;
                incoming.Remove(row.Unit); // handled — what's left is genuinely new
            }
            else
            {
                _db.Facts.Remove(row); // the Mind no longer knows this sound
            }
        }

        foreach (var fact in incoming.Values)
        {
            _db.Facts.Add(StoredFact.From(fact, now));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fact>> AllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Facts
            .OrderByDescending(f => f.Confidence)
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.ToFact()).ToList();
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Facts.CountAsync(cancellationToken);
}
