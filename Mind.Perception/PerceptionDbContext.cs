using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Mind.Hearing;

namespace Mind.Perception;

/// <summary>
/// EF Core context for Perception's durable state, in <c>mind-perception-db</c>. Two tables:
/// <c>codebook</c> (the sound-unit repertoire — a single jsonb row, so unit ids are stable across
/// restarts) and <c>clips</c> (the catalogue of saved sensory clips, one row per salient episode,
/// carrying the human label that teaches the Mind what a sound is).
/// </summary>
public sealed class PerceptionDbContext : DbContext
{
    public PerceptionDbContext(DbContextOptions<PerceptionDbContext> options) : base(options)
    {
    }

    public DbSet<StoredCodebook> Codebooks => Set<StoredCodebook>();

    public DbSet<StoredClip> Clips => Set<StoredClip>();

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

        modelBuilder.Entity<StoredClip>(entity =>
        {
            entity.ToTable("clips");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.Unit);
            entity.Property(c => c.CapturedAt);
            entity.Property(c => c.Seconds);
            entity.Property(c => c.SampleRate);
            entity.Property(c => c.Path).IsRequired();
            entity.Property(c => c.Label);
            entity.Property(c => c.LabeledAt);
            // Browsing and labelling both go by unit and by recency.
            entity.HasIndex(c => c.Unit);
            entity.HasIndex(c => c.CapturedAt);
        });
    }
}
