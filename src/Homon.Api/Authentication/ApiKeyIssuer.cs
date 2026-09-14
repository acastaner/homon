using Homon.Domain.Auth;
using Homon.Infrastructure.Persistence;

namespace Homon.Api.Authentication;

/// <summary>
/// Mints an API key and stores its digest. Used by the <c>create-api-key</c> verb today and
/// by the administrator's key page once the Backups module lands, so both produce the same
/// credential.
/// </summary>
internal sealed class ApiKeyIssuer(HomonDbContext database)
{
    /// <summary>
    /// Creates the key and returns the row alongside the one-time credential. The credential
    /// is not stored anywhere and cannot be recovered; the caller shows it once.
    /// </summary>
    internal async Task<(ApiKey Key, string Presented)> IssueAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        if (trimmed.Length is 0 || trimmed.Length > ApiKey.NameMaxLength)
        {
            throw new ArgumentException(
                $"A key name is 1 to {ApiKey.NameMaxLength} characters.", nameof(name));
        }

        var (tokenId, presented, secretHash) = ApiKeyRules.Mint();

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            TokenId = tokenId,
            SecretHash = secretHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        database.ApiKeys.Add(key);
        await database.SaveChangesAsync(cancellationToken);

        return (key, presented);
    }
}
