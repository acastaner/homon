namespace Homon.Domain.Monitoring;

/// <summary>
/// The HTTP methods a probe may use. HEAD and GET only this phase — the brief's "…" reads as
/// extensible, but nothing in the brief names a third method, and HEAD+GET already cover
/// "does this service answer" without ever risking a mutating request against an
/// admin-configured host.
/// </summary>
public enum HttpProbeMethod
{
    Head,
    Get,
}

/// <summary>
/// Per-probe options for <see cref="ProbeKind.Http"/>, owned by <see cref="Probe.HttpOptions"/>
/// and stored as one jsonb document (plan 003's Decision 1). Null for every other kind.
/// </summary>
public sealed class HttpProbeOptions
{
    /// <summary>What a newly created HTTP probe gets when its request omits the field.</summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>Smallest <see cref="TimeoutSeconds"/> allowed.</summary>
    public const int MinTimeoutSeconds = 1;

    /// <summary>
    /// Largest <see cref="TimeoutSeconds"/> allowed — 5 seconds under
    /// <c>MonitoringOptions.PerPollTimeout</c>'s 30-second default, so this runner's own,
    /// more specific timeout detail always fires before the scheduler's generic outer one.
    /// See plan 003's Decision 4a; re-check this bound if that default ever changes.
    /// </summary>
    public const int MaxTimeoutSeconds = 25;

    public HttpProbeMethod Method { get; set; } = HttpProbeMethod.Get;

    /// <summary>No leading slash, e.g. <c>"api/health"</c> — trimmed if given with one.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary><see langword="true"/> for <c>https://</c>, <see langword="false"/> for <c>http://</c>.</summary>
    public bool UseHttps { get; set; }

    /// <summary>
    /// Skips TLS certificate validation for this probe's own requests, to a host the admin
    /// already chose — self-signed certs are common on home NAS/media servers. Default
    /// <see langword="false"/>. Does not widen what the admin's network access already permits.
    /// </summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>
    /// This probe's own request timeout, in seconds — not a runner constant (plan 003's
    /// Decision 4a). Validated <see cref="MinTimeoutSeconds"/>–<see cref="MaxTimeoutSeconds"/>
    /// inclusive at the API layer.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Unset means "only a 2xx response counts as success." Set (possibly negated) replaces
    /// that rule entirely for this probe.
    /// </summary>
    public int? ExpectedStatusCode { get; set; }

    public bool ExpectedStatusCodeNegate { get; set; }

    /// <summary>
    /// Evaluated in addition to whichever status rule applies, only once the status check
    /// passes. Rejected at the API layer when <see cref="Method"/> is <see cref="HttpProbeMethod.Head"/>
    /// — a HEAD response has no body.
    /// </summary>
    public string? ExpectedBodyText { get; set; }

    public bool ExpectedBodyTextNegate { get; set; }

    public HttpCredential Credential { get; set; } = new();
}
