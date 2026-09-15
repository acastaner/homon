using Homon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name)
            .HasMaxLength(ApiKey.NameMaxLength)
            .IsRequired();

        // The only column a lookup ever uses, so it is the only index. Fixed-length: the
        // minter always produces exactly TokenIdLength characters.
        builder.Property(k => k.TokenId)
            .HasMaxLength(ApiKey.TokenIdLength)
            .IsRequired();

        builder.HasIndex(k => k.TokenId)
            .IsUnique();

        builder.Property(k => k.SecretHash)
            .IsRequired();

        // String, not int: a dump stays readable ("Read"/"ReadWrite") and a future third scope
        // does not silently renumber an already-stored value.
        builder.Property(k => k.Scope)
            .HasConversion<string>()
            .HasMaxLength(ApiKey.ScopeMaxLength)
            // Existing rows (any key minted before this column, including the runbook's
            // "clockmaster restic" key) become ReadWrite on migration, not Read — see
            // Decision 2. New keys default to Read instead; see CreateApiKeyArguments.
            .HasDefaultValue(ApiKeyScope.ReadWrite)
            // HasDefaultValue alone marks the column ValueGenerated.OnAdd, so EF Core omits
            // this property from the INSERT whenever it holds its CLR default — and
            // ApiKeyScope.Read (0) IS that default. Every explicitly-minted Read key would
            // silently be stored as ReadWrite, the column's default, instead. ValueGeneratedNever
            // keeps the SQL default (for the migration's backfill of pre-existing rows) while
            // making every insert always send ApiKeyIssuer's explicit value.
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(k => k.ExpiresAt);
    }
}
