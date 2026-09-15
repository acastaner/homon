using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homon.Api.Tests;

/// <summary>
/// Exercises <see cref="ProbeObservationRetentionService.SweepAsync"/> directly, constructed
/// by hand with a <see cref="FixedTimeProvider"/> — never through the hosted loop.
/// </summary>
public class ProbeObservationRetentionServiceTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task SweepAsync_deletes_only_observations_older_than_the_retention_window()
    {
        var probe = await SeedProbeAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

            database.ProbeObservations.AddRange(
                MakeObservation(probe.Id, Now - TimeSpan.FromDays(31)), // older than the cutoff
                MakeObservation(probe.Id, Now - TimeSpan.FromDays(30.5)), // older than the cutoff
                MakeObservation(probe.Id, Now - TimeSpan.FromDays(1))); // newer than the cutoff

            await database.SaveChangesAsync();
        }

        var service = BuildService(Now);
        var deleted = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(2, deleted);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDatabase = verifyScope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var remaining = await verifyDatabase.ProbeObservations.Where(o => o.ProbeId == probe.Id).ToListAsync();

        Assert.Single(remaining);
        Assert.True(remaining[0].ObservedAt > Now - TimeSpan.FromDays(30));
    }

    [DatabaseFact]
    public async Task A_second_sweep_with_nothing_left_to_delete_returns_zero()
    {
        var probe = await SeedProbeAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            database.ProbeObservations.Add(MakeObservation(probe.Id, Now - TimeSpan.FromDays(40)));
            await database.SaveChangesAsync();
        }

        var service = BuildService(Now);

        Assert.Equal(1, await service.SweepAsync(CancellationToken.None));
        Assert.Equal(0, await service.SweepAsync(CancellationToken.None));
    }

    private static ProbeObservation MakeObservation(Guid probeId, DateTimeOffset observedAt) =>
        new() { ProbeId = probeId, ObservedAt = observedAt, Succeeded = true, LatencyMs = 1.0 };

    private async Task<Probe> SeedProbeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var probe = new Probe
        {
            Id = Guid.NewGuid(),
            Name = $"probe-{Guid.NewGuid():N}",
            Host = "probe.test",
            Kind = ProbeKind.Ping,
            PollInterval = TimeSpan.FromSeconds(60),
            FailureThreshold = 2,
        };

        database.Probes.Add(probe);
        await database.SaveChangesAsync();

        return probe;
    }

    private ProbeObservationRetentionService BuildService(DateTimeOffset now) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<MonitoringOptions>(new MonitoringOptions()),
            new FixedTimeProvider(now),
            NullLogger<ProbeObservationRetentionService>.Instance);
}
