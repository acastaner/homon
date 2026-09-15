using Homon.Domain.Monitoring;

namespace Homon.Api.Tests;

public class ProbeStateMachineTests
{
    [Fact]
    public void Derive_is_always_unknown_when_never_polled_regardless_of_counters()
    {
        Assert.Equal(ProbeStatus.Unknown, ProbeStateMachine.Derive(0, 0, 2, everPolled: false));
        Assert.Equal(ProbeStatus.Unknown, ProbeStateMachine.Derive(5, 0, 2, everPolled: false));
        Assert.Equal(ProbeStatus.Unknown, ProbeStateMachine.Derive(0, 5, 2, everPolled: false));
    }

    [Theory]
    [InlineData(0, 0, true, 1, 0, ProbeStatus.Unstable)] // Unknown -> success, streak < N -> Unstable
    [InlineData(0, 0, false, 0, 1, ProbeStatus.Unstable)] // Unknown -> failure, streak < N -> Unstable
    [InlineData(1, 0, true, 2, 0, ProbeStatus.Up)] // streak reaches N on success -> Up
    [InlineData(0, 1, false, 0, 2, ProbeStatus.Down)] // streak reaches N on failure -> Down
    [InlineData(2, 0, false, 0, 1, ProbeStatus.Unstable)] // Up -> failure, streak < N -> Unstable
    [InlineData(0, 2, true, 1, 0, ProbeStatus.Unstable)] // Down -> success, streak < N -> Unstable
    [InlineData(1, 0, false, 0, 1, ProbeStatus.Unstable)] // Unstable -> failure, streak < N -> Unstable
    [InlineData(0, 1, true, 1, 0, ProbeStatus.Unstable)] // Unstable -> success, streak < N -> Unstable
    public void Apply_follows_the_transition_table_for_threshold_two(
        int startSuccesses, int startFailures, bool succeeded,
        int expectedSuccesses, int expectedFailures, ProbeStatus expectedStatus)
    {
        var (successes, failures, status) = ProbeStateMachine.Apply(startSuccesses, startFailures, 2, succeeded);

        Assert.Equal(expectedSuccesses, successes);
        Assert.Equal(expectedFailures, failures);
        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public void A_single_success_resets_an_in_progress_failure_streak_and_vice_versa()
    {
        var afterOneFailure = ProbeStateMachine.Apply(0, 0, 3, succeeded: false);
        Assert.Equal((0, 1, ProbeStatus.Unstable), afterOneFailure);

        var afterASuccess = ProbeStateMachine.Apply(
            afterOneFailure.Successes, afterOneFailure.Failures, 3, succeeded: true);
        Assert.Equal((1, 0, ProbeStatus.Unstable), afterASuccess);

        var afterOneSuccess = ProbeStateMachine.Apply(0, 0, 3, succeeded: true);
        Assert.Equal((1, 0, ProbeStatus.Unstable), afterOneSuccess);

        var afterAFailure = ProbeStateMachine.Apply(
            afterOneSuccess.Successes, afterOneSuccess.Failures, 3, succeeded: false);
        Assert.Equal((0, 1, ProbeStatus.Unstable), afterAFailure);
    }

    [Fact]
    public void A_failure_threshold_of_one_skips_unstable_entirely_in_both_directions()
    {
        var success = ProbeStateMachine.Apply(0, 0, 1, succeeded: true);
        Assert.Equal(ProbeStatus.Up, success.Status);

        var failure = ProbeStateMachine.Apply(0, 0, 1, succeeded: false);
        Assert.Equal(ProbeStatus.Down, failure.Status);

        // Flipping straight back the other way, never passing through Unstable.
        var backToDown = ProbeStateMachine.Apply(success.Successes, success.Failures, 1, succeeded: false);
        Assert.Equal(ProbeStatus.Down, backToDown.Status);

        var backToUp = ProbeStateMachine.Apply(failure.Successes, failure.Failures, 1, succeeded: true);
        Assert.Equal(ProbeStatus.Up, backToUp.Status);
    }

    [Theory]
    [InlineData(3, 0, 3, ProbeStatus.Up)]
    [InlineData(0, 3, 3, ProbeStatus.Down)]
    [InlineData(2, 0, 3, ProbeStatus.Unstable)]
    [InlineData(0, 2, 3, ProbeStatus.Unstable)]
    public void Derive_reads_the_counters_under_the_given_threshold(
        int successes, int failures, int threshold, ProbeStatus expected)
    {
        Assert.Equal(expected, ProbeStateMachine.Derive(successes, failures, threshold, everPolled: true));
    }
}
