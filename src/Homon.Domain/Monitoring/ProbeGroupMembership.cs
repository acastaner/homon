namespace Homon.Domain.Monitoring;

/// <summary>
/// One probe's membership in one <see cref="ProbeGroup"/>, and its position within it. The
/// join row of the many-to-many between <see cref="Probe"/> and <see cref="ProbeGroup"/>.
/// </summary>
public sealed class ProbeGroupMembership
{
    public Guid GroupId { get; set; }

    public Guid ProbeId { get; set; }

    /// <summary>An admin-set order within the group. Not a unique index — see ProbeGroupMembershipConfiguration.</summary>
    public int Position { get; set; }

    public ProbeGroup Group { get; set; } = null!;
}
