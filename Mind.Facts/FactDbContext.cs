using Microsoft.EntityFrameworkCore;

namespace Mind.Facts;

/// <summary>
/// EF Core context for the Mind's distilled facts. One table, <c>facts</c>, keyed by sound-unit.
/// Where memories are an ever-growing log, facts are a small standing set the Mind overwrites as its
/// knowledge shifts — so this table is a current picture, not a history.
/// </summary>
public sealed class FactDbContext : DbContext
{
    public FactDbContext(DbContextOptions<FactDbContext> options) : base(options)
    {
    }

    public DbSet<StoredFact> Facts => Set<StoredFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFact>(entity =>
        {
            entity.ToTable("facts");
            // The unit is the fact's identity — one standing fact per known sound. It comes from the
            // codebook, not the database, so EF must not treat it as generated.
            entity.HasKey(f => f.Unit);
            entity.Property(f => f.Unit).ValueGeneratedNever();
            entity.Property(f => f.Kind).IsRequired();
            entity.Property(f => f.Statement).IsRequired();
            entity.Property(f => f.Confidence);
            entity.Property(f => f.Evidence);
            entity.Property(f => f.UpdatedAt);
        });
    }
}
