using Homon.Domain.Weather;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class WeatherSettingsConfiguration : IEntityTypeConfiguration<WeatherSettings>
{
    public void Configure(EntityTypeBuilder<WeatherSettings> builder)
    {
        builder.ToTable("WeatherSettings");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Latitude).IsRequired();

        builder.Property(w => w.Longitude).IsRequired();

        // No max length beyond the domain constant — the singleton invariant (at most one
        // row, always at WeatherSettings.SingletonId) is enforced by that constant being the
        // only value ever written, not by a database constraint.
        builder.Property(w => w.Place).HasMaxLength(WeatherSettings.PlaceMaxLength);

        builder.Property(w => w.Units).IsRequired();

        builder.Property(w => w.CreatedAt).IsRequired();

        builder.Property(w => w.UpdatedAt).IsRequired();
    }
}
