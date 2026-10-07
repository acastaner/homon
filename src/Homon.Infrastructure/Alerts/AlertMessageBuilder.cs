using System.Globalization;
using System.Net;
using System.Text;
using Homon.Domain.Alerts;
using Homon.Infrastructure.Email;

namespace Homon.Infrastructure.Alerts;

/// <summary>
/// Composes the email for one <see cref="AlertNotification"/> (plan 026, Decision 6): plain text
/// plus a small HTML part. It says <i>what</i>, never <i>from whom</i> — the sender is the
/// transport's business.
/// </summary>
/// <remarks>
/// Every interpolated value in the HTML goes through <see cref="WebUtility.HtmlEncode(string?)"/>.
/// Times are UTC and written as such: no time-zone setting exists yet. Links use the public base
/// URL, because the host a probe watches is something readers never see, so neither does the
/// email.
/// </remarks>
public static class AlertMessageBuilder
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static EmailMessage Build(
        AlertNotification notification, IReadOnlyList<string> recipients, string publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(recipients);

        var baseUrl = (publicBaseUrl ?? string.Empty).TrimEnd('/');
        var name = notification.ProbeName;
        var probeLink = notification.ProbeId is { } id ? $"{baseUrl}/probes/{id}" : null;

        string subject;
        var lines = new List<string>();

        switch (notification.Kind)
        {
            case AlertKind.Down:
                subject = $"[Homon] {StripControl(name)} is down";
                lines.Add($"{name} is down.");
                lines.Add(string.Empty);
                lines.Add($"Since:  {FormatTime(notification.DownSince ?? notification.OccurredAt)}");
                if (!string.IsNullOrWhiteSpace(notification.Detail))
                {
                    lines.Add($"Detail: {notification.Detail}");
                }

                AddLink(lines, probeLink);
                break;

            case AlertKind.Up:
                subject = $"[Homon] {StripControl(name)} is back up";
                if (notification.DownSince is { } downSince)
                {
                    lines.Add($"{name} is back up after {FormatDuration(notification.OccurredAt - downSince)}.");
                    lines.Add(string.Empty);
                    lines.Add($"Down from: {FormatTime(downSince)}");
                    lines.Add($"Back up:   {FormatTime(notification.OccurredAt)}");
                }
                else
                {
                    lines.Add($"{name} is back up.");
                    lines.Add(string.Empty);
                    lines.Add($"Back up: {FormatTime(notification.OccurredAt)}");
                }

                AddLink(lines, probeLink);
                break;

            case AlertKind.Test:
                subject = "[Homon] Test alert";
                lines.Add("This is a test alert from Homon. If you can read it, alert email reaches you.");
                lines.Add(string.Empty);
                lines.Add($"Sent to: {string.Join(", ", recipients)}");
                AddLink(lines, $"{baseUrl}/admin/alerts");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(notification), notification.Kind, "Unknown alert kind.");
        }

        return new EmailMessage(
            recipients,
            subject,
            string.Join("\n", lines),
            ToHtml(lines));
    }

    /// <summary>
    /// A downtime in words: "less than a minute", "5 min", "1 h 12 min", "1 d 1 h". Invariant
    /// culture. The SPA's <c>formatDuration</c> mirrors this table.
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return "less than a minute";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"{duration.Minutes.ToString(Invariant)} min";
        }

        if (duration < TimeSpan.FromDays(1))
        {
            var hours = (int)duration.TotalHours;
            return duration.Minutes == 0
                ? $"{hours.ToString(Invariant)} h"
                : $"{hours.ToString(Invariant)} h {duration.Minutes.ToString(Invariant)} min";
        }

        var days = (int)duration.TotalDays;
        return duration.Hours == 0
            ? $"{days.ToString(Invariant)} d"
            : $"{days.ToString(Invariant)} d {duration.Hours.ToString(Invariant)} h";
    }

    private static string FormatTime(DateTimeOffset when) =>
        when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", Invariant);

    private static string StripControl(string value) =>
        string.Concat(value.Where(c => !char.IsControl(c)));

    private static void AddLink(List<string> lines, string? link)
    {
        if (link is null)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(link);
    }

    private static string ToHtml(List<string> lines)
    {
        // The text lines are the single source of truth; the HTML is the same content, one
        // paragraph per blank-line-separated block, encoded value by value. A bare URL line
        // becomes a link.
        var html = new StringBuilder();
        var block = new List<string>();

        void Flush()
        {
            if (block.Count == 0)
            {
                return;
            }

            html.Append("<p>")
                .Append(string.Join("<br>", block.Select(ToHtmlLine)))
                .Append("</p>");
            block.Clear();
        }

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                Flush();
            }
            else
            {
                block.Add(line);
            }
        }

        Flush();
        return html.ToString();
    }

    private static string ToHtmlLine(string line)
    {
        var encoded = WebUtility.HtmlEncode(line);
        return line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? $"<a href=\"{encoded}\">{encoded}</a>"
                : encoded;
    }
}
