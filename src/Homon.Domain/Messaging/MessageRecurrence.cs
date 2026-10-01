using System.Globalization;
using System.Xml;

namespace Homon.Domain.Messaging;

/// <summary>
/// Turns what a reporter says about its next message into one absolute deadline. A reporter
/// may send an ISO 8601 duration (<c>PT25H</c>, <c>P1D</c>, <c>P5Y</c>) or an absolute instant,
/// never both — plan 021's Decision 8. The duration form is what a shell script can write
/// without doing date arithmetic; the instant form is for a reporter that needs a calendar it
/// can trust.
/// </summary>
/// <remarks>
/// Durations are parsed by <see cref="XmlConvert.ToTimeSpan(string)"/>, which counts a month as
/// 30 days and a year as 365. That is a documented approximation, not a bug: every error
/// message and <c>docs/message-reporting.md</c> say so, and point a reporter that needs
/// calendar-exact timing at the instant form.
/// </remarks>
public static class MessageRecurrence
{
    /// <summary>
    /// How far ahead a reporter may promise, in days — ten years, counted flat. Long enough for
    /// "this certificate is checked annually" and short enough that a typo in a duration
    /// (<c>P25H</c>, which means 25 <em>days</em>) does not silently become a deadline nobody
    /// alive will see pass.
    /// </summary>
    public const int MaxHorizonDays = 3_653;

    /// <summary>
    /// Resolves the pair into a deadline, or into the one field and sentence that should be
    /// reported back. Both arguments absent is valid and means the reporter promised nothing.
    /// </summary>
    /// <param name="recurrence">An ISO 8601 duration, or null.</param>
    /// <param name="expectNextBy">An absolute instant, or null.</param>
    /// <param name="receivedAt">When the message arrived, which a duration is measured from.</param>
    public static MessageRecurrenceResult Resolve(
        string? recurrence,
        DateTimeOffset? expectNextBy,
        DateTimeOffset receivedAt)
    {
        var declared = string.IsNullOrWhiteSpace(recurrence) ? null : recurrence.Trim();

        if (declared is not null && expectNextBy is not null)
        {
            return MessageRecurrenceResult.Invalid(
                "recurrence",
                "Send either 'recurrence' or 'expectNextBy', not both.");
        }

        if (declared is null && expectNextBy is null)
        {
            return MessageRecurrenceResult.None;
        }

        var horizon = TimeSpan.FromDays(MaxHorizonDays);

        if (declared is not null)
        {
            if (declared.Length > Message.DeclarationMaxLength)
            {
                return MessageRecurrenceResult.Invalid(
                    "recurrence",
                    $"Recurrence must be at most {Message.DeclarationMaxLength} characters.");
            }

            TimeSpan interval;

            try
            {
                interval = XmlConvert.ToTimeSpan(declared);
            }
            catch (FormatException)
            {
                return MessageRecurrenceResult.Invalid("recurrence", MalformedDuration);
            }
            catch (OverflowException)
            {
                return MessageRecurrenceResult.Invalid("recurrence", MalformedDuration);
            }

            if (interval <= TimeSpan.Zero)
            {
                return MessageRecurrenceResult.Invalid(
                    "recurrence",
                    "Recurrence must be a positive duration — a reporter cannot promise a message in the past.");
            }

            if (interval > horizon)
            {
                return MessageRecurrenceResult.Invalid("recurrence", TooFar);
            }

            return MessageRecurrenceResult.Resolved(receivedAt + interval, declared);
        }

        var deadline = expectNextBy!.Value;

        if (deadline <= receivedAt)
        {
            return MessageRecurrenceResult.Invalid(
                "expectNextBy",
                "The next message must be expected in the future.");
        }

        if (deadline - receivedAt > horizon)
        {
            return MessageRecurrenceResult.Invalid("expectNextBy", TooFar);
        }

        return MessageRecurrenceResult.Resolved(
            deadline,
            deadline.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture));
    }

    private const string MalformedDuration =
        "Recurrence must be an ISO 8601 duration such as 'PT25H', 'P1D' or 'P5Y', "
        + "where a month counts as 30 days and a year as 365. Send 'expectNextBy' instead if you need an exact date.";

    private static readonly string TooFar =
        $"The next message must be expected within {MaxHorizonDays} days.";
}

/// <summary>
/// What <see cref="MessageRecurrence.Resolve"/> worked out: a deadline and the declaration to
/// keep, or the wire field and sentence to refuse it with.
/// </summary>
/// <param name="NextExpectedAt">The deadline, or null when nothing was promised.</param>
/// <param name="Declaration">What to store for display, or null.</param>
/// <param name="ErrorField">The camelCase wire field at fault, or null when valid.</param>
/// <param name="ErrorMessage">One full sentence for the caller, or null when valid.</param>
public sealed record MessageRecurrenceResult(
    DateTimeOffset? NextExpectedAt,
    string? Declaration,
    string? ErrorField,
    string? ErrorMessage)
{
    /// <summary>The reporter promised nothing, which is allowed.</summary>
    public static readonly MessageRecurrenceResult None = new(null, null, null, null);

    public bool IsValid => ErrorField is null;

    internal static MessageRecurrenceResult Resolved(DateTimeOffset nextExpectedAt, string declaration) =>
        new(nextExpectedAt, declaration, null, null);

    internal static MessageRecurrenceResult Invalid(string field, string message) =>
        new(null, null, field, message);
}
