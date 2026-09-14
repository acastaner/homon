using Homon.Api.Authentication;
using Homon.Domain.Auth;

namespace Homon.Api.Tests;

public class ApiKeyRulesTests
{
    [Fact]
    public void A_minted_key_round_trips_through_parse_and_matches_its_own_hash()
    {
        var (tokenId, presented, hash) = ApiKeyRules.Mint();

        Assert.Equal(ApiKey.TokenIdLength, tokenId.Length);
        Assert.StartsWith(ApiKeyRules.Prefix, presented, StringComparison.Ordinal);
        Assert.True(ApiKeyRules.TryParse(presented, out var parsedId, out var secret));
        Assert.Equal(tokenId, parsedId);
        Assert.True(ApiKeyRules.Matches(hash, secret));
        Assert.False(ApiKeyRules.Matches(hash, secret + "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hmn_")]
    [InlineData("hmn_TOOSHORT_secret")]
    [InlineData("hmn_ABCDEFGH1234")]
    [InlineData("hmn_ABCDEFGH1234_")]
    [InlineData("hmn_abcdefgh1234_secret")]
    [InlineData("chr_ABCDEFGH1234_secret")]
    public void Anything_not_shaped_like_a_key_does_not_parse(string? presented)
    {
        Assert.False(ApiKeyRules.TryParse(presented, out _, out _));
    }

    [Fact]
    public void Two_mints_never_collide()
    {
        var first = ApiKeyRules.Mint();
        var second = ApiKeyRules.Mint();

        Assert.NotEqual(first.TokenId, second.TokenId);
        Assert.NotEqual(first.Presented, second.Presented);
    }
}
