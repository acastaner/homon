using Homon.Api.Cli;
using Homon.Domain.Auth;

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

    [Fact]
    public void Scope_and_expiry_default_when_both_flags_are_omitted()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(["create-api-key", "--name", "clockmaster restic"]);

        Assert.Null(error);
        Assert.Equal(ApiKeyScope.Read, parsed!.Scope);
        Assert.Null(parsed.ExpiresAt);
    }

    [Fact]
    public void Scope_read_write_parses()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(
            ["create-api-key", "--name", "clockmaster restic", "--scope", "read-write"]);

        Assert.Null(error);
        Assert.Equal(ApiKeyScope.ReadWrite, parsed!.Scope);
    }

    [Fact]
    public void An_unrecognised_scope_is_refused()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(
            ["create-api-key", "--name", "clockmaster restic", "--scope", "bogus"]);

        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void Expires_parses_to_that_dates_utc_end_of_day()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(
            ["create-api-key", "--name", "clockmaster restic", "--expires", "2099-01-01"]);

        var expected = new DateTimeOffset(
            new DateOnly(2099, 1, 1).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        Assert.Null(error);
        Assert.Equal(expected, parsed!.ExpiresAt);
    }

    [Fact]
    public void A_malformed_expiry_date_is_refused()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(
            ["create-api-key", "--name", "clockmaster restic", "--expires", "2099-1-1"]);

        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void An_already_past_expiry_date_is_refused()
    {
        var (parsed, error) = CreateApiKeyArguments.Parse(
            ["create-api-key", "--name", "clockmaster restic", "--expires", "2000-01-01"]);

        Assert.Null(parsed);
        Assert.Contains("already passed", error, StringComparison.Ordinal);
    }
}
