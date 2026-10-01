using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class ReporterConfiguration : IEntityTypeConfiguration<Reporter>
{
    public void Configure(EntityTypeBuilder<Reporter> builder)
    {
        builder.ToTable("Reporters");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .HasMaxLength(Reporter.NameMaxLength)
            .IsRequired();

        builder.Property(r => r.NormalizedName)
            .HasMaxLength(Reporter.NameMaxLength)
            .IsRequired();

        // Case-insensitive uniqueness through a normalised column, Identity's own pattern, not
        // citext — see ProbeGroupConfiguration.
        builder.HasIndex(r => r.NormalizedName)
            .IsUnique();

        builder.Property(r => r.Identifier)
            .HasMaxLength(Reporter.IdentifierLength)
            .IsRequired();

        // Unique, and the hottest lookup in the module: a message probe resolves its reporter by
        // this on every poll (Probe.Host holds it — plan 021's Decision 3).
        builder.HasIndex(r => r.Identifier)
            .IsUnique();

        builder.Property(r => r.Description)
            .HasMaxLength(Reporter.DescriptionMaxLength);

        // String, not int, per the convention ProbeConfiguration sets: a dump stays legible and a
        // third visibility later renumbers no stored value.
        //
        // Deliberately no HasDefaultValue, unlike ApiKeyConfiguration's Scope: Administrator is
        // both the least-privileged value and the CLR default, so a SQL default would make EF
        // omit the column from inserts — harmless here, but it would also silently re-interpret
        // every explicitly-Administrator row if that default were ever changed.
        builder.Property(r => r.BodyVisibility)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // The 1:1 pairing, mapped from the dependent side with no navigation on either: a
        // reporter cannot exist without a key, while a key exists perfectly well without a
        // reporter (create-api-key still mints read-only keys for scripts that only read).
        // HasForeignKey<Reporter> is what gives ApiKeyId its unique index, and mapping it from
        // here keeps Domain/Auth unaware that Domain/Messaging exists.
        //
        // Restrict, not Cascade: an ApiKey row is revoked, never deleted (see ApiKey.RevokedAt),
        // so this should never fire — and Restrict makes that assumption loud if it is ever wrong.
        builder.HasOne<ApiKey>()
            .WithOne()
            .HasForeignKey<Reporter>(r => r.ApiKeyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
