import { useEffect, useState, type ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import { Link as RouterLink } from 'react-router'

import { CollapsibleSection } from '@/components/collapsible-section'
import { Sparkline } from '@/components/sparkline'
import { StatusChip } from '@/components/status-chip'
import { ApiError } from '@/lib/api'
import { useCollapsedSections } from '@/lib/collapsed-sections'
import { formatUptime } from '@/lib/format-uptime'
import { useLinks } from '@/lib/links'
import { usePublishedPages } from '@/lib/pages'
import { orderSections, useSectionOrder } from '@/lib/section-order'
import {
  dashboardSections,
  formatCheckedAt,
  messageChipWord,
  plotsLatency,
  summariseProbeStates,
  useStatus,
  type DashboardSection,
  type ProbeState,
} from '@/lib/status'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'
import {
  formatForecastDay,
  unitSymbol,
  useWeather,
  weatherConditionIcon,
  WEATHER_CONDITION_LABEL,
  windUnit,
  type Weather,
} from '@/lib/weather'

/**
 * Below this width each section's rows are sorted by severity instead of the administrator's
 * own order (plan 002's Decision 10 / `dashboardSections`'s `phone` option) — a family
 * scanning a narrow screen sees the worst news first. Not a design-system breakpoint (plan
 * 012 owns those); just wide enough to cover the Pixel-class phones `docs/design-brief.md`
 * names as the primary reader device, narrow enough to leave the desktop e2e project alone.
 */
const PHONE_MEDIA_QUERY = '(max-width: 640px)'

/** True when `matchMedia` is unavailable — the jsdom test environment does not implement it. */
function supportsMatchMedia(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
}

function useIsPhoneViewport(): boolean {
  const [isPhone, setIsPhone] = useState(() => (supportsMatchMedia() ? window.matchMedia(PHONE_MEDIA_QUERY).matches : false))

  useEffect(() => {
    if (!supportsMatchMedia()) {
      return
    }

    const query = window.matchMedia(PHONE_MEDIA_QUERY)
    const onChange = () => setIsPhone(query.matches)

    query.addEventListener('change', onChange)

    return () => query.removeEventListener('change', onChange)
  }, [])

  return isPhone
}

/** The row tint / paused hatch utilities from index.css, keyed by state (docs/design-
 * brief.md's Status chip component rule — "Row" column of the state table). */
function rowStateClassName(state: ProbeState): string {
  switch (state) {
    case 'down':
      return 'row-tint-down'
    case 'unstable':
      return 'row-tint-unstable'
    case 'paused':
      return 'row-paused'
    default:
      return ''
  }
}

/** The detail cell's text colour — `unstable`/`down` at 500 weight, `muted` otherwise
 * (docs/design-brief.md's Status chip component rule). */
function detailClassName(state: ProbeState): string {
  if (state === 'down') {
    return 'font-medium text-down'
  }
  if (state === 'unstable') {
    return 'font-medium text-unstable'
  }
  return 'text-muted'
}


const PANEL = 'rounded-md border border-line bg-surface'

/**
 * The page-level controls beside the <h1>. `h-10` is the 40px tap-target floor `e2e/helpers.ts`'s
 * `expectTappable` enforces over every <button> on `/` — `e2e/refresh.spec.ts` runs it on this very
 * page. Not cosmetic; do not shrink it. The `text-muted` / `border-line` pair is the one the stat
 * strip beside it already uses, which is what keeps `e2e/contrast.spec.ts` (axe's `color-contrast`
 * over this page, both schemes) green without a new token.
 */
const HEADER_BUTTON =
  'inline-flex h-10 shrink-0 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-muted hover:border-line-strong hover:text-text disabled:pointer-events-none disabled:opacity-50'

/**
 * One dashboard section, whatever it holds — a probe group, the ungrouped rest, Links, Pages,
 * Weather. Everything the page renders goes through this list so that ordering
 * (`lib/section-order.ts`) has one flat thing to permute, rather than four hard-coded JSX blocks in
 * a fixed sequence.
 *
 * An array, not a map keyed by id: the ordering algebra consumes a list, nothing here ever wants one
 * section by id (the only lookups are "what is above me" and "what is below me", which are
 * `ordered[index - 1]` and `ordered[index + 1]`), and the conditional Pages section is an array
 * spread rather than a `present` flag every consumer would have to remember to honour.
 *
 * Declared here and not in `lib/status.ts` because it holds a `ReactNode`, and that module is
 * React-free.
 */
interface SectionSlot {
  id: string
  headingId: string
  /** What the <h2> says. May carry a suffix — see `label`. */
  heading: string
  /** The stable name the move buttons are addressed by. Differs from `heading` for Weather only. */
  label: string
  /** Shown beside the heading while collapsed. Probe sections only. */
  summary?: string
  body: ReactNode
}

/**
 * The probe table, or the "No probes yet" onboarding panel — unchanged from plan 002, lifted into a
 * function so the slot list below stays readable. A plain function and not a component: there is no
 * new component identity for React to reconcile across a reorder.
 */
function probeSectionBody(section: DashboardSection, now: Date): ReactNode {
  if (section.probes.length === 0) {
    return (
      <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
        <p className="text-[13.5px] text-muted">
          No probes yet. An administrator adds them under{' '}
          <RouterLink to="/admin/probes" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
            Admin → Probes
          </RouterLink>
          .
        </p>
      </div>
    )
  }

  return (
    <div className={`${PANEL} overflow-x-auto`}>
      <table className="w-full min-w-[640px] border-collapse text-left sm:min-w-0">
        <thead>
          <tr className="text-[11.5px] font-semibold tracking-[0.08em] text-muted uppercase">
            <th scope="col" className="w-[132px] px-4 py-2 font-semibold">
              Status
            </th>
            <th scope="col" className="px-4 py-2 font-semibold">
              Service
            </th>
            <th scope="col" className="px-4 py-2 font-semibold">
              Detail
            </th>
            <th scope="col" className="w-24 px-4 py-2 text-right font-semibold">
              Uptime
            </th>
            <th scope="col" className="w-[92px] px-4 py-2 font-semibold">
              30 days
            </th>
            <th scope="col" className="w-[120px] px-4 py-2 font-semibold">
              Checked
            </th>
          </tr>
        </thead>
        <tbody>
          {section.probes.map((probe) => (
            <tr key={probe.id} className={`min-h-12 border-t border-line ${rowStateClassName(probe.state)}`}>
              <td className="px-4 py-3">
                {/*
                  A message probe borrows the chip's glyphs and colours under the vocabulary
                  docs/design-brief.md set aside for backup outcomes — Succeeded / Warning / Failed
                  — via the `word` prop that component has been carrying for exactly this since
                  plan 012. The word comes from what the reporter claimed, not from the colour: a
                  reporter that claimed nothing has "Reported" to say for itself, not "Succeeded".
                */}
                <StatusChip state={probe.state} word={messageChipWord(probe)} />
              </td>
              {/*
                The name is a link to the probe's own page (plan 023, D9). Its text is exactly the
                name, so every unit and e2e row/name query is unaffected, and the colour pair is the
                one Links already uses, so contrast is unaffected too.
              */}
              <td className="px-4 py-3 text-[15px] font-semibold">
                <RouterLink
                  to={`/probes/${probe.id}`}
                  className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
                >
                  {probe.name}
                </RouterLink>
              </td>
              <td className={`px-4 py-3 text-[14px] ${detailClassName(probe.state)}`}>
                {probe.detail ?? ''}
                {/*
                  The reporter's own words, and only when its reporter is reader-visible — the
                  server sends null otherwise and caps what it does send at 2000 characters (plan
                  021, Decision 11). No colour of its own: it inherits the cell's, so this adds no
                  new text-on-tint pair for e2e/contrast.spec.ts to police. Clamped to three lines
                  because even 2000 characters would make one row taller than the panel.
                */}
                {probe.message?.body != null ? (
                  <span className="mt-1 block line-clamp-3 break-words whitespace-pre-wrap text-[13px]">
                    {probe.message.body}
                  </span>
                ) : null}
              </td>
              <td className="mono px-4 py-3 text-right text-[14px]">{formatUptime(probe.uptimePercent)}</td>
              <td className="px-4 py-3">
                {plotsLatency(probe.kind) ? <Sparkline samples={probe.sparkline} state={probe.state} /> : null}
              </td>
              <td className="px-4 py-3 text-[13px] text-muted">{formatCheckedAt(probe.lastCheckedAt, now)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** The weather panel and its four states: not configured, temporarily unavailable, loaded, loading. */
function weatherSectionBody(
  weather: Weather | null | undefined,
  isWeatherError: boolean,
  weatherError: unknown,
): ReactNode {
  if (weather === null) {
    return (
      <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
        <p className="text-[13.5px] text-muted">
          No weather location yet — an administrator sets it under Admin →{' '}
          <RouterLink to="/admin/weather" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
            Weather
          </RouterLink>
        </p>
      </div>
    )
  }

  if (isWeatherError && weatherError instanceof ApiError && weatherError.status === 503) {
    return (
      <div className={`${PANEL} px-4 py-3.5`}>
        <p className="text-[13.5px] text-muted">Weather is temporarily unavailable.</p>
      </div>
    )
  }

  if (!weather) {
    return null
  }

  const CurrentIcon = weatherConditionIcon(weather.current.condition)

  return (
    <div className={PANEL}>
      {/* One link wraps the data and stops short of the attribution below it: nesting the
          Open-Meteo anchor inside this one would be invalid HTML, and the credit has to stay
          independently reachable. The accessible name says where it goes, not what it shows. */}
      <RouterLink
        to="/weather"
        aria-label={weather.place ? `Weather for ${weather.place}, full forecast` : 'Weather, full forecast'}
        className="block rounded-md p-4 no-underline hover:bg-bg"
      >
        <div className="flex items-start gap-3.5 pb-3">
          <CurrentIcon aria-hidden="true" strokeWidth={1.5} className="size-10 shrink-0 text-muted" />
          <div className="flex flex-col gap-0.5">
            <p className="flex flex-wrap items-baseline gap-x-3">
              <span className="mono text-[28px] leading-none font-medium sm:text-[30px]">
                {Math.round(weather.current.temperature)}
                {unitSymbol(weather.units)}
              </span>
              {/* Today's extremes, deliberately without the unit symbol: the current
                  temperature carries it three characters away. The three forecast rows below
                  keep theirs, because the unit and vitest suites match those exact strings
                  and renaming them is not this change's business (plan 020, D11). */}
              <span className="mono text-[17px] leading-none font-medium text-muted">
                {Math.round(weather.today.high)}° / {Math.round(weather.today.low)}°
              </span>
            </p>
            <p className="text-[14px] text-muted">
              {WEATHER_CONDITION_LABEL[weather.current.condition]} · Wind{' '}
              {Math.round(weather.current.windSpeed)} {windUnit(weather.units)} · Feels like{' '}
              {Math.round(weather.current.apparentTemperature)}
              {unitSymbol(weather.units)}
            </p>
          </div>
          <ChevronRight aria-hidden="true" strokeWidth={2} className="ml-auto size-[18px] shrink-0 text-muted" />
        </div>
        <ul className="divide-y divide-line">
          {/* The widget shows three days, not all seven: the forecast grew with plan 020 and
              this panel is a glance, not the page behind it. */}
          {weather.forecast.slice(0, 3).map((day) => {
            const DayIcon = weatherConditionIcon(day.condition)
            return (
              <li key={day.date} className="flex items-center gap-2.5 py-2 text-[14px]">
                <span className="w-10 text-muted">{formatForecastDay(day.date)}</span>
                <DayIcon aria-hidden="true" strokeWidth={1.75} className="size-5 shrink-0 text-muted" />
                <span>{WEATHER_CONDITION_LABEL[day.condition]}</span>
                <span className="mono ml-auto font-medium">
                  {Math.round(day.high)}
                  {unitSymbol(weather.units)}/{Math.round(day.low)}
                  {unitSymbol(weather.units)}
                </span>
              </li>
            )
          })}
        </ul>
      </RouterLink>
      <p className="px-4 pb-3.5 text-[12.5px] text-muted">
        Weather data by{' '}
        <a
          href="https://open-meteo.com/"
          target="_blank"
          rel="noopener noreferrer"
          className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
        >
          Open-Meteo.com
        </a>
      </p>
    </div>
  )
}

/**
 * The family's page: a stat strip, then every section in the order this browser's reader put them
 * in — one per probe group, the ungrouped rest when there is one, Links, Pages when anything is
 * published, and Weather. `dashboardSections` decides the probe split and the labels;
 * `lib/section-order.ts` decides the sequence and remembers it.
 */
export function DashboardPage() {
  useDocumentTitle(pageTitle('Dashboard'))

  const status = useStatus()
  const isPhone = useIsPhoneViewport()
  const sections = dashboardSections(status.data, { phone: isPhone })
  const totals = status.data?.totals
  const now = new Date()
  // Five minutes, not the status board's thirty seconds: links and pages change only when an
  // administrator edits them. The banner's Refresh button covers the impatient case.
  const { data: links = [] } = useLinks({ refetchInterval: 5 * 60 * 1000, refetchOnWindowFocus: true })
  const { data: pages = [] } = usePublishedPages({ refetchInterval: 5 * 60 * 1000, refetchOnWindowFocus: true })
  const { data: weather, isError: isWeatherError, error: weatherError } = useWeather()

  const { collapsed, toggle } = useCollapsedSections()
  const { order, swap, reset } = useSectionOrder()

  // Arrange mode is deliberately NOT persisted: it is a mode you are in, not a preference you hold.
  // A browser that reopened the dashboard mid-arrangement would greet a reader who only wanted to
  // know whether the NAS is up with two extra controls on every section.
  const [isArranging, setIsArranging] = useState(false)

  // Every id that CAN be a section, in natural order — not only what is on screen right now.
  // `pages` vanishes when nothing is published and `ungrouped` vanishes when every probe is grouped
  // (plan 019), and neither pruning (lib/collapsed-sections.ts) nor ordering (lib/section-order.ts)
  // may forget a reader's choice because a section is temporarily absent.
  //
  // Built from `status.data.groups` with 'ungrouped' as a LITERAL, deliberately not from `sections`:
  // `sections` no longer contains the ungrouped section when it is empty, so deriving this from it
  // would drop 'ungrouped' from the prune list and wipe that section's remembered collapse and
  // order slot on the very next press of any other section's control. `null` until /status has
  // answered — see lib/collapsed-sections.ts for why pruning early is destructive.
  const knownSectionIds =
    status.data === undefined
      ? null
      : [...status.data.groups.map((group) => group.id), 'ungrouped', 'links', 'pages', 'weather']

  const slots: SectionSlot[] = [
    ...sections.map((section) => ({
      id: section.id,
      headingId: section.headingId,
      heading: section.heading,
      label: section.heading,
      summary: summariseProbeStates(section.probes),
      body: probeSectionBody(section, now),
    })),
    {
      id: 'links',
      headingId: 'links-heading',
      heading: 'Links',
      label: 'Links',
      body:
        links.length === 0 ? (
          <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
            <p className="text-[13.5px] text-muted">
              No links yet. An administrator adds them under Admin →{' '}
              <RouterLink to="/admin/links" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
                Links
              </RouterLink>
            </p>
          </div>
        ) : (
          <ul className={`${PANEL} divide-y divide-line px-4`}>
            {links.map((link) => (
              <li key={link.id} className="flex flex-col gap-1 py-2.5 sm:flex-row sm:items-baseline sm:gap-3">
                <a
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={`${link.title} (opens in a new tab)`}
                  className="text-[15px] font-semibold text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
                >
                  {link.title}
                </a>
                {link.description ? <span className="text-[14px] text-muted">{link.description}</span> : null}
              </li>
            ))}
          </ul>
        ),
    },
    // Absent entirely when nothing is published, exactly as before — which is why 'pages' stays in
    // `knownSectionIds` above, so its slot in the stored order survives the absence.
    ...(pages.length > 0
      ? [
          {
            id: 'pages',
            headingId: 'pages-heading',
            heading: 'Pages',
            label: 'Pages',
            body: (
              <ul className={`${PANEL} divide-y divide-line px-4`}>
                {pages.map((page) => (
                  <li key={page.slug} className="py-2.5">
                    <RouterLink
                      to={`/pages/${page.slug}`}
                      className="text-[15px] font-semibold text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
                    >
                      {page.title}
                    </RouterLink>
                  </li>
                ))}
              </ul>
            ),
          },
        ]
      : []),
    {
      id: 'weather',
      headingId: 'weather-heading',
      heading: `Weather${weather?.place ? ` · ${weather.place}` : ''}`,
      // NOT the heading: that carries the configured place, which is household data.
      label: 'Weather',
      body: weatherSectionBody(weather, isWeatherError, weatherError),
    },
  ]

  const ordered = orderSections(slots, order)

  return (
    <>
      <div className="flex flex-col gap-3 border-b border-line-strong pb-4 sm:flex-row sm:items-baseline sm:justify-between sm:gap-8">
        {/* The <h1> keeps its own text and nothing else — e2e/layout.spec.ts and e2e/admin.spec.ts
            both match `heading, level: 1, name: 'Dashboard'` exactly, so these buttons are its
            siblings, never its children. flex-wrap because "Dashboard" plus two 40px controls is
            close to a Pixel 7's width, and e2e/layout.spec.ts measures the document. */}
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">Dashboard</h1>
          {/* The accessible name states the ACTION, not the state — the reasoning written down in
              components/theme-toggle.tsx. Swapping the text to "Done" is the whole announcement, so
              no aria-pressed: a stable name plus aria-pressed would make every locator in the unit
              and Playwright suites depend on knowing which mode the page happens to be in. */}
          <button type="button" onClick={() => setIsArranging(!isArranging)} className={HEADER_BUTTON}>
            {isArranging ? 'Done' : 'Arrange'}
          </button>
          {/* One page-level Reset, not one per section: the action clears a single key, and N
              buttons sharing the accessible name "Reset order" is an instant strict-mode
              ambiguity in Playwright. */}
          {isArranging ? (
            <button type="button" onClick={reset} disabled={order.length === 0} className={HEADER_BUTTON}>
              Reset order
            </button>
          ) : null}
        </div>
        {totals ? (
          // Deliberately flat text, no per-value <span> — dashboard-page.test.tsx's
          // `getByText(/1 up · 0 unstable · …/)` matches only a node's DIRECT text-node
          // children (testing-library's getNodeText), not text nested inside child
          // elements, so wrapping the numbers to colour them individually would make no
          // element's own text ever equal the full string again. Decision 7 (the test
          // suite is the contract) wins over the brief's "unstable and down in their
          // status colours" here; the mono treatment applies to the line as a whole.
          <p className="mono text-[13px] text-muted sm:text-sm">
            {totals.up} up · {totals.unstable} unstable · {totals.down} down · {totals.paused} paused ·{' '}
            {formatUptime(totals.uptimePercent)} uptime, 30 days
          </p>
        ) : null}
      </div>
      {/* A direct child of the fragment, NOT wrapped in a <div>: app-shell.tsx makes <main> a
          `flex flex-col gap-8` and these sections are its own flex children, so a wrapper would
          collapse every gap between sections into one. */}
      {ordered.map((slot, index) => {
        // The neighbours are the sections VISIBLE above and below, never the neighbouring stored
        // ids — a reader pressing "up" means "above what I can see".
        const above = ordered[index - 1]
        const below = ordered[index + 1]

        return (
          <CollapsibleSection
            key={slot.id}
            headingId={slot.headingId}
            heading={slot.heading}
            label={slot.label}
            collapsed={collapsed.has(slot.id)}
            onToggle={() => toggle(slot.id, knownSectionIds)}
            summary={slot.summary}
            arrange={
              isArranging
                ? {
                    canMoveUp: above !== undefined,
                    canMoveDown: below !== undefined,
                    onMoveUp: () => {
                      if (above !== undefined) {
                        swap(slot.id, above.id, knownSectionIds)
                      }
                    },
                    onMoveDown: () => {
                      if (below !== undefined) {
                        swap(slot.id, below.id, knownSectionIds)
                      }
                    },
                  }
                : undefined
            }
          >
            {slot.body}
          </CollapsibleSection>
        )
      })}
    </>
  )
}
