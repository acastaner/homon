using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// Administration of the API keys themselves: what exists, minting one, and revoking one. Carried
/// over from plan 008, which this module supersedes — until now the only minter was the
/// <c>create-api-key</c> verb, which still exists because a fresh install needs a key before
/// anybody can sign in.
/// </summary>
/// <remarks>
/// Administrator-only, including the reads: §3.3's "a key never administers" is the whole point,
/// and an API key that could list or revoke keys would be exactly that. A key is never deleted,
/// only revoked — the row is the audit trail.
/// </remarks>
internal static class ApiKeyEndpoints
{
    internal static RouteGroupBuilder MapApiKeyEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/api-keys").RequireAuthorization(HomonPolicies.Administrator);

        group.MapGet("", GetApiKeysAsync)
            .WithName("GetApiKeys")
            .WithSummary("Lists every API key, revoked ones included, newest first.");

        group.MapPost("", CreateApiKeyAsync)
            .WithName("CreateApiKey")
            .WithSummary("Mints an API key and returns it once.");

        group.MapDelete("/{id:guid}", RevokeApiKeyAsync)
            .WithName("RevokeApiKey")
            .WithSummary("Revokes an API key.");

        return group;
    }

    private static async Task<Ok<ApiKeyResponse[]>> GetApiKeysAsync(
        HomonDbContext database, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var keys = await database.ApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ThenBy(k => k.Id)
            .ToListAsync(cancellationToken);

        // Which keys belong to a reporter, so the page can warn before an administrator revokes a
        // credential a script is still using — and so this page is not a way around the reporter
        // page's own refusal.
        var reporterNames = await database.Reporters
            .Select(r => new { r.ApiKeyId, r.Name })
            .ToDictionaryAsync(x => x.ApiKeyId, x => x.Name, cancellationToken);

        return TypedResults.Ok(keys
            .Select(k => new ApiKeyResponse(
                k.Id,
                k.Name,
                k.TokenId,
                k.Scope,
                k.CreatedAt,
                k.LastUsedAt,
                k.ExpiresAt,
                k.RevokedAt,
                // Computed, never stored: an expiry that has passed is a fact about the clock, and
                // storing it would mean a job to maintain it.
                k.ExpiresAt is { } expiry && expiry <= now,
                reporterNames.GetValueOrDefault(k.Id)))
            .ToArray());
    }

    private static async Task<Results<Created<ApiKeyCreatedResponse>, ValidationProblem>> CreateApiKeyAsync(
        ApiKeyRequest request,
        ApiKeyIssuer issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > ApiKey.NameMaxLength)
        {
            errors["name"] = [$"Name is required and at most {ApiKey.NameMaxLength} characters."];
        }

        var scope = ApiKeyScope.Read;
        if (request.Scope is { } rawScope && !string.IsNullOrWhiteSpace(rawScope)
            && !Enum.TryParse(rawScope.Trim(), ignoreCase: true, out scope))
        {
            errors["scope"] = ["Scope must be 'read' or 'readWrite'."];
        }

        var expiresAt = request.ExpiresAt;
        if (expiresAt is { } expiry && expiry <= timeProvider.GetUtcNow())
        {
            errors["expiresAt"] = ["An expiry must be in the future."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (key, presented) = await issuer.IssueAsync(name, scope, expiresAt, cancellationToken);

        return TypedResults.Created(
            $"/api/v1/api-keys/{key.Id}",
            new ApiKeyCreatedResponse(key.Id, key.Name, key.TokenId, key.Scope, key.CreatedAt, key.ExpiresAt, presented));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, NotFound>> RevokeApiKeyAsync(
        Guid id,
        HomonDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var key = await database.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

        if (key is null)
        {
            return TypedResults.NotFound();
        }

        var reporter = await database.Reporters
            .FirstOrDefaultAsync(r => r.ApiKeyId == id, cancellationToken);

        if (reporter is not null)
        {
            // Revoking a reporter's own key here would leave it holding a dead credential with
            // nothing on its page explaining why its reports stopped. Rotation and deletion both
            // live on the reporter, where the consequence is visible.
            return TypedResults.Problem(
                title: "Key belongs to a reporter",
                detail: $"This key is paired with the reporter \"{reporter.Name}\". Replace that reporter's key, or delete the reporter, instead.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Idempotent: revoking an already-revoked key keeps the first revocation's timestamp,
        // because that is when it actually stopped working.
        await database.ApiKeys
            .Where(k => k.Id == id && k.RevokedAt == null)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);

        return TypedResults.NoContent();
    }

    /// <param name="Name">What the key is for, as the administrator will read it in the list.</param>
    /// <param name="Scope"><c>read</c> or <c>readWrite</c>. Omitted means <c>read</c> — least privilege.</param>
    /// <param name="ExpiresAt">An optional expiry. Omitted means the key does not expire.</param>
    internal sealed record ApiKeyRequest(string? Name, string? Scope, DateTimeOffset? ExpiresAt);

    /// <summary>
    /// A key as the administrator's page sees it. Never the secret: <c>TokenId</c> is the public
    /// half. <c>IsExpired</c> is computed from the clock, and <c>ReporterName</c> is set when this
    /// key is a reporter's paired credential.
    /// </summary>
    public sealed record ApiKeyResponse(
        Guid Id,
        string Name,
        string TokenId,
        ApiKeyScope Scope,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastUsedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt,
        bool IsExpired,
        string? ReporterName);

    /// <summary>The one response that carries a usable key. It is not recoverable afterwards.</summary>
    public sealed record ApiKeyCreatedResponse(
        Guid Id,
        string Name,
        string TokenId,
        ApiKeyScope Scope,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt,
        string Token);
}
