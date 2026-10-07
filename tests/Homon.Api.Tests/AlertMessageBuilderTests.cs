using Homon.Domain.Alerts;
using Homon.Infrastructure.Alerts;

namespace Homon.Api.Tests;

public class AlertMessageBuilderTests
{
    private const string BaseUrl = "https://homon.test";

    private static readonly DateTimeOffset Down = new(2026, 3, 4, 10, 5, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<string> Recipients = ["a@example.test", "b@example.test"];

    private static AlertNotification Row(
        AlertKind kind, string name = "NAS", Guid? probeId = null, string? detail = "ping: TimedOut", TimeSpan? downFor = null) =>
        new()
        {
            Kind = kind,
            ProbeId = probeId,
            ProbeName = name,
            Detail = detail,
            DownSince = kind == AlertKind.Test ? null : Down,
            OccurredAt = kind == AlertKind.Up ? Down + (downFor ?? TimeSpan.FromMinutes(5)) : Down,
        };

    [Fact]
    public void The_three_subjects_are_exact()
    {
        Assert.Equal("[Homon] NAS is down", AlertMessageBuilder.Build(Row(AlertKind.Down), Recipients, BaseUrl).Subject);
        Assert.Equal("[Homon] NAS is back up", AlertMessageBuilder.Build(Row(AlertKind.Up), Recipients, BaseUrl).Subject);
        Assert.Equal("[Homon] Test alert", AlertMessageBuilder.Build(Row(AlertKind.Test), Recipients, BaseUrl).Subject);
    }

    [Fact]
    public void Control_characters_in_a_name_are_stripped_from_the_subject()
    {
        var message = AlertMessageBuilder.Build(Row(AlertKind.Down, name: "NAS\r\nBcc: x@y.z"), Recipients, BaseUrl);

        Assert.Equal("[Homon] NASBcc: x@y.z is down", message.Subject);
        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain('\n', message.Subject);
    }

    [Fact]
    public void The_down_text_has_the_utc_stamp_the_detail_and_the_probe_link()
    {
        var id = Guid.NewGuid();

        var message = AlertMessageBuilder.Build(Row(AlertKind.Down, probeId: id), Recipients, BaseUrl + "/");

        Assert.Contains("NAS is down.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Since:  2026-03-04 10:05 UTC", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Detail: ping: TimedOut", message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"https://homon.test/probes/{id}", message.TextBody, StringComparison.Ordinal);
        Assert.Equal(Recipients, message.To);
    }

    [Fact]
    public void The_down_text_omits_the_detail_line_when_there_is_no_detail()
    {
        var message = AlertMessageBuilder.Build(Row(AlertKind.Down, detail: null), Recipients, BaseUrl);

        Assert.DoesNotContain("Detail:", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_up_text_says_how_long_it_was_down()
    {
        var message = AlertMessageBuilder.Build(
            Row(AlertKind.Up, probeId: Guid.NewGuid(), downFor: TimeSpan.FromMinutes(72)), Recipients, BaseUrl);

        Assert.Contains("NAS is back up after 1 h 12 min.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Down from: 2026-03-04 10:05 UTC", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Back up:   2026-03-04 11:17 UTC", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_test_names_its_recipients_and_links_the_alerts_page()
    {
        var message = AlertMessageBuilder.Build(Row(AlertKind.Test), Recipients, BaseUrl);

        Assert.Contains("This is a test alert from Homon.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Sent to: a@example.test, b@example.test", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://homon.test/admin/alerts", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_with_no_probe_carries_no_probe_link()
    {
        var message = AlertMessageBuilder.Build(Row(AlertKind.Down, probeId: null), Recipients, BaseUrl);

        Assert.DoesNotContain("/probes/", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_is_encoded_value_by_value()
    {
        var message = AlertMessageBuilder.Build(
            Row(AlertKind.Down, name: "<script>x</script>", detail: "a & b"), Recipients, BaseUrl);

        Assert.NotNull(message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("a &amp; b", message.HtmlBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30, "less than a minute")]
    [InlineData(300, "5 min")]
    [InlineData(3600, "1 h")]
    [InlineData(3660, "1 h 1 min")]
    [InlineData(25 * 3600, "1 d 1 h")]
    [InlineData(48 * 3600, "2 d")]
    public void FormatDuration_follows_the_table(int seconds, string expected) =>
        Assert.Equal(expected, AlertMessageBuilder.FormatDuration(TimeSpan.FromSeconds(seconds)));
}
