using Homon.Domain.Monitoring;

namespace Homon.Api.Tests;

/// <summary>
/// Plan 026's Decision 1: <see cref="Probe.DownSince"/> is set on entering Down and cleared on
/// reaching Up, and only <see cref="Probe.RecordObservation"/> changes it. Pure — no database.
/// </summary>
public class ProbeOutageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

    private static Probe NewProbe(int threshold = 2) => new() { Name = "NAS", FailureThreshold = threshold };

    [Fact]
    public void The_second_failure_of_two_opens_the_outage_at_that_observation()
    {
        var probe = NewProbe();

        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(false, null, "x", At(0)));
        Assert.Null(probe.DownSince);

        Assert.Equal(ProbeOutageChange.WentDown, probe.RecordObservation(false, null, "x", At(1)));
        Assert.Equal(ProbeStatus.Down, probe.Status);
        Assert.Equal(At(1), probe.DownSince);
    }

    [Fact]
    public void Further_failures_do_not_reopen_or_move_the_outage()
    {
        var probe = NewProbe();
        probe.RecordObservation(false, null, null, At(0));
        probe.RecordObservation(false, null, null, At(1));

        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(false, null, null, At(2)));
        Assert.Equal(At(1), probe.DownSince);
    }

    [Fact]
    public void Unstable_between_down_and_up_keeps_the_outage_and_mails_nothing()
    {
        var probe = NewProbe();
        probe.RecordObservation(false, null, null, At(0));
        probe.RecordObservation(false, null, null, At(1));

        // One success: Down -> Unstable.
        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(true, 1, null, At(2)));
        Assert.Equal(ProbeStatus.Unstable, probe.Status);
        Assert.Equal(At(1), probe.DownSince);

        // Flapping back to Down is the same outage, not a second one.
        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(false, null, null, At(3)));
        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(false, null, null, At(4)));
        Assert.Equal(ProbeStatus.Down, probe.Status);
        Assert.Equal(At(1), probe.DownSince);
    }

    [Fact]
    public void Reaching_up_closes_the_outage_and_clears_it()
    {
        var probe = NewProbe();
        probe.RecordObservation(false, null, null, At(0));
        probe.RecordObservation(false, null, null, At(1));
        probe.RecordObservation(true, 1, null, At(2));

        Assert.Equal(ProbeOutageChange.Recovered, probe.RecordObservation(true, 1, null, At(3)));
        Assert.Equal(ProbeStatus.Up, probe.Status);
        Assert.Null(probe.DownSince);

        // Staying up reports nothing further.
        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(true, 1, null, At(4)));
    }

    [Fact]
    public void A_healthy_probe_that_becomes_up_for_the_first_time_is_not_a_recovery()
    {
        var probe = NewProbe();

        probe.RecordObservation(true, 1, null, At(0));

        Assert.Equal(ProbeOutageChange.None, probe.RecordObservation(true, 1, null, At(1)));
        Assert.Equal(ProbeStatus.Up, probe.Status);
        Assert.Null(probe.DownSince);
    }

    [Fact]
    public void With_a_threshold_of_one_the_first_ever_failure_opens_an_outage()
    {
        var probe = NewProbe(threshold: 1);

        Assert.Equal(ProbeOutageChange.WentDown, probe.RecordObservation(false, null, null, At(0)));
        Assert.Equal(At(0), probe.DownSince);

        Assert.Equal(ProbeOutageChange.Recovered, probe.RecordObservation(true, 1, null, At(1)));
    }

    [Fact]
    public void A_derived_status_drives_the_outage_whatever_the_threshold()
    {
        var probe = NewProbe(threshold: 5);

        Assert.Equal(
            ProbeOutageChange.WentDown,
            probe.RecordObservation(false, null, "message: failure", At(0), ProbeStatus.Down));
        Assert.Equal(At(0), probe.DownSince);

        // No verdict: the outage stays open.
        Assert.Equal(
            ProbeOutageChange.None,
            probe.RecordObservation(false, null, "message: unknown", At(1), ProbeStatus.Unknown));
        Assert.Equal(At(0), probe.DownSince);

        Assert.Equal(
            ProbeOutageChange.Recovered,
            probe.RecordObservation(true, null, "message: success", At(2), ProbeStatus.Up));
        Assert.Null(probe.DownSince);
    }

    [Fact]
    public void Pausing_and_unpausing_while_down_leaves_the_outage_alone()
    {
        var probe = NewProbe();
        probe.RecordObservation(false, null, null, At(0));
        probe.RecordObservation(false, null, null, At(1));

        probe.Pause();
        Assert.Equal(At(1), probe.DownSince);

        probe.Unpause();
        Assert.Equal(ProbeStatus.Down, probe.Status);
        Assert.Equal(At(1), probe.DownSince);
    }

    [Fact]
    public void Changing_the_threshold_does_not_touch_the_outage()
    {
        var probe = NewProbe(threshold: 3);
        probe.RecordObservation(false, null, null, At(0));
        probe.RecordObservation(false, null, null, At(1));
        Assert.Null(probe.DownSince);

        // Lowering the threshold flips the status to Down straight away, but the outage is
        // only recorded by the next real poll.
        probe.ChangeFailureThreshold(2);
        Assert.Equal(ProbeStatus.Down, probe.Status);
        Assert.Null(probe.DownSince);

        Assert.Equal(ProbeOutageChange.WentDown, probe.RecordObservation(false, null, null, At(2)));
        Assert.Equal(At(2), probe.DownSince);
    }
}
