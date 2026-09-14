using System.ComponentModel.DataAnnotations;

namespace Homon.Infrastructure.Administration;

/// <summary>
/// Credentials for the bootstrap administrator, supplied entirely from configuration.
/// </summary>
/// <remarks>
/// <para>
/// This account has no database row by design: if the database is exfiltrated, it yields
/// nothing that can sign in as administrator. Only a password <i>hash</i> is held, so a
/// leaked environment does not hand over a usable password either. Mint one with:
/// <code>dotnet run --project src/Homon.Api -- hash-password</code>
/// </para>
/// <para>
/// Both values are optional, deliberately. A fresh installation boots and serves the
/// dashboard with no administrator at all — <c>GET /api/v1/meta</c> says so, and sign-in
/// refuses everyone — because a self-hoster's first run should show them the application
/// before it asks them for secrets. Set both to enable sign-in.
/// </para>
/// </remarks>
public sealed class AdministratorOptions
{
    public const string SectionName = "Administrator";

    /// <summary>Sign-in address for the bootstrap administrator.</summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>ASP.NET Core Identity password hash. Never the password itself.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>True when both halves are present, which is when sign-in is possible.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(PasswordHash);
}
