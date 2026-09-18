using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Homon.Api.Tests;

/// <summary>
/// The session cookie's Secure attribute, which decides whether a LAN-first deployment can sign
/// in at all. Production is the interesting environment here, and <see cref="HomonApiFactory"/>
/// deliberately runs as Development — so both factories below override it, and supply the one
/// extra setting a Production host validates on start.
/// </summary>
public class PlainTextSessionTests
{
    [Fact]
    public async Task Production_marks_the_session_cookie_secure_by_default()
    {
        using var factory = new ProductionFactory();
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.Contains("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowing_plain_text_sessions_drops_secure_for_a_plain_http_request()
    {
        using var factory = new PlainTextProductionFactory();
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.DoesNotContain("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowing_plain_text_sessions_keeps_secure_for_an_https_request()
    {
        using var factory = new PlainTextProductionFactory();
        using var client = TestClient.Create(factory);

        // SameAsRequest, not None (D5): the flag must not cost a TLS origin its Secure cookie.
        // The in-process host takes Request.Scheme straight from the request URI, so an https
        // base address is the same signal the forwarded-headers middleware produces in
        // production when nginx passes X-Forwarded-Proto: https through. SignInAsync posts a
        // relative URI, so this is all it takes.
        client.BaseAddress = new Uri("https://localhost");

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.Contains("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("homon.sid=", StringComparison.Ordinal));

    /// <summary>
    /// Production, with the settings a Production host validates on start that
    /// <see cref="HomonApiFactory"/> leaves unset because Development does not check them.
    /// </summary>
    private class ProductionFactory : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(Environments.Production);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // A Production host refuses to start without one rather than silently
                    // logging alerts instead of sending them. Never used: nothing here sends.
                    ["Email:ResendApiToken"] = "re_test_token",
                }));
        }
    }

    private sealed class PlainTextProductionFactory : ProductionFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:AllowPlainTextSessions"] = "true",
                }));
        }
    }
}
