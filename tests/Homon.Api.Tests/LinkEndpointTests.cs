using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Links;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class LinkEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task GET_on_a_fresh_database_returns_an_empty_array()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.GetAsync("/api/v1/links");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetArrayLength());
    }

    [DatabaseFact]
    public async Task POST_creates_a_link_appended_at_the_end_of_the_display_order()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var first = await CreateAsync(client, "First link", "https://first.test");
        var second = await CreateAsync(client, "Second link", "https://second.test");

        Assert.Equal(HttpStatusCode.Created, first.Response.StatusCode);
        Assert.NotNull(first.Response.Headers.Location);
        Assert.Equal($"/api/v1/links/{first.Body.GetProperty("id").GetGuid()}", first.Response.Headers.Location!.ToString());
        Assert.Equal("First link", first.Body.GetProperty("title").GetString());
        Assert.Equal("https://first.test", first.Body.GetProperty("url").GetString());

        // Position is not on the wire — order is the format — so the relative order between
        // two links created back to back is what a GET must reflect.
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/links");
        var titles = list.EnumerateArray().Select(l => l.GetProperty("title").GetString()).ToArray();
        var firstIndex = Array.IndexOf(titles, "First link");
        var secondIndex = Array.IndexOf(titles, "Second link");

        Assert.True(secondIndex > firstIndex);
        Assert.False(second.Body.TryGetProperty("position", out _));
    }

    [DatabaseTheory]
    [InlineData(null, "https://valid.test", "title")]
    [InlineData("", "https://valid.test", "title")]
    [InlineData("Valid title", null, "url")]
    [InlineData("Valid title", "", "url")]
    [InlineData("Valid title", "javascript:alert(1)", "url")]
    [InlineData("Valid title", "ftp://example.test", "url")]
    public async Task POST_rejects_invalid_fields(string? title, string? url, string failingField)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/links", new { title, url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(failingField, out _));
    }

    [DatabaseFact]
    public async Task POST_rejects_an_over_length_url()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var overLongUrl = "https://over-length.test/" + new string('x', Link.UrlMaxLength);

        var response = await client.PostAsJsonAsync(
            "/api/v1/links", new { title = "Valid title", url = overLongUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("url", out _));
    }

    [DatabaseFact]
    public async Task POST_rejects_an_over_length_title()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/links",
            new { title = new string('x', Link.TitleMaxLength + 1), url = "https://over-length-title.test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
    }

    [DatabaseFact]
    public async Task PUT_edits_fields_and_leaves_order_unchanged()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var first = await CreateAsync(client, "Editable link", "https://editable.test");
        await CreateAsync(client, "Sibling link", "https://sibling.test");
        var id = first.Body.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/links/{id}",
            new { title = "Renamed link", url = "https://renamed.test", description = "A new detail" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed link", body.GetProperty("title").GetString());
        Assert.Equal("https://renamed.test", body.GetProperty("url").GetString());
        Assert.Equal("A new detail", body.GetProperty("description").GetString());

        // Order is untouched by an edit: the edited link stays before its sibling.
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/links");
        var titles = list.EnumerateArray().Select(l => l.GetProperty("title").GetString()).ToArray();
        var renamedIndex = Array.IndexOf(titles, "Renamed link");
        var siblingIndex = Array.IndexOf(titles, "Sibling link");

        Assert.True(siblingIndex > renamedIndex);
    }

    [DatabaseFact]
    public async Task PUT_trims_and_normalises_an_empty_description_to_null()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, "Description link", "https://description.test");
        var id = created.Body.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/links/{id}",
            new { title = "Description link", url = "https://description.test", description = "   " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
    }

    [DatabaseFact]
    public async Task PUT_on_an_unknown_id_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/links/{Guid.NewGuid()}", new { title = "n", url = "https://n.test" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DatabaseFact]
    public async Task DELETE_requires_the_json_content_type_that_closes_the_CSRF_guard()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, "Deletable link", "https://deletable.test");
        var id = created.Body.GetProperty("id").GetGuid();

        // What a cross-site <form method="post"> could produce: no application/json content
        // type. Model AuthenticationEndpointTests.cs's "Sign_out_refuses_a_form_post".
        using var form = new FormUrlEncodedContent([]);
        using var noContentTypeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/links/{id}")
        {
            Content = form,
        };
        var refused = await client.SendAsync(noContentTypeRequest);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);

        using var jsonRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/links/{id}")
        {
            Content = JsonContent.Create(new { }),
        };
        var deleted = await client.SendAsync(jsonRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var unknownRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/links/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { }),
        };
        var unknown = await client.SendAsync(unknownRequest);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [DatabaseFact]
    public async Task Order_accepts_a_permutation_and_rejects_missing_extra_or_duplicate_ids()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var a = await CreateAsync(client, "Order A", "https://order-a.test");
        var b = await CreateAsync(client, "Order B", "https://order-b.test");
        var c = await CreateAsync(client, "Order C", "https://order-c.test");

        var aId = a.Body.GetProperty("id").GetGuid();
        var bId = b.Body.GetProperty("id").GetGuid();
        var cId = c.Body.GetProperty("id").GetGuid();

        // The class shares one database clone across its methods — read the full set back
        // rather than assuming these three are the whole table.
        var existing = await client.GetFromJsonAsync<JsonElement>("/api/v1/links");
        var allIds = existing.EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();

        // Move C to the front, keep the rest of the existing order intact.
        var reorderedIds = new[] { cId }.Concat(allIds.Where(id => id != cId)).ToList();

        var reordered = await client.PutAsJsonAsync("/api/v1/links/order", new { linkIds = reorderedIds });
        Assert.Equal(HttpStatusCode.NoContent, reordered.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/links");
        var order = list.EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(reorderedIds, order);

        var missing = await client.PutAsJsonAsync("/api/v1/links/order", new { linkIds = new[] { aId, bId } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var extra = await client.PutAsJsonAsync(
            "/api/v1/links/order", new { linkIds = allIds.Append(Guid.NewGuid()).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);

        var duplicate = await client.PutAsJsonAsync(
            "/api/v1/links/order", new { linkIds = new[] { aId, aId, bId, cId } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [DatabaseFact]
    public async Task GET_is_open_to_an_anonymous_caller_by_default()
    {
        using var anonymous = TestClient.Create(factory);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/links")).StatusCode);
    }

    [DatabaseFact]
    public async Task The_write_auth_matrix_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("link write attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var body = new { title = "n", url = "https://n.test" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/links", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/links", body)).StatusCode);

        var linkId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/links/{linkId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/links/{linkId}", body)).StatusCode);

        using var anonymousDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/links/{linkId}")
        {
            Content = JsonContent.Create(new { }),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(anonymousDelete)).StatusCode);

        using var keyedDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/links/{linkId}")
        {
            Content = JsonContent.Create(new { }),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.SendAsync(keyedDelete)).StatusCode);

        var order = new { linkIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/links/order", order)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync("/api/v1/links/order", order)).StatusCode);
    }

    [Fact]
    public async Task GET_is_refused_anonymously_once_readers_must_sign_in()
    {
        // The ReaderHandler refuses an anonymous caller before the endpoint ever runs, so
        // this needs no database at all — same pattern as MetaEndpointTests.cs's
        // ConfiguredFactory, on the deliberately unreachable connection string every plain
        // HomonApiFactory carries.
        using var gated = new ConfiguredFactory();
        using var gatedClient = TestClient.Create(gated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await gatedClient.GetAsync("/api/v1/links")).StatusCode);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> CreateAsync(
        HttpClient client, string title, string url)
    {
        var response = await client.PostAsJsonAsync("/api/v1/links", new { title, url });
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
