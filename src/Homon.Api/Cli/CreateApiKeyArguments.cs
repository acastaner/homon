namespace Homon.Api.Cli;

/// <summary>The parsed grammar of <c>create-api-key --name &lt;name&gt;</c>.</summary>
internal sealed record CreateApiKeyArguments(string Name)
{
    private const string Usage = "Expected: create-api-key --name <name>.";

    /// <summary>
    /// Parses the full <c>args</c> array (verb included, at index 0), or a message ready to
    /// print to stderr.
    /// </summary>
    internal static (CreateApiKeyArguments? Parsed, string? Error) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? name = null;

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
            else
            {
                return (null, $"'{argument}' is not a create-api-key option. {Usage}");
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, $"A key needs a name — what it is for, in your words. {Usage}");
        }

        return (new CreateApiKeyArguments(name.Trim()), null);
    }
}
