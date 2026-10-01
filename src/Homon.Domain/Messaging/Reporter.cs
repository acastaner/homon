namespace Homon.Domain.Messaging;

/// <summary>
/// A registered sender of messages: one script, cron job or agent somewhere on the network.
/// Created by the administrator, never by a caller — Homon generates the
/// <see cref="Identifier"/> and mints the one API key bound to it in the same transaction, so
/// a key is always paired with exactly one reporter and the key alone decides who is
/// reporting (plan 021, Decision 13).
/// </summary>
/// <remarks>
/// The reporter holds no schedule and no expectations of its own. Everything about a given
/// run — what it was called, what it did, when the next one is due — belongs to the
/// <see cref="Message"/>, because the reporter owns that vocabulary and Homon must not need
/// editing when a script changes what it calls itself.
/// </remarks>
public sealed class Reporter
{
    /// <summary>
    /// Length of the generated handle. Sixteen characters of Crockford base32, longer than an
    /// <c>ApiKey.TokenIdLength</c> token id because this one is quoted in shell scripts and
    /// never typed from a screenshot.
    /// </summary>
    public const int IdentifierLength = 16;

    /// <summary>The administrator's label for this reporter, shown on its own page.</summary>
    public const int NameMaxLength = 100;

    /// <summary>The administrator's own note about what this reporter is for.</summary>
    public const int DescriptionMaxLength = 280;

    public Guid Id { get; set; }

    /// <summary>
    /// The public handle a message may name. Generated once, unique, and never editable: a
    /// <c>message</c> probe stores it in <c>Probe.Host</c> (Decision 3), so renaming it would
    /// orphan the probe watching it.
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Normalize"/> of <see cref="Name"/>, kept unique so two reporters cannot differ
    /// only by case — <c>ProbeGroup</c>'s pattern. Load-bearing for the admin page as well as the
    /// database: a unique name is what lets every row control be named "Edit {name}" without a
    /// disambiguating suffix, which is what Playwright's strict mode needs with N rows.
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Who may read this reporter's message bodies. Defaults to
    /// <see cref="MessageBodyVisibility.Administrator"/>; the administrator opts a reporter in
    /// to the reader-facing payload deliberately, one at a time.
    /// </summary>
    public MessageBodyVisibility BodyVisibility { get; set; } = MessageBodyVisibility.Administrator;

    /// <summary>
    /// The key this reporter authenticates with, one to one (a unique index enforces it).
    /// Rotation repoints this at a freshly minted key and soft-revokes the old one, which
    /// stays in the table for the audit trail <c>ApiKey</c> already promises.
    /// </summary>
    public Guid ApiKeyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Message> Messages { get; } = [];

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
