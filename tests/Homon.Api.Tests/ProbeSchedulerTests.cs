using Homon.Domain.Alerts;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homon.Api.Tests;

/// <summary>
/// Exercises <see cref="ProbeScheduler.TickAsync"/> directly, with a fake <see cref="IProbeRunner"/>
/// and a <see cref="FixedTimeProvider"/> — never through the hosted loop (that stays disabled
/// for every test host; see <c>HomonApiFactory</c>).
/// </summary>
public class ProbeSchedulerTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_probe_with_no_prior_observation_is_due_and_gains_one_observation()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null);
        var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(true, 4.2, null)));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
        Assert.Equal(Epoch, reloaded.LastObservedAt);
        Assert.Equal(1, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task A_probe_still_inside_its_poll_interval_is_skipped()
    {
        var probe = await SeedProbeAsync(lastObservedAt: Epoch - TimeSpan.FromSeconds(10), pollInterval: TimeSpan.FromSeconds(60));
        var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(true, 1.0, null)));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task A_paused_probe_is_never_polled_even_when_due_by_time()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null, isPaused: true);
        var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(true, 1.0, null)));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task Overlapping_ticks_never_double_poll_a_still_running_probe()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null);

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var callCount = 0;

        var runner = new FakeProbeRunner(async (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            started.TrySetResult();
            await release.Task;
            return new ProbeResult(true, 1.0, null);
        });

        var scheduler = BuildScheduler(runner, Epoch);

        var firstTick = scheduler.TickAsync(CancellationToken.None);
        await started.Task;

        // The probe is still mid-poll (blocked on `release`) — a second tick must see it
        // still "due" by the database (LastObservedAt not yet written) but must not dispatch
        // a second poll: the _inFlight set is what stops it, not the database state.
        await scheduler.TickAsync(CancellationToken.None);

        release.SetResult();
        await firstTick;

        Assert.Equal(1, callCount);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        Assert.Equal(1, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task A_runner_that_throws_still_lets_the_tick_finish_and_records_a_failed_observation()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null);
        var runner = new FakeProbeRunner((_, _) => throw new InvalidOperationException("boom"));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var observation = await database.ProbeObservations.SingleAsync(o => o.ProbeId == probe.Id);
        Assert.False(observation.Succeeded);
        Assert.StartsWith("probe runner threw:", observation.Detail, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Advancing_time_past_the_poll_interval_makes_a_polled_probe_due_again()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null, pollInterval: TimeSpan.FromSeconds(30));
        var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(true, 1.0, null)));

        var timeProvider = new FixedTimeProvider(Epoch);
        var scheduler = BuildScheduler(runner, timeProvider);

        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None); // still inside the interval — no second poll

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Equal(2, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task A_derived_status_overrides_what_the_streak_counters_imply()
    {
        // A message probe's authority is the reporter's own verdict: one "warning" report must
        // stay Unstable rather than accumulating into Down as the same message is re-read on
        // every tick (plan 021, Decision 5). The counters still advance underneath it.
        // A zero poll interval so every tick is due against the fixed clock: three polls of the
        // same warning would be Down by the second one without the derived status, since the
        // seeded FailureThreshold is 2.
        var probe = await SeedProbeAsync(lastObservedAt: null, pollInterval: TimeSpan.Zero);
        var runner = new FakeProbeRunner((_, _) =>
            Task.FromResult(new ProbeResult(false, null, "message: warning, reported 2026-01-01 00:00Z", ProbeStatus.Unstable)));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
        Assert.Equal(ProbeStatus.Unstable, reloaded.Status);
        Assert.Equal(3, reloaded.ConsecutiveFailureCount);
        Assert.Equal(3, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task A_derived_unknown_records_no_observation_but_still_stamps_the_probe()
    {
        // "No verdict" (plan 021, A1): recording an observation would give uptime a denominator
        // it has not earned, and leaving LastObservedAt null would re-dispatch the probe on
        // every five-second tick forever.
        var probe = await SeedProbeAsync(lastObservedAt: null);
        var runner = new FakeProbeRunner((_, _) =>
            Task.FromResult(new ProbeResult(false, null, "message: no report received yet", ProbeStatus.Unknown)));

        var scheduler = BuildScheduler(runner, Epoch);
        await scheduler.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
        Assert.Equal(ProbeStatus.Unknown, reloaded.Status);
        Assert.Equal(Epoch, reloaded.LastObservedAt);
        Assert.Equal("message: no report received yet", reloaded.LastDetail);
        Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == probe.Id));
    }

    [DatabaseFact]
    public async Task Going_down_writes_one_pending_outbox_row_in_the_same_save()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null, pollInterval: TimeSpan.Zero, failureThreshold: 1);
        try
        {
            var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(false, null, "ping: TimedOut")));

            var scheduler = BuildScheduler(runner, Epoch);
            await scheduler.TickAsync(CancellationToken.None);

            using var scope = factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

            var rows = await database.AlertNotifications.Where(n => n.ProbeId == probe.Id).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(AlertKind.Down, row.Kind);
            Assert.Equal(AlertDeliveryState.Pending, row.State);
            Assert.Equal(probe.Name, row.ProbeName);
            Assert.Equal("ping: TimedOut", row.Detail);
            Assert.Equal(Epoch, row.DownSince);

            var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
            Assert.Equal(Epoch, reloaded.DownSince);
        }
        finally
        {
            await RemoveProbeAsync(probe.Id);
        }
    }

    [DatabaseFact]
    public async Task An_over_long_detail_still_records_the_probe_down_with_a_truncated_alert()
    {
        // Probe.LastDetail is unbounded and a runner writes raw exception text into it; the
        // outbox column is not. The row is written in the scheduler's single save, so an
        // untruncated copy would reject the whole save and the probe would never go Down.
        var probe = await SeedProbeAsync(lastObservedAt: null, pollInterval: TimeSpan.Zero, failureThreshold: 1);
        try
        {
            var longDetail = new string('x', 2000);
            var runner = new FakeProbeRunner((_, _) => Task.FromResult(new ProbeResult(false, null, longDetail)));

            var scheduler = BuildScheduler(runner, Epoch);
            await scheduler.TickAsync(CancellationToken.None);

            using var scope = factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

            var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
            Assert.Equal(ProbeStatus.Down, reloaded.Status);
            Assert.Equal(Epoch, reloaded.DownSince);

            var row = Assert.Single(await database.AlertNotifications.Where(n => n.ProbeId == probe.Id).ToListAsync());
            Assert.Equal(AlertKind.Down, row.Kind);
            Assert.Equal(AlertNotification.DetailMaxLength, row.Detail!.Length);
        }
        finally
        {
            await RemoveProbeAsync(probe.Id);
        }
    }

    [DatabaseFact]
    public async Task Coming_back_up_writes_a_second_row_that_remembers_when_the_outage_began()
    {
        var probe = await SeedProbeAsync(lastObservedAt: null, pollInterval: TimeSpan.Zero, failureThreshold: 1);
        try
        {
            var clock = new FixedTimeProvider(Epoch);
            var succeed = false;
            var runner = new FakeProbeRunner((_, _) => Task.FromResult(
                succeed ? new ProbeResult(true, 1.0, null) : new ProbeResult(false, null, "ping: TimedOut")));

            var scheduler = BuildScheduler(runner, clock);
            await scheduler.TickAsync(CancellationToken.None);

            succeed = true;
            clock.Advance(TimeSpan.FromMinutes(10));
            await scheduler.TickAsync(CancellationToken.None);

            using var scope = factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

            var rows = await database.AlertNotifications
                .Where(n => n.ProbeId == probe.Id)
                .OrderBy(n => n.Id)
                .ToListAsync();
            Assert.Equal([AlertKind.Down, AlertKind.Up], rows.Select(r => r.Kind));
            Assert.Equal(Epoch, rows[1].DownSince);
            Assert.Equal(Epoch.AddMinutes(10), rows[1].OccurredAt);

            var reloaded = await database.Probes.SingleAsync(p => p.Id == probe.Id);
            Assert.Null(reloaded.DownSince);
        }
        finally
        {
            await RemoveProbeAsync(probe.Id);
        }
    }

    /// <summary>
    /// A zero-interval probe is due on every tick for the rest of the class, so a test that seeds
    /// one removes it: <c>Overlapping_ticks_never_double_poll_a_still_running_probe</c> counts
    /// every poll the shared fake runner receives, not only its own probe's.
    /// </summary>
    private async Task RemoveProbeAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.Probes.Where(p => p.Id == id).ExecuteDeleteAsync();
    }

    private async Task<Probe> SeedProbeAsync(
        DateTimeOffset? lastObservedAt, bool isPaused = false, TimeSpan? pollInterval = null, int failureThreshold = 2)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var probe = new Probe
        {
            Id = Guid.NewGuid(),
            Name = $"probe-{Guid.NewGuid():N}",
            Host = "probe.test",
            Kind = ProbeKind.Ping,
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(60),
            FailureThreshold = failureThreshold,
            IsPaused = isPaused,
            Status = isPaused ? ProbeStatus.Paused : ProbeStatus.Unknown,
            LastObservedAt = lastObservedAt,
        };

        database.Probes.Add(probe);
        await database.SaveChangesAsync();

        return probe;
    }

    private ProbeScheduler BuildScheduler(IProbeRunner runner, DateTimeOffset now) =>
        BuildScheduler(runner, new FixedTimeProvider(now));

    private ProbeScheduler BuildScheduler(IProbeRunner runner, TimeProvider timeProvider) =>
        new(
            new SingleRunnerScopeFactory(factory.Services, runner),
            new StaticOptionsMonitor<MonitoringOptions>(new MonitoringOptions()),
            timeProvider,
            NullLogger<ProbeScheduler>.Instance);

    /// <summary>
    /// Wraps the factory's own <see cref="IServiceScopeFactory"/> so every scope it creates
    /// additionally resolves the given <see cref="IProbeRunner"/> — the fake the test wants
    /// polled, without touching the host's own DI registration (<c>PingProbeRunner</c>, which
    /// would otherwise also be resolved and try to run).
    /// </summary>
    private sealed class SingleRunnerScopeFactory(IServiceProvider root, IProbeRunner runner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(root.CreateScope(), runner);

        private sealed class Scope(IServiceScope inner, IProbeRunner runner) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new RunnerOverrideProvider(inner.ServiceProvider, runner);

            public void Dispose() => inner.Dispose();
        }

        private sealed class RunnerOverrideProvider(IServiceProvider inner, IProbeRunner runner) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IEnumerable<IProbeRunner>))
                {
                    return new[] { runner };
                }

                return inner.GetService(serviceType);
            }
        }
    }

    private sealed class FakeProbeRunner(Func<Probe, CancellationToken, Task<ProbeResult>> run) : IProbeRunner
    {
        public ProbeKind Kind => ProbeKind.Ping;

        public Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken) =>
            run(probe, cancellationToken);
    }
}
