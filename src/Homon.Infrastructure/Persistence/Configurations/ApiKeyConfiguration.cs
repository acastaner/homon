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
    }
}
