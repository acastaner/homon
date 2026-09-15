using System.Globalization;
using Homon.Domain.Auth;

namespace Homon.Api.Cli;

/// <summary>
/// The parsed grammar of <c>create-api-key --name &lt;name&gt; [--scope read|read-write]
/// [--expires &lt;YYYY-MM-DD&gt;]</c>.
/// </summary>
internal sealed record CreateApiKeyArguments(string Name, ApiKeyScope Scope, DateTimeOffset? ExpiresAt)
{
    private const string Usage =
        "Expected: create-api-key --name <name> [--scope read|read-write] [--expires <YYYY-MM-DD>].";

    /// <summary>
    /// Parses the full <c>args</c> array (verb included, at index 0), or a message ready to
    /// print to stderr.
    /// </summary>
    internal static (CreateApiKeyArguments? Parsed, string? Error) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? name = null;
        var scope = ApiKeyScope.Read;
        DateTimeOffset? expiresAt = null;

        var arguments = args[1..];

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];

            if (argument.StartsWith("--name=", StringComparison.Ordinal))
            {
                name = argument["--name=".Length..];
            }
            else if (argument == "--name" && index + 1 < arguments.Length)
            {
                name = arguments[++index];
            }
            else if (argument.StartsWith("--scope=", StringComparison.Ordinal))
            {
                if (!TryParseScope(argument["--scope=".Length..], out scope))
                {
                    return (null, $"'{argument}' is not a recognised scope. {Usage}");
                }
            }
            else if (argument == "--scope" && index + 1 < arguments.Length)
            {
                var value = arguments[++index];

                if (!TryParseScope(value, out scope))
                {
                    return (null, $"'{value}' is not a recognised scope. {Usage}");
                }
            }
            else if (argument.StartsWith("--expires=", StringComparison.Ordinal))
            {
                if (!TryParseExpiry(argument["--expires=".Length..], out expiresAt, out var error))
                {
                    return (null, $"{error} {Usage}");
                }
            }
            else if (argument == "--expires" && index + 1 < arguments.Length)
            {
                var value = arguments[++index];

                if (!TryParseExpiry(value, out expiresAt, out var error))
                {
                    return (null, $"{error} {Usage}");
                }
            }
            else
            {
                return (null, $"'{argument}' is not a create-api-key option. {Usage}");
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, $"A key needs a name — what it is for, in your words. {Usage}");
        }

        return (new CreateApiKeyArguments(name.Trim(), scope, expiresAt), null);
    }

    /// <summary>Exact match on "read"/"read-write", nothing else.</summary>
    private static bool TryParseScope(string value, out ApiKeyScope scope)
    {
        switch (value)
        {
            case "read":
                scope = ApiKeyScope.Read;
                return true;
            case "read-write":
                scope = ApiKeyScope.ReadWrite;
                return true;
            default:
                scope = ApiKeyScope.Read;
                return false;
        }
    }

    /// <summary>
    /// Parses a calendar date, valid through the whole of that day, UTC — read like a
    /// certificate's "valid until", not a timestamp the operator reasons about a time zone
    /// for. A date already past (UTC) is refused at parse time. Reads
    /// <see cref="DateTimeOffset.UtcNow"/> directly rather than taking a
    /// <see cref="TimeProvider"/>: a pure static parser with no DI; <c>ApiKeyIssuer</c>'s own
    /// <see cref="TimeProvider"/> check is the one that actually matters, this one exists only
    /// to fail fast before a database round trip.
    /// </summary>
    private static bool TryParseExpiry(string value, out DateTimeOffset? expiresAt, out string? error)
    {
        if (!DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            expiresAt = null;
            error = $"'{value}' is not a date in YYYY-MM-DD form.";
            return false;
        }

        var endOfDay = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        if (endOfDay <= DateTimeOffset.UtcNow)
        {
            expiresAt = null;
            error = $"'{value}' has already passed.";
            return false;
        }

        expiresAt = endOfDay;
        error = null;
        return true;
    }
}
