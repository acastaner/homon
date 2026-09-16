namespace Homon.Domain.Links;

/// <summary>
/// A bookmark the dashboard shows beside the status cards: a NAS's web UI, the router, a
/// shared recipe site. Always opens in a new tab — that is a rule of the SPA, not a
/// per-link option.
/// </summary>
public sealed class Link
{
    /// <summary>Longest <see cref="Title"/> the admin may give a link.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>
    /// Longest <see cref="Url"/> accepted — the longest length every major browser/proxy
    /// reliably accepts.
    /// </summary>
    public const int UrlMaxLength = 2048;

    /// <summary>Longest <see cref="Description"/> the admin may give a link.</summary>
    public const int DescriptionMaxLength = 1000;

    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>An admin-set order among links. Not a unique index — see LinkConfiguration.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Renumbers every link's <see cref="Position"/> to <c>0..n-1</c> in <paramref
    /// name="orderedIds"/>'s order — the same reordering convention plan 002 establishes for
    /// <c>ProbeGroup</c>/<c>Probe</c>: <c>Position</c> is a sort key, not a dense index, and
    /// the caller's array order is the wire format.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="orderedIds"/> is not exactly a permutation of <paramref name="links"/>'
    /// ids — a different count, a duplicate, a missing id or an id that names no link.
    /// </exception>
    public static void Reorder(IReadOnlyList<Link> links, IReadOnlyList<Guid> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(orderedIds);

        if (orderedIds.Count != links.Count)
        {
            throw new ArgumentException(
                $"Expected {links.Count} link ids, got {orderedIds.Count}.", nameof(orderedIds));
        }

        var seen = new HashSet<Guid>();
        var byId = links.ToDictionary(link => link.Id);

        foreach (var id in orderedIds)
        {
            if (!seen.Add(id))
            {
                throw new ArgumentException($"Duplicate link id: {id}.", nameof(orderedIds));
            }

            if (!byId.ContainsKey(id))
            {
                throw new ArgumentException($"Unknown link id: {id}.", nameof(orderedIds));
            }
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            byId[orderedIds[i]].Position = i;
        }
    }
}
