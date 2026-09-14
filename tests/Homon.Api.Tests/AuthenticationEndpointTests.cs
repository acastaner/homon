using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Homon.Api.Tests;

/// <summary>
/// The bootstrap administrator has no database row, so every test here runs against the
/// database-less factory: sign-in, the session and sign-out never touch PostgreSQL.
/// </summary>
public class AuthenticationEndpointTests(HomonApiFactory factory) : IClassFixture<HomonApiFactory>
{
    [Fact]
    public async Task Anonymous_session_is_a_204()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_signs_in_and_the_session_describes_them()
    {
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.Contains(signIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith("homon.sid=", StringComparison.Ordinal));

        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.Equal("administrator", session.GetProperty("kind").GetString());
        Assert.Equal(HomonApiFactory.AdministratorEmail, session.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Administrator_address_is_matched_case_insensitively()
    {
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync(email: HomonApiFactory.AdministratorEmail.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_for_the_administrator_is_a_401_with_no_cookie()
    {
        using var client = TestClient.Create(factory);

        var response = await client.SignInAsync(password: "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Sign-in failed", problem.GetProperty("title").GetString());
        Assert.Equal("The email address or password is incorrect.", problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Sign_out_ends_the_session_and_is_idempotent()
    {
        using var client = TestClient.Create(factory);

        await client.SignInAsync();

        var first = await client.SignOutAsync();
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var session = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);

        var second = await client.SignOutAsync();
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task Sign_out_refuses_a_form_post()
    {
        using var client = TestClient.Create(factory);

        await client.SignInAsync();

        // What a cross-site <form method="post"> can produce, and what the endpoint refuses
        // rather than needing an antiforgery token.
        using var form = new FormUrlEncodedContent([]);
        var response = await client.PostAsync("/api/v1/auth/sign-out", form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        Assert.Equal("administrator", session.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Keep_me_signed_in_issues_a_persistent_cookie()
    {
        using var client = TestClient.Create(factory);

        var response = await client.SignInAsync(keepSignedIn: true);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("homon.sid=", StringComparison.Ordinal));

        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Some_other_bearer_token_is_not_ours_and_falls_through_to_the_cookie()
    {
        // A token without our prefix is never looked up, so no database is involved.
        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOi.not.ours");

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
