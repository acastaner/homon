namespace Homon.Infrastructure.Email;

/// <summary>
/// One outbound message, transport-agnostic. The sender identity is not here: it comes from
/// <see cref="EmailOptions"/>, so a caller composes <i>what</i> is said and never <i>from whom</i>.
/// </summary>
/// <param name="To">Recipient addresses. At least one.</param>
/// <param name="Subject">Subject line, plain text.</param>
/// <param name="TextBody">Plain-text body. Always present, so a text-only client reads something.</param>
/// <param name="HtmlBody">Optional HTML alternative. Callers encode every interpolated value.</param>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string TextBody,
    string? HtmlBody = null);
