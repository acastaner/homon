using Homon.Domain.Links;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class LinkConfiguration : IEntityTypeConfiguration<Link>
{
    public void Configure(EntityTypeBuilder<Link> builder)
    {
        builder.ToTable("Links");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Title)
            .HasMaxLength(Link.TitleMaxLength)
            .IsRequired();

        builder.Property(l => l.Url)
            .HasMaxLength(Link.UrlMaxLength)
            .IsRequired();

        builder.Property(l => l.Description)
            .HasMaxLength(Link.DescriptionMaxLength);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .IsRequired();

        // No unique index on Position — same reasoning as ProbeConfiguration.
    }
}
