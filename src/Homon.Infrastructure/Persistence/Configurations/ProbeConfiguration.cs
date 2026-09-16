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

        // One nullable owned type per kind, mapped to its own jsonb column — plan 003's
        // Decision 1. *Rejected*: a separate table per kind — every probe-list read needs a
        // conditional join per kind, and a new kind is a migration touching the shared read
        // path; a single kind-agnostic jsonb blob — loses compile-time field names and mixes
        // each kind's validation together; flat nullable scalar columns prefixed by kind —
        // four kinds x ~6 fields is 20+ mostly-null columns with no natural home for negation
        // flags. 004 (SMB) and 005 (SNMP) copy this same shape by name.
        builder.OwnsOne(p => p.HttpOptions, http =>
        {
            http.ToJson();
            http.OwnsOne(o => o.Credential);
        });
    }
}
