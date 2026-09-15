namespace Homon.Domain.Monitoring;

/// <summary>
/// A named, admin-created arrangement of probes on the dashboard — "Hosts" for servers and
/// network devices, "Services" for Jellyfin, Immich, Audiobookshelf checks, say. Many-to-many
/// with <see cref="Probe"/> (a probe may belong to several groups), optional (a probe in no
/// group appears in the dashboard's final, ungrouped section) and flat (groups do not nest).
/// See plan 002's Decision 7.
/// </summary>
public sealed class ProbeGroup
{
    /// <summary>Longest <see cref="Name"/> the admin may give a group.</summary>
    public const int NameMaxLength = 60;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Normalize"/> of <see cref="Name"/>, kept unique so two groups cannot differ
    /// only by case — the same pattern ASP.NET Identity's own tables already use.
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>An admin-set order among groups. Not a unique index — see ProbeGroupConfiguration.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ProbeGroupMembership> Members { get; set; } = [];

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    /// <summary>
    /// Updates the memberships already tracked, never removes and re-adds the same
    /// (GroupId, ProbeId) key — EF would throw a composite-key identity conflict — and
    /// renumbers every remaining position 0..n-1.
    /// </summary>
    public void ReplaceMembers(IReadOnlyList<Guid> orderedProbeIds)
    {
        var existing = Members.ToDictionary(m => m.ProbeId);
        var next = new List<ProbeGroupMembership>(orderedProbeIds.Count);

        for (var i = 0; i < orderedProbeIds.Count; i++)
        {
            var probeId = orderedProbeIds[i];

            if (!existing.TryGetValue(probeId, out var membership))
            {
                membership = new ProbeGroupMembership { GroupId = Id, ProbeId = probeId };
            }

            membership.Position = i;
            next.Add(membership);
        }

        Members.Clear();
        Members.AddRange(next);
    }

    /// <summary>Appends at the end; does nothing if the probe is already a member.</summary>
    public void Include(Guid probeId)
    {
        if (Members.Any(m => m.ProbeId == probeId))
        {
            return;
        }

        Members.Add(new ProbeGroupMembership { GroupId = Id, ProbeId = probeId, Position = Members.Count });
    }

    public void Exclude(Guid probeId) => Members.RemoveAll(m => m.ProbeId == probeId);
}
