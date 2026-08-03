using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Mind.Hearing;

namespace Mind.Perception;

/// <summary>
/// EF Core context for Perception's durable state: the sound-unit codebook. One table, <c>codebook</c>,
/// holding a single row whose prototypes/counts live in a jsonb document. Persisting this is what makes
/// unit ids stable across restarts — "sound #3" stays the same sound — so the facts keyed on those ids
/// (in Mind.Facts) keep their meaning session to session.
/// </summary>
public sealed class CodebookDbContext : DbContext
{
    public CodebookDbContext(DbContextOptions<CodebookDbContext> options) : base(options)
    {
    }

    public DbSet<StoredCodebook> Codebooks => Set<StoredCodebook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The snapshot is stored as one opaque jsonb document — we only ever read/write it whole, so a
        // JSON conversion is the simplest fit; a comparer makes EF change-tracking treat it by value.
        var snapshotConverter = new ValueConverter<CodebookSnapshot, string>(
            snapshot => JsonSerializer.Serialize(snapshot, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<CodebookSnapshot>(json, (JsonSerializerOptions?)null)
                    ?? new CodebookSnapshot(Array.Empty<float[]>(), Array.Empty<int>()));

        var snapshotComparer = new ValueComparer<CodebookSnapshot>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            snapshot => snapshot == null
                ? 0
                : JsonSerializer.Serialize(snapshot, (JsonSerializerOptions?)null).GetHashCode(),
            snapshot => JsonSerializer.Deserialize<CodebookSnapshot>(
                            JsonSerializer.Serialize(snapshot, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null)
                        ?? new CodebookSnapshot(Array.Empty<float[]>(), Array.Empty<int>()));

        modelBuilder.Entity<StoredCodebook>(entity =>
        {
            entity.ToTable("codebook");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.UnitCount);
            entity.Property(c => c.UpdatedAt);
            entity.Property(c => c.Snapshot)
                .HasConversion(snapshotConverter, snapshotComparer)
                .HasColumnType("jsonb")
                .IsRequired();
        });
    }
}
