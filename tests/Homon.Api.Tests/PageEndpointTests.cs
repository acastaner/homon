using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Pages;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class PageEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task POST_stores_the_sanitised_body_not_the_raw_submitted_body()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(
            client, "sanitised-body", "Sanitised body",
            bodyHtml: "<p onclick=\"alert(1)\">hi</p>", isPublished: false);

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        Assert.Equal("<p>hi</p>", created.Body.GetProperty("bodyHtml").GetString());
    }

    [DatabaseFact]
    public async Task POST_rejects_an_empty_slug()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/pages", new { slug = "", title = "Valid title", bodyHtml = "<p>x</p>", isPublished = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [DatabaseFact]
    public async Task POST_rejects_an_over_length_slug()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var overLongSlug = new string('a', Page.SlugMaxLength + 1);

        var response = await client.PostAsJsonAsync(
            "/api/v1/pages",
            new { slug = overLongSlug, title = "Valid title", bodyHtml = "<p>x</p>", isPublished = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [DatabaseTheory]
    [InlineData("Upper-Case")]
    [InlineData("has spaces")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    public async Task POST_rejects_a_slug_with_invalid_characters(string invalidSlug)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/pages",
            new { slug = invalidSlug, title = "Valid title", bodyHtml = "<p>x</p>", isPublished = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [DatabaseFact]
    public async Task POST_rejects_a_duplicate_slug()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var first = await CreateAsync(client, "duplicate-slug", "First", "<p>a</p>", isPublished: false);
        Assert.Equal(HttpStatusCode.Created, first.Response.StatusCode);

        var second = await CreateAsync(client, "duplicate-slug", "Second", "<p>b</p>", isPublished: false);

        Assert.Equal(HttpStatusCode.BadRequest, second.Response.StatusCode);
        Assert.True(second.Body.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [DatabaseFact]
    public async Task A_genuine_race_between_two_concurrent_creates_of_the_same_slug_yields_one_success_and_one_validation_problem_not_a_500()
    {
        using var first = TestClient.Create(factory);
        using var second = TestClient.Create(factory);
        await first.SignInAsync();
        await second.SignInAsync();

        var body = new { slug = "racing-slug", title = "Racing page", bodyHtml = "<p>race</p>", isPublished = false };

        var firstTask = first.PostAsJsonAsync("/api/v1/pages", body);
        var secondTask = second.PostAsJsonAsync("/api/v1/pages", body);

        await Task.WhenAll(firstTask, secondTask);

        var responses = new[] { await firstTask, await secondTask };

        // Neither request may ever answer 500 — a race is an expected outcome the endpoint
        // must turn into the same validation problem the sequential check produces, never an
        // unhandled exception.
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);

        var losers = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToArray();
        Assert.Single(losers);
        Assert.Equal(HttpStatusCode.BadRequest, losers[0].StatusCode);

        var problem = await losers[0].Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [DatabaseFact]
    public async Task GET_pages_lists_only_published_pages_while_GET_admin_pages_lists_every_page()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        await CreateAsync(client, "published-page", "Published page", "<p>p</p>", isPublished: true);
        await CreateAsync(client, "draft-page", "Draft page", "<p>d</p>", isPublished: false);

        var published = await client.GetFromJsonAsync<JsonElement>("/api/v1/pages");
        var publishedSlugs = published.EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ToArray();
        Assert.Contains("published-page", publishedSlugs);
        Assert.DoesNotContain("draft-page", publishedSlugs);

        var admin = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/pages");
        var adminSlugs = admin.EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ToArray();
        Assert.Contains("published-page", adminSlugs);
        Assert.Contains("draft-page", adminSlugs);
    }

    [DatabaseFact]
    public async Task GET_by_slug_on_an_unpublished_page_is_404_anonymous_404_reader_and_200_administrator()
    {
        using var admin = TestClient.Create(factory);
        await admin.SignInAsync();

        await CreateAsync(admin, "unpublished-preview", "Unpublished preview", "<p>u</p>", isPublished: false);

        using var anonymous = TestClient.Create(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/pages/unpublished-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/pages/unpublished-preview")).StatusCode);
    }

    [DatabaseFact]
    public async Task GET_by_slug_on_an_unknown_slug_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.GetAsync("/api/v1/pages/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DatabaseFact]
    public async Task POST_rejects_an_over_length_title()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/pages",
            new
            {
                slug = "over-length-title",
                title = new string('x', Page.TitleMaxLength + 1),
                bodyHtml = "<p>x</p>",
                isPublished = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
    }

    [DatabaseFact]
    public async Task POST_rejects_a_body_over_the_cap_after_sanitisation()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        // Plain text inside one allowed tag: the sanitiser keeps it verbatim, so its length
        // survives sanitising and still exceeds the cap — this exercises the post-sanitise
        // check, not the cheap pre-check on the raw string (which is twice as large).
        var overLongBody = "<p>" + new string('x', Page.BodyHtmlMaxLength + 1) + "</p>";

        var response = await client.PostAsJsonAsync(
            "/api/v1/pages",
            new { slug = "over-length-body", title = "Over length body", bodyHtml = overLongBody, isPublished = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("bodyHtml", out _));
    }

    [DatabaseFact]
    public async Task DELETE_an_unknown_id_is_404_a_known_id_is_204_then_a_subsequent_GET_404s()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var unknown = await client.DeleteAsync($"/api/v1/pages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var created = await CreateAsync(client, "deletable-page", "Deletable page", "<p>d</p>", isPublished: true);
        var id = created.Body.GetProperty("id").GetGuid();

        var deleted = await client.DeleteAsync($"/api/v1/pages/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var subsequent = await client.GetAsync("/api/v1/pages/deletable-page");
        Assert.Equal(HttpStatusCode.NotFound, subsequent.StatusCode);
    }

    [DatabaseFact]
    public async Task The_write_auth_matrix_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("page write attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var body = new { slug = "auth-matrix-page", title = "n", bodyHtml = "<p>n</p>", isPublished = false };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/pages", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/pages", body)).StatusCode);

        var pageId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/pages/{pageId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/pages/{pageId}", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/pages/{pageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.DeleteAsync($"/api/v1/pages/{pageId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.GetAsync("/api/v1/admin/pages")).StatusCode);
    }

    [DatabaseFact]
    public async Task GET_pages_is_open_to_an_anonymous_caller_by_default()
    {
        using var anonymous = TestClient.Create(factory);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/pages")).StatusCode);
    }

    [Fact]
    public async Task GET_pages_is_refused_anonymously_once_readers_must_sign_in()
    {
        // The ReaderHandler refuses an anonymous caller before the endpoint ever runs, so
        // this needs no database at all — same pattern as MetaEndpointTests.cs's
        // ConfiguredFactory, on the deliberately unreachable connection string every plain
        // HomonApiFactory carries.
        using var gated = new ConfiguredFactory();
        using var gatedClient = TestClient.Create(gated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await gatedClient.GetAsync("/api/v1/pages")).StatusCode);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> CreateAsync(
        HttpClient client, string slug, string title, string bodyHtml, bool isPublished)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/pages", new { slug, title, bodyHtml, isPublished });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (response, body);
    }

    /// <summary>A <see cref="HomonApiFactory"/> with <c>Auth:RequireSignInForReaders</c> forced on.</summary>
    private sealed class ConfiguredFactory : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:RequireSignInForReaders"] = "true",
                }));
        }
    }
}
