using Homon.Api.Cli;

namespace Homon.Api.Tests;

public class CliArgumentsTests
{
    [Theory]
    [InlineData("create-api-key", "--name", "clockmaster restic")]
    [InlineData("create-api-key", "--name=clockmaster restic")]
    public void Create_api_key_takes_a_name_in_either_spelling(params string[] args)
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(args);

        Assert.Null(error);
        Assert.Equal("clockmaster restic", parsed!.Name);
    }

    [Fact]
    public void Create_api_key_without_a_name_is_refused()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(["create-api-key"]);

        Assert.Null(parsed);
        Assert.Contains("needs a name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_api_key_refuses_an_unknown_option()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(["create-api-key", "--bogus"]);

        Assert.Null(parsed);
        Assert.Contains("'--bogus' is not a create-api-key option", error, StringComparison.Ordinal);
    }
}
