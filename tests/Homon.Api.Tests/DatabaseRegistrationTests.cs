using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class DatabaseRegistrationTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task The_template_clone_is_fully_migrated()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.Contains("homon_test_", database.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Identity_and_api_key_tables_exist()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Equal(0, await database.Users.CountAsync());
        Assert.Equal(0, await database.Roles.CountAsync());
        Assert.Equal(0, await database.ApiKeys.CountAsync());
    }
}
