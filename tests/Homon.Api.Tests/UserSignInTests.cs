using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Homon.Api.Tests;

/// <summary>
/// The sign-in path for an address that is <i>not</i> the bootstrap administrator consults
/// the user store, so it needs a database — even though no account exists in it yet.
/// </summary>
public class UserSignInTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseTheory]
    [InlineData("somebody-else@example.test", HomonApiFactory.AdministratorPassword)]
    [InlineData("", "")]
    public async Task An_unknown_address_gets_the_same_401_as_a_wrong_password(string email, string password)
    {
        using var client = TestClient.Create(factory);

        var response = await client.SignInAsync(email, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("The email address or password is incorrect.", problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
