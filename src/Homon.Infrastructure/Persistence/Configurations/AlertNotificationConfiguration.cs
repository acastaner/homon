using Homon.Domain.Alerts;
using Homon.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class AlertNotificationConfiguration : IEntityTypeConfiguration<AlertNotification>
{
    public void Configure(EntityTypeBuilder<AlertNotification> builder)
    {
        builder.ToTable("AlertNotifications");

        builder.HasKey(n => n.Id);

        // Identity long key, as for ProbeObservations: an append-mostly, time-ordered table.
        builder.Property(n => n.Id).ValueGeneratedOnAdd();

        // Strings, not ints, for the same reason as Probe.Kind — a dump stays readable.
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(n => n.State).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(n => n.ProbeName).HasMaxLength(200).IsRequired();

        builder.Property(n => n.Detail).HasMaxLength(500);

        builder.Property(n => n.LastError).HasMaxLength(500);

        builder.Property(n => n.OccurredAt).IsRequired();

        builder.Property(n => n.NextAttemptAt).IsRequired();

        // SetNull, not Cascade: the row is a record of what was mailed and ProbeName is a
        // snapshot, so deleting a probe must not erase the history of its alerts.
        builder.HasOne<Probe>()
            .WithMany()
            .HasForeignKey(n => n.ProbeId)
            .OnDelete(DeleteBehavior.SetNull);

        // The dispatcher's query: Pending rows that are due.
        builder.HasIndex(n => new { n.State, n.NextAttemptAt });

        // The deliveries list (newest first) and the retention sweep.
        builder.HasIndex(n => n.OccurredAt);
    }
}
