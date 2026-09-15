using Homon.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class ProbeGroupMembershipConfiguration : IEntityTypeConfiguration<ProbeGroupMembership>
{
    public void Configure(EntityTypeBuilder<ProbeGroupMembership> builder)
    {
        builder.ToTable("ProbeGroupMemberships");

        builder.HasKey(m => new { m.GroupId, m.ProbeId });

        // Postgres does not index a foreign key on its own — this explicit index is what
        // makes "delete a probe, cascade its memberships" and "list a probe's groups" cheap.
        builder.HasIndex(m => m.ProbeId);

        builder.HasOne<Probe>()
            .WithMany()
            .HasForeignKey(m => m.ProbeId)
            .OnDelete(DeleteBehavior.Cascade);

        // The Group side (GroupId -> ProbeGroup, cascade) is configured from ProbeGroupConfiguration's
        // HasMany(g => g.Members) to keep the one-to-many navigation on ProbeGroup in one place.
    }
}
