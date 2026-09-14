using System.Net;

namespace Homon.Api.Tests;

public class HealthEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task Health_is_healthy_when_the_database_answers()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

}

/// <summary>Separate class: the database-less factory must not construct the clone fixture.</summary>
public class HealthWithoutDatabaseTests(HomonApiFactory factory) : IClassFixture<HomonApiFactory>
{
    [Fact]
    public async Task Health_is_unhealthy_when_the_database_is_unreachable()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
