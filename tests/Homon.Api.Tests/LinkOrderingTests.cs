using Homon.Domain.Links;

namespace Homon.Api.Tests;

public class LinkOrderingTests
{
    [Fact]
    public void A_full_permutation_renumbers_zero_through_n_minus_one_in_the_given_order()
    {
        var a = new Link { Id = Guid.NewGuid(), Position = 5 };
        var b = new Link { Id = Guid.NewGuid(), Position = 1 };
        var c = new Link { Id = Guid.NewGuid(), Position = 9 };
        var links = new List<Link> { a, b, c };

        Link.Reorder(links, [c.Id, a.Id, b.Id]);

        Assert.Equal(0, c.Position);
        Assert.Equal(1, a.Position);
        Assert.Equal(2, b.Position);
    }

    [Fact]
    public void A_missing_id_throws()
    {
        var a = new Link { Id = Guid.NewGuid() };
        var b = new Link { Id = Guid.NewGuid() };
        var links = new List<Link> { a, b };

        Assert.Throws<ArgumentException>(() => Link.Reorder(links, [a.Id]));
    }

    [Fact]
    public void An_extra_id_throws()
    {
        var a = new Link { Id = Guid.NewGuid() };
        var links = new List<Link> { a };

        Assert.Throws<ArgumentException>(() => Link.Reorder(links, [a.Id, Guid.NewGuid()]));
    }

    [Fact]
    public void A_duplicate_id_throws()
    {
        var a = new Link { Id = Guid.NewGuid() };
        var b = new Link { Id = Guid.NewGuid() };
        var links = new List<Link> { a, b };

        Assert.Throws<ArgumentException>(() => Link.Reorder(links, [a.Id, a.Id]));
    }
}
