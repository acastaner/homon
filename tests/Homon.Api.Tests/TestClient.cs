using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Homon.Api.Tests;

/// <summary>The two or three request shapes every test in the suite repeats.</summary>
internal static class TestClient
{
    /// <summary>
    /// A client that keeps cookies, so a sign-in on one request authenticates the next —
    /// which is exactly what the SPA does — and never follows redirects, because this API
    /// answers with status codes and a redirect would be a bug worth seeing.
    /// </summary>
    public static HttpClient Create(HomonApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    public static Task<HttpResponseMessage> SignInAsync(
        this HttpClient client,
        string email = HomonApiFactory.AdministratorEmail,
        string password = HomonApiFactory.AdministratorPassword,
        bool keepSignedIn = false) =>
        client.PostAsJsonAsync("/api/v1/auth/sign-in", new { email, password, keepSignedIn });

    /// <summary>
    /// Signs out the way the SPA does: an empty JSON body, which is what satisfies the
    /// endpoint's content-type check.
    /// </summary>
    public static Task<HttpResponseMessage> SignOutAsync(this HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/auth/sign-out", new { });
}
