using Microsoft.EntityFrameworkCore;

namespace Mind.Perception;

/// <summary>
/// EF Core context for the saved-clip catalogue, in its own database <c>mind-clips-db</c>. One table,
/// <c>clips</c> — a row per salient episode pointing at its WAV on disk, with a <see cref="StoredClip.Label"/>
/// a human fills in later. Kept separate from the codebook so each database is created whole by
/// <c>EnsureCreated</c> and can grow and be pruned on its own terms.
/// </summary>
public sealed class ClipDbContext : DbContext
{
    public ClipDbContext(DbContextOptions<ClipDbContext> options) : base(options)
    {
    }

    public DbSet<StoredClip> Clips => Set<StoredClip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
