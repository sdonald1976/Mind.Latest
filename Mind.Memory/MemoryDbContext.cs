using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Mind.Contracts;

namespace Mind.Memory;

/// <summary>
/// EF Core context for the Mind's memories. One table, <c>memories</c>, with the
/// perceptions serialized into a single jsonb column. Swapping how memories are
/// stored is a change contained entirely within this service.
/// </summary>
public sealed class MemoryDbContext : DbContext
{
    public MemoryDbContext(DbContextOptions<MemoryDbContext> options) : base(options)
    {
    }

    public DbSet<StoredMemory> Memories => Set<StoredMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Perceptions are stored as one jsonb document. We only ever read/write
        // the whole list, so an opaque JSON conversion is the simplest fit; a
        // comparer is supplied so EF change-tracking treats it by value.
        var perceptionsConverter = new ValueConverter<List<Perception>, string>(
            list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<Perception>>(json, (JsonSerializerOptions?)null)
                    ?? new List<Perception>());

        var perceptionsComparer = new ValueComparer<List<Perception>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            list => list == null
                ? 0
                : JsonSerializer.Serialize(list, (JsonSerializerOptions?)null).GetHashCode(),
            list => JsonSerializer.Deserialize<List<Perception>>(
                        JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                        (JsonSerializerOptions?)null)
                    ?? new List<Perception>());

        modelBuilder.Entity<StoredMemory>(entity =>
        {
            entity.ToTable("memories");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Place).IsRequired();
            entity.Property(m => m.StartedAt);
            entity.Property(m => m.EndedAt);
            entity.Property(m => m.Perceptions)
                .HasConversion(perceptionsConverter, perceptionsComparer)
                .HasColumnType("jsonb")
                .IsRequired();
        });
    }
}
