using Homon.Domain.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class AlertSettingsConfiguration : IEntityTypeConfiguration<AlertSettings>
{
    public void Configure(EntityTypeBuilder<AlertSettings> builder)
    {
        builder.ToTable("AlertSettings");

        builder.HasKey(s => s.Id);

        // Like WeatherSettings, the singleton invariant (at most one row, always at
        // AlertSettings.SingletonId) is held by that constant being the only id ever written,
        // not by a database constraint.
        builder.Property(s => s.ProtectedApiKey).HasMaxLength(2000);

        builder.Property(s => s.FromAddress).HasMaxLength(254).IsRequired();

        builder.Property(s => s.FromName).HasMaxLength(100).IsRequired();

        // A primitive collection: Npgsql maps List<string> to text[], so the recipients need no
        // child table for a list capped at AlertSettings.MaxRecipients.
        builder.Property(s => s.Recipients).IsRequired();

        builder.Property(s => s.CreatedAt).IsRequired();

        builder.Property(s => s.UpdatedAt).IsRequired();

        // Derived from the other columns; the Domain project has no EF reference to say so itself.
        builder.Ignore(s => s.IsReady);
    }
}
