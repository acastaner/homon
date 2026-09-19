import { useEffect, useState } from 'react'
import { Link as RouterLink } from 'react-router'

import { CollapsibleSection } from '@/components/collapsible-section'
import { Sparkline } from '@/components/sparkline'
import { StatusChip } from '@/components/status-chip'
import { ApiError } from '@/lib/api'
import { useCollapsedSections } from '@/lib/collapsed-sections'
import { formatUptime } from '@/lib/format-uptime'
import { useLinks } from '@/lib/links'
import { usePublishedPages } from '@/lib/pages'
import { dashboardSections, formatCheckedAt, summariseProbeStates, useStatus, type ProbeState } from '@/lib/status'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'
import {
  useWeather,
  weatherConditionIcon,
  WEATHER_CONDITION_LABEL,
  type WeatherUnitsValue,
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

function unitSymbol(units: WeatherUnitsValue): string {
  return units === 'imperial' ? '°F' : '°C'
}

function windUnit(units: WeatherUnitsValue): string {
  return units === 'imperial' ? 'mph' : 'km/h'
}

/**
 * Parses `date` (an ISO calendar date, e.g. "2026-09-16") as local calendar components, not
 * `new Date(dateString)` — the latter constructs UTC midnight, which renders as the
 * *previous* day in a negative-offset timezone.
 */
function formatForecastDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString(undefined, { weekday: 'short' })
}

const PANEL = 'rounded-md border border-line bg-surface'

/**
 * The family's page: a stat strip, then one section per non-empty probe group followed by the
 * ungrouped rest — `dashboardSections` decides the split and the labels — then Links, a
 * conditional Pages section, and Weather.
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

  // Everything that can be a section, not only what is on screen right now: `pages` disappears
  // entirely when no page is published, and pruning (lib/collapsed-sections.ts) must not forget
  // a reader's choice just because the Pages section is temporarily absent. `null` until the
  // status query has answered — see that module's comment for why pruning early is destructive.
  const knownSectionIds = status.data === undefined ? null : [...sections.map((section) => section.id), 'links', 'pages', 'weather']

  return (
    <>
      <div className="flex flex-col gap-3 border-b border-line-strong pb-4 sm:flex-row sm:items-baseline sm:justify-between sm:gap-8">
        <h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">Dashboard</h1>
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
      {sections.map((section) => (
        <CollapsibleSection
          key={section.id}
          headingId={section.headingId}
          heading={section.heading}
          collapsed={collapsed.has(section.id)}
          onToggle={() => toggle(section.id, knownSectionIds)}
          summary={summariseProbeStates(section.probes)}
        >
          {section.probes.length === 0 ? (
            <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
              <p className="text-[13.5px] text-muted">
                No probes yet. An administrator adds them under{' '}
                <RouterLink to="/admin/probes" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
                  Admin → Probes
                </RouterLink>
                .
              </p>
            </div>
          ) : (
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
                        <StatusChip state={probe.state} />
                      </td>
                      <td className="px-4 py-3 text-[15px] font-semibold">{probe.name}</td>
                      <td className={`px-4 py-3 text-[14px] ${detailClassName(probe.state)}`}>{probe.detail ?? ''}</td>
                      <td className="mono px-4 py-3 text-right text-[14px]">{formatUptime(probe.uptimePercent)}</td>
                      <td className="px-4 py-3">
                        {probe.kind === 'ping' ? <Sparkline samples={probe.sparkline} state={probe.state} /> : null}
                      </td>
                      <td className="px-4 py-3 text-[13px] text-muted">{formatCheckedAt(probe.lastCheckedAt, now)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CollapsibleSection>
      ))}
      <CollapsibleSection
        headingId="links-heading"
        heading="Links"
        collapsed={collapsed.has('links')}
        onToggle={() => toggle('links', knownSectionIds)}
      >
        {links.length === 0 ? (
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
        )}
      </CollapsibleSection>
      {pages.length > 0 ? (
        <CollapsibleSection
          headingId="pages-heading"
          heading="Pages"
          collapsed={collapsed.has('pages')}
          onToggle={() => toggle('pages', knownSectionIds)}
        >
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
        </CollapsibleSection>
      ) : null}
      <CollapsibleSection
        headingId="weather-heading"
        heading={`Weather${weather?.place ? ` · ${weather.place}` : ''}`}
        collapsed={collapsed.has('weather')}
        onToggle={() => toggle('weather', knownSectionIds)}
      >
        {weather === null ? (
          <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
            <p className="text-[13.5px] text-muted">
              No weather location yet — an administrator sets it under Admin →{' '}
              <RouterLink to="/admin/weather" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
                Weather
              </RouterLink>
            </p>
          </div>
        ) : isWeatherError && weatherError instanceof ApiError && weatherError.status === 503 ? (
          <div className={`${PANEL} px-4 py-3.5`}>
            <p className="text-[13.5px] text-muted">Weather is temporarily unavailable.</p>
          </div>
        ) : weather ? (
          <div className={`${PANEL} p-4`}>
            {(() => {
              const CurrentIcon = weatherConditionIcon(weather.current.condition)
              return (
                <div className="flex items-center gap-3.5 pb-3">
                  <CurrentIcon aria-hidden="true" strokeWidth={1.5} className="size-10 shrink-0 text-muted" />
                  <div className="flex flex-col gap-0.5">
                    <p className="mono text-[28px] leading-none font-medium sm:text-[30px]">
                      {Math.round(weather.current.temperature)}
                      {unitSymbol(weather.units)}
                    </p>
                    <p className="text-[14px] text-muted">
                      {WEATHER_CONDITION_LABEL[weather.current.condition]} · Wind{' '}
                      {Math.round(weather.current.windSpeed)} {windUnit(weather.units)} · Feels like{' '}
                      {Math.round(weather.current.apparentTemperature)}
                      {unitSymbol(weather.units)}
                    </p>
                  </div>
                </div>
              )
            })()}
            <ul className="divide-y divide-line">
              {weather.forecast.map((day) => {
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
            <p className="pt-3 text-[12.5px] text-muted">
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
        ) : null}
      </CollapsibleSection>
    </>
  )
}
