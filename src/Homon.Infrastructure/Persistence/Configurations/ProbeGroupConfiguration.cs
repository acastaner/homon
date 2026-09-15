using Homon.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class ProbeGroupConfiguration : IEntityTypeConfiguration<ProbeGroup>
{
    public void Configure(EntityTypeBuilder<ProbeGroup> builder)
    {
        builder.ToTable("ProbeGroups");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .HasMaxLength(ProbeGroup.NameMaxLength)
            .IsRequired();

        // Unique ignoring case, via NormalizedName — the same Normalized* pattern Identity's
        // own tables already use, not citext/ICU collations.
        builder.Property(g => g.NormalizedName)
            .HasMaxLength(ProbeGroup.NameMaxLength)
            .IsRequired();

        builder.HasIndex(g => g.NormalizedName)
            .IsUnique();

        // No unique index on Position — same reasoning as ProbeConfiguration.

        builder.HasMany(g => g.Members)
            .WithOne(m => m.Group)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
