using System.Security.Cryptography;

namespace Homon.Domain.Messaging;

/// <summary>
/// Generates a reporter's public handle. Deliberately the same shape as an API key's token id
/// — Crockford's base32, which drops I, L, O and U so a handle read aloud or copied out of a
/// screenshot cannot be mistyped — so the two handles look like siblings in a script's
/// configuration.
/// </summary>
/// <remarks>
/// The alphabet is repeated here rather than shared with <c>Homon.Api.ApiKeyRules</c>: this
/// project has no dependencies by design, and that type is internal to the API. A handle is a
/// public name, not a secret, but it is drawn from <see cref="RandomNumberGenerator"/> anyway
/// so that guessing one tells an attacker nothing about the next.
/// </remarks>
public static class ReporterIdentifier
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A fresh handle of <see cref="Reporter.IdentifierLength"/> characters.</summary>
    public static string New()
    {
        var characters = new char[Reporter.IdentifierLength];

        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }
}
