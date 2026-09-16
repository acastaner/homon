using Homon.Domain.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("Pages");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug)
            .HasMaxLength(Page.SlugMaxLength)
            .IsRequired();

        // Already canonical after validation (lower-case kebab, one regex) — unlike
        // ProbeGroup.Name (plans/002), no separate NormalizedSlug column is needed.
        builder.HasIndex(p => p.Slug)
            .IsUnique();

        builder.Property(p => p.Title)
            .HasMaxLength(Page.TitleMaxLength)
            .IsRequired();

        // No HasMaxLength: Postgres' text and varchar(n) perform identically, and the
        // BodyHtmlMaxLength cap is an API-boundary rule (Page.cs), not a storage one.
        builder.Property(p => p.BodyHtml)
            .IsRequired();
    }
}
