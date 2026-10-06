import { ChevronLeft } from 'lucide-react'
import { Link as RouterLink, useParams, useSearchParams } from 'react-router'

import { LatencyChart } from '@/components/latency-chart'
import { StatusChip } from '@/components/status-chip'
import { ApiError } from '@/lib/api'
import { formatUptime } from '@/lib/format-uptime'
import { useProbeGroups } from '@/lib/probe-groups'
import {
  formatDuration,
  formatLatency,
  formatObservedAt,
  parseProbeRange,
  PROBE_RANGE_LABEL,
  PROBE_RANGES,
  useProbeHistory,
} from '@/lib/probe-detail'
import { useProbe } from '@/lib/probes'
import { useSession } from '@/lib/session'
import { formatCheckedAt, messageChipWord, plotsLatency, PROBE_KIND_LABEL, type ProbeState } from '@/lib/status'
import { pageTitle, useDocumentTitle } from '@/lib/use-document-title'

// The plan-012 visual patterns, copied rather than imported — the design pass sanctioned this
// duplication for a shared *visual* pattern that is not yet a shared component (Slice E:
// "extract it once a third instance exists"). `weather-page.tsx` copies the same constants.
const H1 = 'text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const PANEL = 'rounded-md border border-line bg-surface'
const SECTION_LABEL = 'text-[12px] font-semibold tracking-[0.12em] text-muted uppercase'
const TABLE_HEAD = 'text-[11.5px] font-semibold tracking-[0.08em] text-muted uppercase'
const TH = 'px-4 py-2 font-semibold'
const TD = 'px-4 py-3'
const TEXT_LINK = 'text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text'

/**
 * The range switch's buttons. `h-10` is the 40px tap-target floor `e2e/helpers.ts`'s
 * `expectTappable` enforces over every `<button>`; the `text-muted` / `border-line` pair is the
 * dashboard's own header button pair, so `e2e/contrast.spec.ts` has nothing new to police.
 */
const RANGE_BUTTON =
  'inline-flex h-10 items-center justify-center rounded-md border px-3 text-[13.5px] font-medium hover:border-line-strong hover:text-text'

/**
 * Copied from `dashboard-page.tsx`'s `detailClassName` (private there): the detail line's text
 * colour — `unstable`/`down` at 500 weight, `muted` otherwise.
 */
function detailClassName(state: ProbeState): string {
  if (state === 'down') {
    return 'font-medium text-down'
  }
  if (state === 'unstable') {
    return 'font-medium text-unstable'
  }
  return 'text-muted'
}

/**
 * One probe's page, reached from its name on the dashboard (plan 023): state and uptime, a large
 * latency graph for 24 hours, 7 days or 30 days, the 50 most recent polls, and — for an
 * administrator only — how the probe is configured.
 *
 * The history comes from the Reader-gated `/status/probes/{id}`, which never carries the host.
 * The configuration comes from the administrator-only `/probes/{id}` and is not even requested
 * for anyone else (D1), so a reader's browser never makes a call that would be refused.
 */
