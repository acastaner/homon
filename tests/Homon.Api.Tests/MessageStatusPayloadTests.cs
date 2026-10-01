using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Homon.Api.Tests;

/// <summary>
/// What a reader may see about a message probe on <c>GET /status</c>. The payload is the one
/// surface anybody on the network reads, so the rules about what must not appear on it (plan 021,
/// Decisions 11 and 12) are pinned here rather than left to review.
/// </summary>
public class MessageStatusPayloadTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private const string BodyText = "repository /srv/backups on clockmaster: 412 files";
    private const string ReportedName = "clockmaster restic wrapper";

    [DatabaseFact]
    public async Task An_administrator_only_reporter_leaks_neither_its_body_nor_its_reported_name()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var probe = await SeedWatchedReporterAsync(client, visibility: null);

        var raw = await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync();

        // Even for a signed-in administrator: this payload is reader-facing, and the administrator
        // reads bodies on the reporter's own page.
        Assert.DoesNotContain(BodyText, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(ReportedName, raw, StringComparison.Ordinal);

        var message = await ReadMessageAsync(client, probe);
        Assert.Equal(JsonValueKind.Null, message.GetProperty("body").ValueKind);
        Assert.Equal("success", message.GetProperty("status").GetString());
        Assert.False(message.GetProperty("overdue").GetBoolean());
    }

    [DatabaseFact]
    public async Task A_reader_visible_reporter_puts_its_latest_body_on_the_payload()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var probe = await SeedWatchedReporterAsync(client, visibility: "reader");

        var message = await ReadMessageAsync(client, probe);

        Assert.Equal(BodyText, message.GetProperty("body").GetString());
        // The reported name is still absent: visibility is about the body, not about free text in
        // general, and the row's label is the probe's own administrator-chosen name.
        Assert.DoesNotContain(
            ReportedName,
            await (await client.GetAsync("/api/v1/status")).Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_reader_visible_body_is_capped_so_the_payload_stays_a_dashboard_payload()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var probe = await SeedWatchedReporterAsync(client, visibility: "reader", body: new string('x', 5_000));

        Assert.Equal(2_000, (await ReadMessageAsync(client, probe)).GetProperty("body").GetString()!.Length);
    }

    [DatabaseFact]
    public async Task A_reporter_past_its_deadline_is_flagged_overdue()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        // One minute, and then wait it out: the flag is computed against the request's clock, so
        // nothing has to poll for this assertion to hold.
        var probe = await SeedWatchedReporterAsync(client, visibility: null, recurrence: "PT1S");
        await Task.Delay(1_200);

        Assert.True((await ReadMessageAsync(client, probe)).GetProperty("overdue").GetBoolean());
    }

    [DatabaseFact]
    public async Task A_probe_of_any_other_kind_carries_no_message_object()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = $"ping-{Guid.NewGuid():N}",
            host = "ping.test",
            kind = "ping",
            pollIntervalSeconds = 900,
        });

        var probeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var row = await FindProbeAsync(client, probeId);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("message").ValueKind);
    }

    private async Task<Guid> SeedWatchedReporterAsync(
        HttpClient client, string? visibility, string? body = null, string? recurrence = null)
    {
        var created = await client.PostAsJsonAsync("/api/v1/reporters", new
        {
            name = $"reporter-{Guid.NewGuid():N}",
            bodyVisibility = visibility,
        });
        created.EnsureSuccessStatusCode();

        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var reporter = createdBody.GetProperty("reporter");
        var token = createdBody.GetProperty("token").GetString()!;

        using var reporterClient = TestClient.Create(factory);
        reporterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var report = await reporterClient.PostAsJsonAsync("/api/v1/messages", new
        {
            name = ReportedName,
            status = "success",
            message = body ?? BodyText,
            recurrence,
        });
        report.EnsureSuccessStatusCode();

        var probe = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = $"watcher-{Guid.NewGuid():N}",
            host = reporter.GetProperty("identifier").GetString(),
            kind = "message",
            pollIntervalSeconds = 900,
        });
        probe.EnsureSuccessStatusCode();

        return (await probe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> FindProbeAsync(HttpClient client, Guid probeId)
    {
        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");

        return status.GetProperty("probes").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == probeId);
    }

    private static async Task<JsonElement> ReadMessageAsync(HttpClient client, Guid probeId) =>
        (await FindProbeAsync(client, probeId)).GetProperty("message");
}
