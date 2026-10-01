namespace Homon.Domain.Messaging;

/// <summary>
/// Who may read a reporter's message bodies. Defaults to <see cref="Administrator"/> for
/// every new reporter, because the body is whatever a script piped into it — a restic log
/// carries repository paths, hostnames and snapshot ids that the phone-glancing reader on
/// the trusted network has no business seeing (<c>docs/ARCHITECTURE.md</c> §3.3).
/// </summary>
public enum MessageBodyVisibility
{
    /// <summary>Only a signed-in administrator sees the body, on the reporter's own page.</summary>
    Administrator,

    /// <summary>
    /// The latest body also travels on the reader-facing status payload, capped — for a
    /// reporter whose message is a one-line summary rather than a log.
    /// </summary>
    Reader,
}
