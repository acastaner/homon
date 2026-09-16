using Microsoft.AspNetCore.DataProtection;

namespace Homon.Infrastructure.Security;

/// <summary>
/// <see cref="ISecretProtector"/> backed by ASP.NET Data Protection. One purpose string,
/// shared by every probe secret Homon ever stores (SMB password in 004, calendar
/// credentials in 011 included) — not one purpose per kind, so a key ring export/import
/// covers all of them together. See plan 003's Decision 2.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "Homon.Secrets.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