export function ProbePage() {
  const { id = '' } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const range = parseProbeRange(searchParams.get('range'))

  const { data: history, isError, error } = useProbeHistory(id, range)
  const session = useSession()
  const isAdministrator = session.data?.kind === 'administrator'

  useDocumentTitle(pageTitle(history?.name ?? 'Probe'))

  if (isError && error instanceof ApiError && error.status === 404) {
    return (
      <>
        <h1 className={`${H1} border-b border-line-strong pb-4`}>Probe not found</h1>
        <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
          <p className="text-[13.5px] text-muted">
            This probe no longer exists.{' '}
            <RouterLink to="/" className={TEXT_LINK}>
              Back to the dashboard
            </RouterLink>
          </p>
        </div>
      </>
    )
  }

  if (isError && history === undefined) {
    return (
      <div className={`${PANEL} px-4 py-3.5`}>
        <p className="text-[13.5px] text-muted">This probe could not be loaded.</p>
      </div>
    )
  }

  if (history === undefined) {
    return null
  }

  const shows = plotsLatency(history.kind)
  const totalPolls = history.latency.reduce((sum, bucket) => sum + bucket.polls, 0)

  return (
    <>
      <RouterLink to="/" className="inline-flex h-10 items-center gap-1 text-[13.5px] text-muted hover:text-text">
        <ChevronLeft aria-hidden="true" className="size-4" />
        Dashboard
      </RouterLink>

      <div className="flex flex-col gap-2 border-b border-line-strong pb-4">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
          <h1 className={H1}>{history.name}</h1>
          <StatusChip state={history.state} word={messageChipWord(history)} />
        </div>
        {/* Flat text, no per-value <span>, for the reason dashboard-page.tsx gives at its own
            "uptime, 30 days" line: the suites match a node's direct text, and nesting would
            make no element's own text equal the whole string. */}
        <p className="mono text-[13px] text-muted sm:text-sm">
          {formatUptime(history.uptimePercent)} uptime, 30 days · checked{' '}
          {formatCheckedAt(history.lastCheckedAt, new Date())}
        </p>
        {history.detail === null ? null : (
          <p className={`text-[14px] ${detailClassName(history.state)}`}>{history.detail}</p>
        )}
        {history.message?.body == null ? null : (
          // Unclamped here: the server caps a reader-visible body at 2000 characters.
          <p className="text-[14px] break-words whitespace-pre-wrap text-muted">{history.message.body}</p>
        )}
      </div>

      {shows ? (
        <section aria-labelledby="probe-latency-heading" className="flex flex-col gap-3">
          <h2 id="probe-latency-heading" className={SECTION_LABEL}>
            Latency
          </h2>
          {/* aria-pressed is right HERE, unlike the dashboard's Arrange button: the buttons'
              names are stable ("7 days") and only which one is pressed changes. */}
          <div role="group" aria-label="Range" className="flex flex-wrap gap-2">
            {PROBE_RANGES.map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={option === range}
                onClick={() => {
                  // `replace`: the range is a view setting, not a navigation, so a reader who flips
                  // between ranges three times still gets back to the dashboard with one Back press.
                  setSearchParams(option === '24h' ? {} : { range: option }, { replace: true })
                }}
                className={`${RANGE_BUTTON} ${option === range ? 'border-line-strong text-text' : 'border-line text-muted'}`}
              >
                {PROBE_RANGE_LABEL[option]}
              </button>
            ))}
          </div>
          <div className={`${PANEL} p-4`}>
            <LatencyChart
              buckets={history.latency}
              bucketSeconds={history.bucketSeconds}
              rangeLabel={PROBE_RANGE_LABEL[range]}
              state={history.state}
            />
          </div>
          <p className="mono text-[13px] text-muted">
            {formatUptime(history.rangeUptimePercent)} uptime over the last {PROBE_RANGE_LABEL[range]} ·{' '}
            {totalPolls} polls
          </p>
        </section>
      ) : null}

      <section aria-labelledby="probe-polls-heading" className="flex flex-col gap-3">
        <h2 id="probe-polls-heading" className={SECTION_LABEL}>
          Recent polls
        </h2>
        {history.recentObservations.length === 0 ? (
          <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
            <p className="text-[13.5px] text-muted">No polls yet.</p>
          </div>
        ) : (
          <div className={`${PANEL} overflow-x-auto`}>
            <table aria-label="Recent polls" className="w-full border-collapse text-left">
              <thead className={TABLE_HEAD}>
                <tr className="border-b border-line">
                  <th scope="col" className={TH}>
                    When
                  </th>
                  <th scope="col" className={TH}>
                    Result
                  </th>
                  {shows ? (
                    <th scope="col" className={`${TH} text-right`}>
                      Latency
                    </th>
                  ) : null}
                  <th scope="col" className={TH}>
                    Detail
                  </th>
                </tr>
              </thead>
              <tbody>
                {history.recentObservations.map((poll) => (
                  <tr key={poll.observedAt} className="border-b border-line last:border-b-0">
                    <td className={`${TD} mono text-[13px] whitespace-nowrap text-muted`}>
                      {formatObservedAt(poll.observedAt)}
                    </td>
                    <td className={`${TD} text-[14px] ${poll.succeeded ? 'text-muted' : 'font-medium text-down'}`}>
                      {poll.succeeded ? 'OK' : 'Failed'}
                    </td>
                    {shows ? (
                      <td className={`${TD} mono text-right text-[13px]`}>
                        {poll.latencyMs === null ? '—' : formatLatency(poll.latencyMs)}
                      </td>
                    ) : null}
                    <td className={`${TD} text-[14px] text-muted`}>{poll.detail ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {isAdministrator ? <ProbeConfiguration id={id} /> : null}
    </>
  )
}

/**
 * The administrator's configuration block. A component of its own so that the two queries it
 * needs mount only for an administrator: `useProbeGroups` has no `enabled` switch, and a reader
 * must not make either call (D1).
 *
 * Never renders anything that could be a secret: a credential is the words "stored" or "not
 * set", and the API cannot send the secret anyway.
 */
function ProbeConfiguration({ id }: { id: string }) {
  const { data: probe, isError } = useProbe(id, { enabled: true })
  const { data: groups } = useProbeGroups()

  if (isError) {
    return (
      <section aria-labelledby="probe-configuration-heading" className="flex flex-col gap-3">
        <h2 id="probe-configuration-heading" className={SECTION_LABEL}>
          Configuration
        </h2>
        <div className={`${PANEL} px-4 py-3.5`}>
          <p className="text-[13.5px] text-muted">The configuration could not be loaded.</p>
        </div>
      </section>
    )
  }

  if (probe === undefined) {
    return null
  }

  const http = probe.kind === 'http' ? probe.http : null
  const groupNames = (groups ?? []).filter((group) => probe.groupIds.includes(group.id)).map((group) => group.name)

  const rows: { label: string; value: string; mono?: boolean }[] = [
    { label: 'Kind', value: PROBE_KIND_LABEL[probe.kind] },
    { label: probe.kind === 'message' ? 'Reporter' : 'Host', value: probe.host, mono: true },
  ]

  if (http !== null) {
    rows.push(
      { label: 'URL', value: `${http.useHttps ? 'https' : 'http'}://${probe.host}/${http.path}`, mono: true },
      { label: 'Method', value: http.method === 'get' ? 'GET' : 'HEAD' },
    )
  }

  rows.push(
    { label: 'Poll interval', value: `Every ${formatDuration(probe.pollIntervalSeconds)}` },
    {
      label: 'Failure threshold',
      value: probe.failureThreshold === 1 ? '1 poll' : `${String(probe.failureThreshold)} consecutive polls`,
    },
  )

  if (http !== null) {
    rows.push({ label: 'Timeout', value: `${String(http.timeoutSeconds)} s` })

    rows.push({
      label: 'Expected status',
      value:
        http.expectedStatusCode === null
          ? 'Any 2xx'
          : http.expectedStatusCodeNegate
            ? `Anything but ${String(http.expectedStatusCode)}`
            : String(http.expectedStatusCode),
    })

    if (http.expectedBodyText !== null && http.expectedBodyText !== '') {
      rows.push({
        label: 'Expected body',
        value: http.expectedBodyTextNegate
          ? `Does not contain "${http.expectedBodyText}"`
          : `Contains "${http.expectedBodyText}"`,
      })
    }

    if (http.useHttps) {
      rows.push({ label: 'TLS certificate', value: http.ignoreCertificateErrors ? 'Not validated' : 'Validated' })
    }

    rows.push({
      label: 'Credential',
      value:
        http.credential.type === 'bearer'
          ? http.credential.hasSecret
            ? 'Bearer token (stored)'
            : 'Bearer token (not set)'
          : http.credential.type === 'basic'
            ? `Basic, as ${http.credential.username ?? ''}`
            : 'None',
    })
  }

  rows.push(
    { label: 'Groups', value: groupNames.length === 0 ? 'None' : groupNames.join(', ') },
    { label: 'Paused', value: probe.isPaused ? 'Yes' : 'No' },
  )

  return (
    <section aria-labelledby="probe-configuration-heading" className="flex flex-col gap-3">
      <h2 id="probe-configuration-heading" className={SECTION_LABEL}>
        Configuration
      </h2>
      <dl className={`${PANEL} grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 px-4 py-3.5 text-[14px]`}>
        {rows.map((row) => (
          <div key={row.label} className="contents">
            <dt className="text-muted">{row.label}</dt>
            {/* `min-w-0` + `wrap-anywhere` rather than `break-words`: `overflow-wrap: break-word` does
                not lower an item's min-content, so in a `1fr` grid track an unbroken URL can hold the
                column open and push a phone sideways in a browser that honours that; `anywhere` does
                lower it. (Chromium at 412px wrapped it either way — see e2e/probe-page.spec.ts.) */}
            <dd className={row.mono ? 'mono min-w-0 wrap-anywhere' : 'min-w-0 wrap-anywhere'}>{row.value}</dd>
          </div>
        ))}
      </dl>
      <p className="text-[13.5px] text-muted">
        <RouterLink to="/admin/probes" className={TEXT_LINK}>
          Edit under Admin → Probes
        </RouterLink>
      </p>
    </section>
  )
}
