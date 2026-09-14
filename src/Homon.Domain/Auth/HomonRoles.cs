namespace Homon.Domain.Auth;

/// <summary>The roles Homon knows. Authorisation is role membership, not a ladder.</summary>
public static class HomonRoles
{
    /// <summary>
    /// Edits everything: probes, links, pages, API keys. The bootstrap administrator from
    /// configuration holds this role without a database row; a database-backed account
    /// holds it through Identity's role table.
    /// </summary>
    public const string Administrator = "Administrator";
}
