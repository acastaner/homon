using Homon.Domain.Messaging;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homon.Infrastructure.Messaging;

/// <summary>
/// The runner for <see cref="ProbeKind.Message"/>: it reaches out to nothing, reads the newest
/// message for the reporter named by <c>Probe.Host</c>, and lets
/// <see cref="MessageProbeEvaluator"/> decide what that means.
/// </summary>
/// <remarks>
/// A push probe still being <em>polled</em> is the point rather than an oddity. Nothing arriving
/// has to be able to change a probe's state, and only a tick can notice that nothing arrived —
/// so the dead man's switch needs a clock, not a webhook. Running through the ordinary scheduler
/// also means grouping, pausing, uptime, the observation history and plan 009's alerting all
/// apply with no message-specific code anywhere else.
/// </remarks>
public sealed class MessageProbeRunner(HomonDbContext database, TimeProvider timeProvider) : IProbeRunner
{
    public ProbeKind Kind => ProbeKind.Message;

    public async Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var latest = await database.Messages
            .AsNoTracking()
            .Where(m => database.Reporters.Any(r => r.Id == m.ReporterId && r.Identifier == probe.Host))
            .OrderByDescending(m => m.ReceivedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new MessageSnapshot(m.Status, m.ReceivedAt, m.NextExpectedAt))
            .FirstOrDefaultAsync(cancellationToken);

        // A probe whose host names no reporter reads Unknown rather than throwing. The API refuses
        // to delete a reporter while a probe names it, so this should be unreachable — but a
        // probe silently pointing at nothing must not look like a healthy one, and must not take
        // the tick down either.
        if (latest is null && !await database.Reporters.AnyAsync(r => r.Identifier == probe.Host, cancellationToken))
        {
            return new ProbeResult(false, null, "message: no reporter has this identifier", ProbeStatus.Unknown);
        }

        var evaluation = MessageProbeEvaluator.Evaluate(latest, timeProvider.GetUtcNow());

        // No latency: there is no round trip to time. ProbeObservation.LatencyMs already documents
        // null as "the kind does not report a latency", and the SPA only draws a sparkline for
        // ping probes.
        return new ProbeResult(evaluation.Succeeded, null, evaluation.Detail, evaluation.Status);
    }
}
