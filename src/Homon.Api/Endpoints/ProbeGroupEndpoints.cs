using Homon.Api.Authentication;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Homon.Api.Endpoints;

/// <summary>
/// CRUD, reorder and membership for <see cref="ProbeGroup"/>, under <c>/probe-groups</c>.
/// </summary>
/// <remarks>
/// <c>GET</c> is <see cref="HomonPolicies.Reader"/>-gated (unlike <c>/probes</c> itself) — a
/// group carries only a name and probe ids, which <c>GET /status</c> already exposes to every
/// reader anyway. Every write stays <see cref="HomonPolicies.Administrator"/> — see plan 002's
/// Decision 7/8.
/// </remarks>
internal static class ProbeGroupEndpoints
{
    internal static RouteGroupBuilder MapProbeGroupEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/probe-groups");

        group.MapGet("", GetProbeGroupsAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetProbeGroups")
            .WithSummary("Lists every probe group, in display order.");

        group.MapGet("/{id:guid}", GetProbeGroupAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetProbeGroup")
            .WithSummary("Reads one probe group.");

        group.MapPost("", CreateProbeGroupAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("CreateProbeGroup")
            .WithSummary("Creates a probe group, appended at the end of the display order.");

        group.MapPut("/{id:guid}", UpdateProbeGroupAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("RenameProbeGroup")
            .WithSummary("Renames a probe group.");

        group.MapDelete("/{id:guid}", DeleteProbeGroupAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("DeleteProbeGroup")
            .WithSummary("Deletes a probe group. Its probes are kept.");

        group.MapPut("/order", ReorderProbeGroupsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("ReorderProbeGroups")
            .WithSummary("Sets the display order for every probe group.");

        group.MapPut("/{id:guid}/members", SetMembersAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("SetProbeGroupMembers")
            .WithSummary("Sets a probe group's membership and their order.");

        return group;
    }

    private static async Task<Ok<ProbeGroupResponse[]>> GetProbeGroupsAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var groups = await database.ProbeGroups
            .Include(g => g.Members.OrderBy(m => m.Position))
            .OrderBy(g => g.Position)
            .ThenBy(g => g.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(groups.Select(ToResponse).ToArray());
    }

    private static async Task<Results<Ok<ProbeGroupResponse>, NotFound>> GetProbeGroupAsync(
        Guid id, HomonDbContext database, CancellationToken cancellationToken)
    {
        var group = await database.ProbeGroups
            .Include(g => g.Members.OrderBy(m => m.Position))
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        return group is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(group));
    }

    private static async Task<Results<Created<ProbeGroupResponse>, ValidationProblem>> CreateProbeGroupAsync(
        ProbeGroupRequest request, HomonDbContext database, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (nameError, name) = await ValidateNameAsync(database, request.Name, excludingId: null, cancellationToken);
        if (nameError is not null)
        {
            return nameError;
        }

        var maxPosition = await database.ProbeGroups
            .Select(g => (int?)g.Position)
            .MaxAsync(cancellationToken);

        var group = new ProbeGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = ProbeGroup.Normalize(name),
            Position = (maxPosition ?? -1) + 1,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        database.ProbeGroups.Add(group);

        var saveError = await SaveOrConflictAsync(database, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return TypedResults.Created($"/api/v1/probe-groups/{group.Id}", ToResponse(group));
    }

    private static async Task<Results<Ok<ProbeGroupResponse>, ValidationProblem, NotFound>> UpdateProbeGroupAsync(
        Guid id, ProbeGroupRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var group = await database.ProbeGroups
            .Include(g => g.Members.OrderBy(m => m.Position))
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group is null)
        {
            return TypedResults.NotFound();
        }

        var (nameError, name) = await ValidateNameAsync(database, request.Name, excludingId: id, cancellationToken);
        if (nameError is not null)
        {
            return nameError;
        }

        group.Name = name;
        group.NormalizedName = ProbeGroup.Normalize(name);

        var saveError = await SaveOrConflictAsync(database, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return TypedResults.Ok(ToResponse(group));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteProbeGroupAsync(
        Guid id, HomonDbContext database, CancellationToken cancellationToken)
    {
        var group = await database.ProbeGroups.FindAsync([id], cancellationToken);

        if (group is null)
        {
            return TypedResults.NotFound();
        }

        // Cascade delete removes ProbeGroupMembership rows (GroupId FK) — the probes
        // themselves are untouched.
        database.ProbeGroups.Remove(group);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> ReorderProbeGroupsAsync(
        ReorderProbeGroupsRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var groups = await database.ProbeGroups.ToListAsync(cancellationToken);
        var requested = request.GroupIds ?? [];

        var permutationError = ProbeEndpoints.ValidatePermutation(
            requested, groups.Select(g => g.Id).ToArray(), "groupIds");
        if (permutationError is not null)
        {
            return permutationError;
        }

        for (var i = 0; i < requested.Length; i++)
        {
            groups.Single(g => g.Id == requested[i]).Position = i;
        }

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ProbeGroupResponse>, ValidationProblem, NotFound>> SetMembersAsync(
        Guid id, SetMembersRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var group = await database.ProbeGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group is null)
        {
            return TypedResults.NotFound();
        }

        var probeIds = request.ProbeIds ?? [];
        var existingProbeIds = await database.Probes.Select(p => p.Id).ToListAsync(cancellationToken);

        var membersError = ValidateMemberIds(probeIds, [.. existingProbeIds]);
        if (membersError is not null)
        {
            return membersError;
        }

        group.ReplaceMembers(probeIds);

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(group));
    }

    private static async Task<(ValidationProblem? Error, string Name)> ValidateNameAsync(
        HomonDbContext database, string? rawName, Guid? excludingId, CancellationToken cancellationToken)
    {
        var name = rawName?.Trim() ?? string.Empty;

        if (name.Length is 0 or > ProbeGroup.NameMaxLength)
        {
            return (
                TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = [$"Name is required and at most {ProbeGroup.NameMaxLength} characters."],
                }),
                name);
        }

        var normalized = ProbeGroup.Normalize(name);

        var duplicateExists = await database.ProbeGroups
            .Where(g => g.NormalizedName == normalized)
            .Where(g => excludingId == null || g.Id != excludingId)
            .AnyAsync(cancellationToken);

        if (duplicateExists)
        {
            return (
                TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["A group with this name already exists."],
                }),
                name);
        }

        return (null, name);
    }

    /// <summary>
    /// Commits pending changes, turning a Postgres unique-violation (two concurrent renames
    /// racing the same name past the check above) into the same <see cref="ValidationProblem"/>
    /// the check itself would have produced.
    /// </summary>
    private static async Task<ValidationProblem?> SaveOrConflictAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A group with this name already exists."],
            });
        }
    }

    private static ValidationProblem? ValidateMemberIds(Guid[] probeIds, HashSet<Guid> existingProbeIds)
    {
        var seen = new HashSet<Guid>();

        foreach (var id in probeIds)
        {
            if (!seen.Add(id))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["probeIds"] = ["The list contains a duplicate id."],
                });
            }

            if (!existingProbeIds.Contains(id))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["probeIds"] = [$"Unknown probe id: {id}."],
                });
            }
        }

        return null;
    }

    private static ProbeGroupResponse ToResponse(ProbeGroup group) =>
        new(group.Id, group.Name, group.Members.OrderBy(m => m.Position).Select(m => m.ProbeId).ToArray());

    /// <param name="Name">The group's display name — unique ignoring case, at most <see cref="ProbeGroup.NameMaxLength"/> characters.</param>
    internal sealed record ProbeGroupRequest(string? Name);

    internal sealed record ReorderProbeGroupsRequest(Guid[] GroupIds);

    internal sealed record SetMembersRequest(Guid[] ProbeIds);

    /// <summary>What the admin group list and the dashboard's status payload both need.</summary>
    /// <param name="Id">The group's id.</param>
    /// <param name="Name">The group's display name.</param>
    /// <param name="ProbeIds">Every member probe's id, in group order.</param>
    public sealed record ProbeGroupResponse(Guid Id, string Name, Guid[] ProbeIds);
}
