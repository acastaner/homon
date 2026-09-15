using Homon.Domain.Monitoring;

namespace Homon.Api.Tests;

public class ProbeGroupTests
{
    [Fact]
    public void ReplaceMembers_renumbers_zero_to_n_minus_one_in_the_given_order()
    {
        var group = new ProbeGroup { Id = Guid.NewGuid() };
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        group.ReplaceMembers([first, second, third]);

        Assert.Equal(0, group.Members.Single(m => m.ProbeId == first).Position);
        Assert.Equal(1, group.Members.Single(m => m.ProbeId == second).Position);
        Assert.Equal(2, group.Members.Single(m => m.ProbeId == third).Position);

        group.ReplaceMembers([third, first, second]);

        Assert.Equal(0, group.Members.Single(m => m.ProbeId == third).Position);
        Assert.Equal(1, group.Members.Single(m => m.ProbeId == first).Position);
        Assert.Equal(2, group.Members.Single(m => m.ProbeId == second).Position);
    }

    [Fact]
    public void ReplaceMembers_reuses_existing_membership_rows_across_a_reorder()
    {
        var group = new ProbeGroup { Id = Guid.NewGuid() };
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        group.ReplaceMembers([first, second]);
        var firstMembershipBefore = group.Members.Single(m => m.ProbeId == first);

        group.ReplaceMembers([second, first]);
        var firstMembershipAfter = group.Members.Single(m => m.ProbeId == first);

        // Same object reference survives the reorder — EF would throw a composite-key
        // identity conflict if this instead removed and re-added the same (GroupId, ProbeId).
        Assert.Same(firstMembershipBefore, firstMembershipAfter);
        Assert.Equal(1, firstMembershipAfter.Position);
    }

    [Fact]
    public void Include_does_nothing_on_a_repeat_call()
    {
        var group = new ProbeGroup { Id = Guid.NewGuid() };
        var probeId = Guid.NewGuid();

        group.Include(probeId);
        group.Include(probeId);

        Assert.Single(group.Members);
        Assert.Equal(0, group.Members[0].Position);
    }

    [Fact]
    public void Include_appends_at_the_end()
    {
        var group = new ProbeGroup { Id = Guid.NewGuid() };
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        group.Include(first);
        group.Include(second);

        Assert.Equal(0, group.Members.Single(m => m.ProbeId == first).Position);
        Assert.Equal(1, group.Members.Single(m => m.ProbeId == second).Position);
    }

    [Fact]
    public void Exclude_removes_exactly_one_membership()
    {
        var group = new ProbeGroup { Id = Guid.NewGuid() };
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        group.Include(first);
        group.Include(second);

        group.Exclude(first);

        var remaining = Assert.Single(group.Members);
        Assert.Equal(second, remaining.ProbeId);
    }

    [Theory]
    [InlineData("hosts", "HOSTS")]
    [InlineData("  Hosts  ", "HOSTS")]
    [InlineData("Storage And Backups", "STORAGE AND BACKUPS")]
    public void Normalize_trims_and_uppercases(string input, string expected)
    {
        Assert.Equal(expected, ProbeGroup.Normalize(input));
    }
}
