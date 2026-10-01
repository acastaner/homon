namespace Homon.Domain.Messaging;

/// <summary>
/// One report, exactly as a <see cref="Reporter"/> filed it, plus the deadline Homon derived
/// from it. Append-only: a correction is a second message, never an edit, and the newest row
/// for a reporter is the only one its probe reads.
/// </summary>
/// <remarks>
/// Nothing here is Homon's judgement. <see cref="Status"/> is the reporter's own verdict and
/// <see cref="NextExpectedAt"/> is the reporter's own promise — the whole module is an
/// ingestion point, and <see cref="MessageProbeEvaluator"/> is the only thing that turns these
/// fields into a probe's state.
/// </remarks>
public sealed class Message
{
    /// <summary>What the reporter calls itself. Shown on the administrator's reporter page.</summary>
    public const int NameMaxLength = 100;

    /// <summary>A sentence about this message or this reporter, from the reporter.</summary>
    public const int DescriptionMaxLength = 280;

    /// <summary>
    /// Cap on the body, in UTF-8 bytes. A longer body is truncated to its last 64 KiB and
    /// never rejected (plan 021, Decision 10): a truncated proof-of-run beats a failed report
    /// at 02:00, and Kestrel's 30 MB request limit is the real backstop.
    /// </summary>
    public const int BodyMaxBytes = 65_536;

    /// <summary>Longest category slug. See <see cref="CategoryPattern"/>.</summary>
    public const int CategoryMaxLength = 40;

    /// <summary>
    /// Longest recurrence declaration kept for display — an ISO 8601 duration or instant as
    /// the reporter wrote it, so the administrator can see what was actually sent.
    /// </summary>
    public const int DeclarationMaxLength = 40;

    /// <summary>Lower-case kebab-case, the same shape as a page slug.</summary>
    public const string CategoryPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    /// <summary>
    /// What a message with no category is filed under. The vocabulary is open on purpose: a
    /// new category is data, not a migration (plan 021, Decision 9).
    /// </summary>
    public const string DefaultCategory = "other";

    /// <summary>
    /// A <c>long</c> identity column, not a <c>Guid</c>, for the reason
    /// <c>ProbeObservation.Id</c> gives: an append-only, time-ordered table gains nothing from
    /// a random-order key and pays for it in index bloat.
    /// </summary>
    public long Id { get; set; }

    public Guid ReporterId { get; set; }

    /// <summary>
    /// When Homon accepted the message, from <c>TimeProvider</c> — never a clock reading sent
    /// by the caller, so there is no drift to validate and no replay to detect.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The free text the reporter attached — a command's output, typically. Unbounded in the
    /// column, capped at <see cref="BodyMaxBytes"/> on the way in. Reader-visible only if the
    /// reporter's <see cref="Reporter.BodyVisibility"/> says so.
    /// </summary>
    public string? Body { get; set; }

    public MessageStatus Status { get; set; }

    /// <summary>A validated slug, <see cref="DefaultCategory"/> when the reporter sent none.</summary>
    public string Category { get; set; } = DefaultCategory;

    /// <summary>
    /// What the reporter said about the next message, verbatim, for display only. Null when it
    /// promised nothing.
    /// </summary>
    public string? RecurrenceDeclaration { get; set; }

    /// <summary>
    /// The deadline derived from the declaration. Null means this reporter can never be
    /// overdue — only its own <see cref="MessageStatus"/> can take it off green — which is why
    /// the administrator's page has to say so on the row.
    /// </summary>
    public DateTimeOffset? NextExpectedAt { get; set; }

    /// <summary>
    /// Which key filed it. Kept after a rotation so the history stays attributable, which is
    /// why the foreign key restricts rather than cascades.
    /// </summary>
    public Guid ReportedByKeyId { get; set; }
}
