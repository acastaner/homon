using Microsoft.AspNetCore.Identity;

namespace Homon.Infrastructure.Identity;

/// <summary>
/// A database-backed account. Phase 0 creates none — the only administrator is the one
/// configuration supplies, which has no row — but the table exists so that adding a second
/// administrator later is a feature, not a migration of the auth model.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>What to call this person in the UI; falls back to the address.</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}
