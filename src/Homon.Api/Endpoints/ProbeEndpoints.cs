using Homon.Api.Authentication;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
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
        ProbeRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var fieldsError = ValidateFields(request, requireFailureThreshold: false, out var name, out var host,
            out var pollIntervalSeconds, out var failureThreshold);
        if (fieldsError is not null)
        {
            return fieldsError;
        }

        var kindError = ValidateKind(request.Kind);
        if (kindError is not null)
        {
            return kindError;
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
            Kind = ProbeKind.Ping,
            PollInterval = TimeSpan.FromSeconds(pollIntervalSeconds),
            FailureThreshold = failureThreshold,
            Position = (maxPosition ?? -1) + 1,
            Status = ProbeStatus.Unknown,
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
        Guid id, ProbeRequest request, HomonDbContext database, CancellationToken cancellationToken)
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

    private static ValidationProblem? ValidateKind(string? kind)
    {
        var trimmed = kind?.Trim();

        if (string.Equals(trimmed, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var detail = trimmed is "http" or "smb" or "snmp"
            ? $"Probe kind '{trimmed}' ships with a later plan and is not accepted yet."
            : "Probe kind must be 'ping'.";

        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [detail] });
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
            groupIds);

    /// <param name="Name">The probe's display name.</param>
    /// <param name="Host">A bare hostname or IP — no scheme, no path.</param>
    /// <param name="Kind">
    /// Only <c>"ping"</c> is accepted today. Ignored on <c>PUT</c> — a probe's kind cannot
    /// change after creation.
    /// </param>
    /// <param name="PollIntervalSeconds">How often the probe is polled, in seconds.</param>
    /// <param name="FailureThreshold">
    /// Consecutive polls, in either direction, that flip the probe's status. Optional on
    /// <c>POST</c> (defaults to <see cref="Probe.DefaultFailureThreshold"/>), required on
    /// <c>PUT</c>.
    /// </param>
    /// <param name="GroupIds">Every <see cref="ProbeGroup"/> this probe should belong to.</param>
    internal sealed record ProbeRequest(
        string? Name,
        string? Host,
        string? Kind,
        int? PollIntervalSeconds,
        int? FailureThreshold,
        Guid[]? GroupIds);

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
        Guid[] GroupIds);
}
