import { useEffect, useState } from 'react'
import { Link as RouterLink } from 'react-router'

import { ApiError } from '@/lib/api'
import { useLinks } from '@/lib/links'
import { usePublishedPages } from '@/lib/pages'
import { dashboardSections, formatCheckedAt, useStatus } from '@/lib/status'
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

/**
 * A one-line duplicate of what plan 012's Decision 6 extracts into `lib/format-uptime.ts` —
 * not a blocker, just the plainest text that is correct until that plan lands.
 */
function formatUptime(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(2)}%`
}

function capitalize(word: string): string {
  return word.length === 0 ? word : word.charAt(0).toUpperCase() + word.slice(1)
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

/**
 * The family's page: a stat strip, then one section per non-empty probe group followed by the
 * ungrouped rest — `dashboardSections` decides the split and the labels. Unstyled — no
 * `className` anywhere, no colour or glyph on the status word; plan 012 owns the look.
 */
export function DashboardPage() {
  useDocumentTitle(pageTitle('Dashboard'))

  const status = useStatus()
  const isPhone = useIsPhoneViewport()
  const sections = dashboardSections(status.data, { phone: isPhone })
  const totals = status.data?.totals
  const now = new Date()
  const { data: links = [] } = useLinks()
  const { data: pages = [] } = usePublishedPages()
  const { data: weather, isError: isWeatherError, error: weatherError } = useWeather()

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
        <section key={section.id} aria-labelledby={section.headingId}>
          <h2 id={section.headingId}>{section.heading}</h2>
          {section.probes.length === 0 ? (
            <p>No probes yet. An administrator adds them under Admin → Probes.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th scope="col">Status</th>
                  <th scope="col">Service</th>
                  <th scope="col">Detail</th>
                  <th scope="col">Uptime</th>
                  <th scope="col">30 days</th>
                  <th scope="col">Checked</th>
                </tr>
              </thead>
              <tbody>
                {section.probes.map((probe) => (
                  <tr key={probe.id}>
                    <td>{capitalize(probe.state)}</td>
                    <td>{probe.name}</td>
                    <td>{probe.detail ?? ''}</td>
                    <td>{formatUptime(probe.uptimePercent)}</td>
                    {/* Left empty for 012's Slice D2 (the sparkline) — the header cell already
                        says "30 days" so that pass only fills this in, not restructures the table. */}
                    <td></td>
                    <td>{formatCheckedAt(probe.lastCheckedAt, now)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      ))}
      <section aria-labelledby="links-heading">
        <h2 id="links-heading">Links</h2>
        {links.length === 0 ? (
          <p>
            No links yet. An administrator adds them under Admin →{' '}
            <RouterLink to="/admin/links">Links</RouterLink>
          </p>
        ) : (
          <ul>
            {links.map((link) => (
              <li key={link.id}>
                <a
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={`${link.title} (opens in a new tab)`}
                >
                  {link.title}
                </a>
                {link.description ? <span> {link.description}</span> : null}
              </li>
            ))}
          </ul>
        )}
      </section>
      {pages.length > 0 ? (
        <section aria-labelledby="pages-heading">
          <h2 id="pages-heading">Pages</h2>
          <ul>
            {pages.map((page) => (
              <li key={page.slug}>
                <RouterLink to={`/pages/${page.slug}`}>{page.title}</RouterLink>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      <section aria-labelledby="weather-heading">
        <h2 id="weather-heading">Weather{weather?.place ? ` · ${weather.place}` : ''}</h2>
        {weather === null ? (
          <p>
            No weather location yet — an administrator sets it under Admin →{' '}
            <RouterLink to="/admin/weather">Weather</RouterLink>
          </p>
        ) : isWeatherError && weatherError instanceof ApiError && weatherError.status === 503 ? (
          <p>Weather is temporarily unavailable.</p>
        ) : weather ? (
          <>
            {(() => {
              const CurrentIcon = weatherConditionIcon(weather.current.condition)
              return (
                <p>
                  <CurrentIcon aria-hidden="true" /> {WEATHER_CONDITION_LABEL[weather.current.condition]}{' '}
                  {Math.round(weather.current.temperature)}
                  {unitSymbol(weather.units)}
                </p>
              )
            })()}
            <p>
              {WEATHER_CONDITION_LABEL[weather.current.condition]} · Wind{' '}
              {Math.round(weather.current.windSpeed)} {windUnit(weather.units)} · Feels like{' '}
              {Math.round(weather.current.apparentTemperature)}
              {unitSymbol(weather.units)}
            </p>
            <ul>
              {weather.forecast.map((day) => {
                const DayIcon = weatherConditionIcon(day.condition)
                return (
                  <li key={day.date}>
                    {formatForecastDay(day.date)} <DayIcon aria-hidden="true" />{' '}
                    {WEATHER_CONDITION_LABEL[day.condition]} {Math.round(day.high)}
                    {unitSymbol(weather.units)}/{Math.round(day.low)}
                    {unitSymbol(weather.units)}
                  </li>
                )
              })}
            </ul>
            <p>
              Weather data by{' '}
              <a href="https://open-meteo.com/" target="_blank" rel="noopener noreferrer">
                Open-Meteo.com
              </a>
            </p>
          </>
        ) : null}
      </section>
    </>
  )
}
