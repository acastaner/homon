using Homon.Api.Authentication;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// Admin CRUD, pause and reorder for <see cref="Probe"/>, under <c>/probes</c>.
/// </summary>
/// <remarks>
/// <c>GET</c> admits an Administrator session or a valid, unexpired API key of either scope
/// (<see cref="HomonPolicies.AdministratorOrApiKey"/>) — <c>/probes</c> carries <c>Host</c>,
/// an internal hostname or LAN IP not fit for an anonymous reader, and the maintainer wants a
/// household script able to list probes without a session. Every write stays
/// <see cref="HomonPolicies.Administrator"/>, session only — see docs/ARCHITECTURE.md §3.3
/// and plan 002's Decision 8.
/// </remarks>
internal static class ProbeEndpoints
{
    internal static RouteGroupBuilder MapProbeEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/probes");

        group.MapGet("", GetProbesAsync)
            .RequireAuthorization(HomonPolicies.AdministratorOrApiKey)
            .WithName("GetProbes")
            .WithSummary("Lists every probe, in display order.");

        group.MapGet("/{id:guid}", GetProbeAsync)
            .RequireAuthorization(HomonPolicies.AdministratorOrApiKey)
            .WithName("GetProbe")
            .WithSummary("Reads one probe.");

        group.MapPost("", CreateProbeAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("CreateProbe")
            .WithSummary("Creates a probe, appended at the end of the display order.");

        group.MapPut("/{id:guid}", UpdateProbeAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("UpdateProbe")
            .WithSummary("Replaces a probe's configurable fields. Kind cannot change.");

        group.MapPut("/{id:guid}/pause", SetPausedAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("SetProbePaused")
            .WithSummary("Pauses or unpauses a probe.");

        group.MapDelete("/{id:guid}", DeleteProbeAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("DeleteProbe")
            .WithSummary("Deletes a probe and its observations.");

        group.MapPut("/order", ReorderProbesAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("ReorderProbes")
            .WithSummary("Sets the display order for every probe.");

        return group;
    }

    private static async Task<Ok<ProbeResponse[]>> GetProbesAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var probes = await database.Probes
            .OrderBy(p => p.Position)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var groupIdsByProbe = await GroupIdsByProbeAsync(database, cancellationToken);

        return TypedResults.Ok(probes
            .Select(p => ToResponse(p, groupIdsByProbe.GetValueOrDefault(p.Id, [])))
            .ToArray());
    }

    private static async Task<Results<Ok<ProbeResponse>, NotFound>> GetProbeAsync(
        Guid id, HomonDbContext database, CancellationToken cancellationToken)
    {
        var probe = await database.Probes.FindAsync([id], cancellationToken);

        if (probe is null)
        {
            return TypedResults.NotFound();
        }

        var groupIds = await database.Set<ProbeGroupMembership>()
            .Where(m => m.ProbeId == id)
            .Select(m => m.GroupId)
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(probe, groupIds));
    }

    private static async Task<Results<Created<ProbeResponse>, ValidationProblem>> CreateProbeAsync(
        ProbeRequest request, HomonDbContext database, ISecretProtector secretProtector, CancellationToken cancellationToken)
    {
        var fieldsError = ValidateFields(request, requireFailureThreshold: false, out var name, out var host,
            out var pollIntervalSeconds, out var failureThreshold);
        if (fieldsError is not null)
        {
            return fieldsError;
        }

        var kindError = ValidateKind(request.Kind, out var kind);
        if (kindError is not null)
        {
            return kindError;
        }

        var httpOptionsError = ValidateHttpOptions(
            secretProtector, kind, request.Http, existing: null, out var httpOptions);
        if (httpOptionsError is not null)
        {
            return httpOptionsError;
        }

        var groupIds = (request.GroupIds ?? []).Distinct().ToArray();
        var groupsError = await ValidateGroupIdsAsync(database, groupIds, cancellationToken);
        if (groupsError is not null)
        {
            return groupsError;
        }

        var maxPosition = await database.Probes
            .Select(p => (int?)p.Position)
            .MaxAsync(cancellationToken);

        var probe = new Probe
        {
            Id = Guid.NewGuid(),
            Name = name,
            Host = host,
            Kind = kind,
            PollInterval = TimeSpan.FromSeconds(pollIntervalSeconds),
            FailureThreshold = failureThreshold,
            Position = (maxPosition ?? -1) + 1,
            Status = ProbeStatus.Unknown,
            HttpOptions = httpOptions,
        };

        database.Probes.Add(probe);
        // Saved before the group membership pass: memberships carry a foreign key to this
        // probe's row, which must exist first.
        await database.SaveChangesAsync(cancellationToken);

        await ApplyGroupsAsync(database, probe.Id, groupIds, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/probes/{probe.Id}", ToResponse(probe, groupIds));
    }

    private static async Task<Results<Ok<ProbeResponse>, ValidationProblem, NotFound>> UpdateProbeAsync(
        Guid id, ProbeRequest request, HomonDbContext database, ISecretProtector secretProtector,
        CancellationToken cancellationToken)
    {
        var probe = await database.Probes.FindAsync([id], cancellationToken);

        if (probe is null)
        {
            return TypedResults.NotFound();
        }

        var fieldsError = ValidateFields(request, requireFailureThreshold: true, out var name, out var host,
            out var pollIntervalSeconds, out var failureThreshold);
        if (fieldsError is not null)
        {
            return fieldsError;
        }

        // Kind is immutable — probe.Kind (not request.Kind, which PUT otherwise ignores)
        // governs whether the http object is required or forbidden here.
        var httpOptionsError = ValidateHttpOptions(
            secretProtector, probe.Kind, request.Http, probe.HttpOptions, out var httpOptions);
        if (httpOptionsError is not null)
        {
            return httpOptionsError;
        }

        var groupIds = (request.GroupIds ?? []).Distinct().ToArray();
        var groupsError = await ValidateGroupIdsAsync(database, groupIds, cancellationToken);
        if (groupsError is not null)
        {
            return groupsError;
        }

        probe.Name = name;
        probe.Host = host;
        probe.PollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);
        probe.ChangeFailureThreshold(failureThreshold);
        probe.HttpOptions = httpOptions;
        // Kind and Position are untouched — Kind is immutable after creation, Position is
        // governed only by ReorderProbesAsync.

        await database.SaveChangesAsync(cancellationToken);

        await ApplyGroupsAsync(database, probe.Id, groupIds, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(probe, groupIds));
    }

    private static async Task<Results<Ok<ProbeResponse>, NotFound>> SetPausedAsync(
        Guid id, PauseProbeRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var probe = await database.Probes.FindAsync([id], cancellationToken);

        if (probe is null)
        {
            return TypedResults.NotFound();
        }

        if (request.IsPaused)
        {
            probe.Pause();
        }
        else
        {
            probe.Unpause();
        }

        await database.SaveChangesAsync(cancellationToken);

        var groupIds = await database.Set<ProbeGroupMembership>()
            .Where(m => m.ProbeId == id)
            .Select(m => m.GroupId)
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(probe, groupIds));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteProbeAsync(
        Guid id, HomonDbContext database, CancellationToken cancellationToken)
    {
        var probe = await database.Probes.FindAsync([id], cancellationToken);

        if (probe is null)
        {
            return TypedResults.NotFound();
        }

        // Cascade deletes handle ProbeObservation (ProbeId FK) and ProbeGroupMembership
        // (ProbeId FK) rows — no manual cleanup here.
        database.Probes.Remove(probe);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> ReorderProbesAsync(
        ReorderProbesRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var probes = await database.Probes.ToListAsync(cancellationToken);
        var requested = request.ProbeIds ?? [];

        var permutationError = ValidatePermutation(requested, probes.Select(p => p.Id).ToArray(), "probeIds");
        if (permutationError is not null)
        {
            return permutationError;
        }

        for (var i = 0; i < requested.Length; i++)
        {
            probes.Single(p => p.Id == requested[i]).Position = i;
        }

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Updates every <see cref="ProbeGroup"/>'s membership of <paramref name="probeId"/> to
    /// match <paramref name="groupIds"/> exactly — shared by <c>POST</c> and <c>PUT</c>.
    /// <see cref="ProbeGroup.Include"/>/<see cref="ProbeGroup.Exclude"/> are both no-ops when
    /// the membership already matches, so positions inside groups the probe stays in are
    /// left untouched (plan 002's Decision 8).
    /// </summary>
    private static async Task ApplyGroupsAsync(
        HomonDbContext database, Guid probeId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        var groups = await database.ProbeGroups
            .Include(g => g.Members)
            .ToListAsync(cancellationToken);

        foreach (var group in groups)
        {
            if (groupIds.Contains(group.Id))
            {
                group.Include(probeId);
            }
            else
            {
                group.Exclude(probeId);
            }
        }
    }

    private static async Task<Dictionary<Guid, Guid[]>> GroupIdsByProbeAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var memberships = await database.Set<ProbeGroupMembership>()
            .Select(m => new { m.ProbeId, m.GroupId })
            .ToListAsync(cancellationToken);

        return memberships
            .GroupBy(m => m.ProbeId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.GroupId).ToArray());
    }

    private static ValidationProblem? ValidateFields(
        ProbeRequest request,
        bool requireFailureThreshold,
        out string name,
        out string host,
        out int pollIntervalSeconds,
        out int failureThreshold)
    {
        var errors = new Dictionary<string, string[]>();

        name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Probe.NameMaxLength)
        {
            errors["name"] = [$"Name is required and at most {Probe.NameMaxLength} characters."];
        }

        host = request.Host?.Trim() ?? string.Empty;
        if (host.Length is 0 or > Probe.HostMaxLength)
        {
            errors["host"] = [$"Host is required and at most {Probe.HostMaxLength} characters."];
        }

        if (request.PollIntervalSeconds is not { } interval
            || interval < Probe.MinPollIntervalSeconds
            || interval > Probe.MaxPollIntervalSeconds)
        {
            errors["pollIntervalSeconds"] =
                [$"Poll interval must be between {Probe.MinPollIntervalSeconds} and {Probe.MaxPollIntervalSeconds} seconds."];
        }

        pollIntervalSeconds = request.PollIntervalSeconds ?? Probe.MinPollIntervalSeconds;

        if (request.FailureThreshold is { } threshold)
        {
            if (threshold < Probe.MinFailureThreshold || threshold > Probe.MaxFailureThreshold)
            {
                errors["failureThreshold"] =
                    [$"Failure threshold must be between {Probe.MinFailureThreshold} and {Probe.MaxFailureThreshold}."];
            }

            failureThreshold = threshold;
        }
        else if (requireFailureThreshold)
        {
            errors["failureThreshold"] = ["Failure threshold is required."];
            failureThreshold = Probe.DefaultFailureThreshold;
        }
        else
        {
            // POST only: omitted or null defaults to Probe.DefaultFailureThreshold.
            failureThreshold = Probe.DefaultFailureThreshold;
        }

        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }

    private static ValidationProblem? ValidateKind(string? kind, out ProbeKind parsedKind)
    {
        var trimmed = kind?.Trim();

        if (string.Equals(trimmed, "ping", StringComparison.OrdinalIgnoreCase))
        {
            parsedKind = ProbeKind.Ping;
            return null;
        }

        if (string.Equals(trimmed, "http", StringComparison.OrdinalIgnoreCase))
        {
            parsedKind = ProbeKind.Http;
            return null;
        }

        parsedKind = default;

        // "smb"/"snmp" ship with plans 004/005, not this session — still rejected, but with
        // their own detail rather than the generic "must be" message.
        var detail = trimmed is "smb" or "snmp"
            ? $"Probe kind '{trimmed}' ships with a later plan and is not accepted yet."
            : "Probe kind must be 'ping' or 'http'.";

        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [detail] });
    }

    /// <summary>
    /// Validates <paramref name="request"/> against <paramref name="kind"/> and builds the
    /// <see cref="HttpProbeOptions"/> to store, or returns why it could not. <paramref
    /// name="existing"/> is the probe's current options (null on create, or when the probe is
    /// not <see cref="ProbeKind.Http"/>) — consulted only for the two "absent = keep" fields
    /// (<see cref="HttpProbeOptions.TimeoutSeconds"/> and the credential secret; plan 003's
    /// Decision 2 and 4a). Every other field is a full value on every write, the same
    /// convention <c>ValidateFields</c> already uses for the probe's own fields.
    /// </summary>
    private static ValidationProblem? ValidateHttpOptions(
        ISecretProtector secretProtector,
        ProbeKind kind,
        HttpProbeOptionsRequest? request,
        HttpProbeOptions? existing,
        out HttpProbeOptions? httpOptions)
    {
        httpOptions = null;

        if (kind != ProbeKind.Http)
        {
            return request is null
                ? null
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["http"] = ["The http object is only accepted when kind is 'http'."],
                });
        }

        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["http"] = ["The http object is required when kind is 'http'."],
            });
        }

        var errors = new Dictionary<string, string[]>();

        var method = ParseMethod(request.Method);
        if (method is null)
        {
            errors["http.method"] = ["Method must be 'head' or 'get'."];
        }

        var path = request.Path?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            errors["http.path"] = ["Path is required."];
        }
        else if (path.Contains("://", StringComparison.Ordinal))
        {
            errors["http.path"] = ["Path must not contain a scheme."];
        }

        var expectedBodyText = string.IsNullOrEmpty(request.ExpectedBodyText) ? null : request.ExpectedBodyText;
        if (method == HttpProbeMethod.Head && expectedBodyText is not null)
        {
            errors["http.expectedBodyText"] = ["A HEAD probe cannot check the response body — it has none."];
        }

        // Omitted keeps the probe's current value on update (null `existing` on create falls
        // through to the domain default) — the same "absent = keep" rule the secret uses.
        var timeoutSeconds = request.TimeoutSeconds ?? existing?.TimeoutSeconds ?? HttpProbeOptions.DefaultTimeoutSeconds;
        if (request.TimeoutSeconds is { } requestedTimeout
            && (requestedTimeout < HttpProbeOptions.MinTimeoutSeconds || requestedTimeout > HttpProbeOptions.MaxTimeoutSeconds))
        {
            errors["http.timeoutSeconds"] =
                [$"Timeout must be between {HttpProbeOptions.MinTimeoutSeconds} and {HttpProbeOptions.MaxTimeoutSeconds} seconds."];
        }

        HttpCredentialType credentialType = HttpCredentialType.None;
        if (request.Credential is not null)
        {
            var parsedCredentialType = ParseCredentialType(request.Credential.Type);
            if (parsedCredentialType is null)
            {
                errors["http.credential.type"] = ["Credential type must be 'none', 'bearer' or 'basic'."];
            }
            else
            {
                credentialType = parsedCredentialType.Value;
            }
        }

        var username = request.Credential?.Username?.Trim();
        if (credentialType == HttpCredentialType.Basic && string.IsNullOrEmpty(username))
        {
            errors["http.credential.username"] = ["Username is required for basic auth."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // credential.type == "none" clears any stored secret regardless of what else was
        // sent (plan 003's Decision 2's write-only wire semantics).
        var protectedSecret = credentialType == HttpCredentialType.None
            ? null
            : ResolveProtectedSecret(secretProtector, request.Credential?.Secret, existing?.Credential?.ProtectedSecret);

        httpOptions = new HttpProbeOptions
        {
            Method = method!.Value,
            Path = path.TrimStart('/'),
            UseHttps = request.UseHttps ?? false,
            IgnoreCertificateErrors = request.IgnoreCertificateErrors ?? false,
            TimeoutSeconds = timeoutSeconds,
            ExpectedStatusCode = request.ExpectedStatusCode,
            ExpectedStatusCodeNegate = request.ExpectedStatusCodeNegate ?? false,
            ExpectedBodyText = expectedBodyText,
            ExpectedBodyTextNegate = request.ExpectedBodyTextNegate ?? false,
            Credential = new HttpCredential
            {
                Type = credentialType,
                Username = credentialType == HttpCredentialType.Basic ? username : null,
                ProtectedSecret = protectedSecret,
            },
        };

        return null;
    }

    /// <summary>
    /// The write-only credential secret rule (plan 003's Decision 2): absent/null keeps the
    /// stored value, <c>""</c> clears it, non-empty is protected and replaces it.
    /// </summary>
    private static string? ResolveProtectedSecret(ISecretProtector secretProtector, string? secret, string? existingProtectedSecret)
    {
        if (secret is null)
        {
            return existingProtectedSecret;
        }

        return secret.Length == 0 ? null : secretProtector.Protect(secret);
    }

    private static HttpProbeMethod? ParseMethod(string? method)
    {
        var trimmed = method?.Trim();

        if (string.Equals(trimmed, "head", StringComparison.OrdinalIgnoreCase))
        {
            return HttpProbeMethod.Head;
        }

        return string.Equals(trimmed, "get", StringComparison.OrdinalIgnoreCase) ? HttpProbeMethod.Get : null;
    }

    private static HttpCredentialType? ParseCredentialType(string? type)
    {
        var trimmed = type?.Trim();

        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCredentialType.None;
        }

        if (string.Equals(trimmed, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            return HttpCredentialType.Bearer;
        }

        return string.Equals(trimmed, "basic", StringComparison.OrdinalIgnoreCase) ? HttpCredentialType.Basic : null;
    }

    private static async Task<ValidationProblem?> ValidateGroupIdsAsync(
        HomonDbContext database, Guid[] groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Length == 0)
        {
            return null;
        }

        var existing = await database.ProbeGroups
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        var unknown = groupIds.Except(existing).ToArray();

        return unknown.Length > 0
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["groupIds"] = [$"Unknown group id(s): {string.Join(", ", unknown)}."],
            })
            : null;
    }

    /// <summary>
    /// Checked by both <c>PUT /probes/order</c> and <c>PUT /probe-groups/order</c>/
    /// <c>PUT /probe-groups/{id}/members</c>: <paramref name="requested"/> must be an exact
    /// permutation of <paramref name="existing"/> — no missing, extra or duplicate ids.
    /// </summary>
    internal static ValidationProblem? ValidatePermutation(Guid[] requested, Guid[] existing, string fieldName)
    {
        var requestedSet = new HashSet<Guid>(requested);

        if (requestedSet.Count != requested.Length)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [fieldName] = ["The list contains a duplicate id."],
            });
        }

        if (!requestedSet.SetEquals(existing))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [fieldName] = ["The list must contain exactly the existing ids — none missing, none extra."],
            });
        }

        return null;
    }

    private static ProbeResponse ToResponse(Probe probe, Guid[] groupIds) =>
        new(
            probe.Id,
            probe.Name,
            probe.Host,
            probe.Kind,
            (int)probe.PollInterval.TotalSeconds,
            probe.FailureThreshold,
            probe.IsPaused,
            probe.Position,
            probe.Status,
            probe.LastDetail,
            probe.LastObservedAt,
            groupIds,
            ToHttpResponse(probe.HttpOptions));

    private static HttpProbeOptionsResponse? ToHttpResponse(HttpProbeOptions? options) =>
        options is null
            ? null
            : new HttpProbeOptionsResponse(
                options.Method,
                options.Path,
                options.UseHttps,
                options.IgnoreCertificateErrors,
                options.TimeoutSeconds,
                options.ExpectedStatusCode,
                options.ExpectedStatusCodeNegate,
                options.ExpectedBodyText,
                options.ExpectedBodyTextNegate,
                new HttpCredentialResponse(
                    options.Credential.Type,
                    options.Credential.Username,
                    // Never the value itself — ProtectedSecret is never read into a response
                    // DTO anywhere in this file. See plan 003's Decision 2.
                    options.Credential.ProtectedSecret is not null));

    /// <param name="Name">The probe's display name.</param>
    /// <param name="Host">A bare hostname or IP — no scheme, no path.</param>
    /// <param name="Kind">
    /// <c>"ping"</c> or <c>"http"</c>. Ignored on <c>PUT</c> — a probe's kind cannot change
    /// after creation.
    /// </param>
    /// <param name="PollIntervalSeconds">How often the probe is polled, in seconds.</param>
    /// <param name="FailureThreshold">
    /// Consecutive polls, in either direction, that flip the probe's status. Optional on
    /// <c>POST</c> (defaults to <see cref="Probe.DefaultFailureThreshold"/>), required on
    /// <c>PUT</c>.
    /// </param>
    /// <param name="GroupIds">Every <see cref="ProbeGroup"/> this probe should belong to.</param>
    /// <param name="Http">
    /// Required when <c>kind</c> (create) or the probe's stored kind (update) is <c>"http"</c>;
    /// must be absent otherwise.
    /// </param>
    internal sealed record ProbeRequest(
        string? Name,
        string? Host,
        string? Kind,
        int? PollIntervalSeconds,
        int? FailureThreshold,
        Guid[]? GroupIds,
        HttpProbeOptionsRequest? Http);

    /// <param name="Method"><c>"head"</c> or <c>"get"</c>.</param>
    /// <param name="Path">No leading slash, e.g. <c>"api/health"</c> — trimmed if given with one.</param>
    /// <param name="UseHttps"><see langword="true"/> for <c>https://</c>, <see langword="false"/> for <c>http://</c>.</param>
    /// <param name="IgnoreCertificateErrors">Skips TLS certificate validation for this probe's own requests.</param>
    /// <param name="TimeoutSeconds">
    /// Omitted on create defaults to <see cref="HttpProbeOptions.DefaultTimeoutSeconds"/>;
    /// omitted on update keeps the probe's current value (plan 003's Decision 4a).
    /// </param>
    /// <param name="ExpectedStatusCode">Unset means "only a 2xx response counts as success."</param>
    /// <param name="ExpectedStatusCodeNegate">Negates <paramref name="ExpectedStatusCode"/>'s match.</param>
    /// <param name="ExpectedBodyText">
    /// Evaluated only once the status check passes. Rejected when <c>method</c> is
    /// <c>"head"</c> — a HEAD response has no body.
    /// </param>
    /// <param name="ExpectedBodyTextNegate">Negates <paramref name="ExpectedBodyText"/>'s match.</param>
    /// <param name="Credential">Optional credential to present. Omitted or <c>type: "none"</c> means no credential.</param>
    internal sealed record HttpProbeOptionsRequest(
        string? Method,
        string? Path,
        bool? UseHttps,
        bool? IgnoreCertificateErrors,
        int? TimeoutSeconds,
        int? ExpectedStatusCode,
        bool? ExpectedStatusCodeNegate,
        string? ExpectedBodyText,
        bool? ExpectedBodyTextNegate,
        HttpCredentialRequest? Credential);

    /// <param name="Type"><c>"none"</c>, <c>"bearer"</c> or <c>"basic"</c>.</param>
    /// <param name="Username">Basic auth only — not a secret, round-trips in the clear.</param>
    /// <param name="Secret">
    /// Write-only (plan 003's Decision 2): absent/null keeps the stored value, <c>""</c>
    /// clears it, non-empty is protected and replaces it. <c>type == "none"</c> clears any
    /// stored secret regardless of what else is sent.
    /// </param>
    internal sealed record HttpCredentialRequest(string? Type, string? Username, string? Secret);

    internal sealed record PauseProbeRequest(bool IsPaused);

    internal sealed record ReorderProbesRequest(Guid[] ProbeIds);

    /// <summary>What the admin list, the probe form and a script reading <c>GET /probes</c> need.</summary>
    public sealed record ProbeResponse(
        Guid Id,
        string Name,
        string Host,
        ProbeKind Kind,
        int PollIntervalSeconds,
        int FailureThreshold,
        bool IsPaused,
        int Position,
        ProbeStatus Status,
        string? LastDetail,
        DateTimeOffset? LastCheckedAt,
        Guid[] GroupIds,
        HttpProbeOptionsResponse? Http);

    /// <summary>Never carries a secret — only <see cref="HttpCredentialResponse.HasSecret"/>.</summary>
    public sealed record HttpProbeOptionsResponse(
        HttpProbeMethod Method,
        string Path,
        bool UseHttps,
        bool IgnoreCertificateErrors,
        int TimeoutSeconds,
        int? ExpectedStatusCode,
        bool ExpectedStatusCodeNegate,
        string? ExpectedBodyText,
        bool ExpectedBodyTextNegate,
        HttpCredentialResponse Credential);

    public sealed record HttpCredentialResponse(HttpCredentialType Type, string? Username, bool HasSecret);
}
