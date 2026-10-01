using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        // A long identity column for the reason ProbeObservationConfiguration gives: append-only
        // and time-ordered, so a random-order Guid key would only cost index bloat. It is also
        // the tie-break that makes "the newest message for this reporter" a total order, which
        // the retention sweep's keep-the-latest rule depends on.
        builder.Property(m => m.Id)
            .ValueGeneratedOnAdd();

        builder.Property(m => m.ReceivedAt)
            .IsRequired();

        builder.Property(m => m.Name)
            .HasMaxLength(Message.NameMaxLength)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasMaxLength(Message.DescriptionMaxLength);

        // No length: the body is capped on the way in, in bytes, by MessageBody.Truncate. A
        // character cap here would be a second, differently-counted limit for the same field.
        builder.Property(m => m.Body);

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Category)
            .HasMaxLength(Message.CategoryMaxLength)
            .IsRequired();

        builder.Property(m => m.RecurrenceDeclaration)
            .HasMaxLength(Message.DeclarationMaxLength);

        // Composite (ReporterId, ReceivedAt): the newest-message lookup every message probe poll
        // makes, and the admin history list's ORDER BY, from one index — and it covers the
        // foreign key, which Postgres does not index on its own. The retention sweep's bare
        // "ReceivedAt < cutoff" gets no index of its own, the same posture ProbeObservations
        // takes: this table holds a few rows per reporter per day, so an hourly scan is free.
        builder.HasIndex(m => new { m.ReporterId, m.ReceivedAt });

        builder.HasOne<Reporter>()
            .WithMany(r => r.Messages)
            .HasForeignKey(m => m.ReporterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict for the same reason the reporter's own key is: a key is revoked, never
        // deleted, and keeping the attribution is the point — after a rotation, this still says
        // which credential filed the message.
        builder.HasOne<ApiKey>()
            .WithMany()
            .HasForeignKey(m => m.ReportedByKeyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
