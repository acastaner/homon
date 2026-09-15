using Homon.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class ProbeConfiguration : IEntityTypeConfiguration<Probe>
{
    public void Configure(EntityTypeBuilder<Probe> builder)
    {
        builder.ToTable("Probes");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .HasMaxLength(Probe.NameMaxLength)
            .IsRequired();

        builder.Property(p => p.Host)
            .HasMaxLength(Probe.HostMaxLength)
            .IsRequired();

        // String, not int: a dump stays readable ("Ping"/"Http"/…) and a later member
        // inserted in the middle of the enum does not silently reassign every stored value —
        // plan 002's Decision 1, the convention later modules' own enums may follow.
        builder.Property(p => p.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.PollInterval)
            .IsRequired();

        builder.Property(p => p.FailureThreshold)
            .IsRequired();

        // No unique index on Position anywhere in this module: PostgreSQL checks a
        // non-deferrable unique constraint row by row, so swapping two positions inside one
        // transaction would fail partway through. Position is a sort key, not a dense index —
        // see plan 002's Decision 7.
    }
}
