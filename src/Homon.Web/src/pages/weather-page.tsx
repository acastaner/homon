import { useState, type ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'

import { ApiError } from '@/lib/api'
import { pageTitle, useDocumentTitle } from '@/lib/use-document-title'
import {
  NO_VALUE,
  WEATHER_CONDITION_LABEL,
  WEATHER_WARNING_KIND_LABEL,
  WEATHER_WARNING_SEVERITY_WORD,
  formatForecastDay,
  precipitationUnit,
  unitSymbol,
  warningSentence,
  weatherConditionIcon,
  weatherWarningIcon,
  windUnit,
  useWeather,
  type Weather,
  type WeatherDay,
  type WeatherHour,
  type WeatherUnitsValue,
  type WeatherWarning,
  type WeatherWarningSeverity,
} from '@/lib/weather'

// The plan-012 visual patterns, copied rather than imported — the design pass sanctioned this
// duplication for a shared *visual* pattern that is not yet a shared component (Slice E:
// "extract it once a third instance exists").
const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const PANEL = 'rounded-md border border-line bg-surface'
const SECTION_LABEL = 'text-[12px] font-semibold tracking-[0.12em] text-muted uppercase'
const TABLE_HEAD = 'text-[11.5px] font-semibold tracking-[0.08em] text-muted uppercase'
const TH = 'px-4 py-2 font-semibold'
const TD = 'px-4 py-3'

/** How many hourly rows are shown before the reader asks for more, and the step each click adds. */
const HOURS_PER_STEP = 6

/**
 * The full forecast for the household's location, reached from the dashboard's Weather widget.
 *
 * It reads the same `useWeather()` query the dashboard already warmed, so arriving here costs
 * no request and shows no spinner. Its four states are the widget's four states, in the same
 * order and with the same sentences — a reader who sees "temporarily unavailable" on the
 * dashboard must not get something different here.
 */
export function WeatherPage() {
  useDocumentTitle(pageTitle('Weather'))

  const { data: weather, isError, error } = useWeather()

  if (weather === null) {
    return (
      <>
        <h1 className={PAGE_H1}>Weather</h1>
        <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
          <p className="text-[13.5px] text-muted">
            No weather location yet — an administrator sets it under Admin →{' '}
            <RouterLink
              to="/admin/weather"
              className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
            >
              Weather
            </RouterLink>
          </p>
        </div>
      </>
    )
  }

  if (isError && error instanceof ApiError && error.status === 503) {
    return (
      <>
        <h1 className={PAGE_H1}>Weather</h1>
        <div className={`${PANEL} px-4 py-3.5`}>
          <p className="text-[13.5px] text-muted">Weather is temporarily unavailable.</p>
        </div>
      </>
    )
  }

  if (!weather) {
    // Loading renders nothing, as every other page in this application does.
    return null
  }

  return <LoadedWeather weather={weather} />
}

function LoadedWeather({ weather }: { weather: Weather }) {
  const { units } = weather

  return (
    <>
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 border-b border-line-strong pb-4">
        {/* The place stays out of the h1: it is household data, and the e2e specs match the
            heading's whole accessible name. Same reasoning as the dashboard's section label. */}
        <h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">Weather</h1>
        <p className="text-[14px] text-muted">
          {weather.place ? `${weather.place} · ` : ''}
          {formatForecastDay(weather.today.date, { weekday: 'long', day: 'numeric', month: 'long' })}
        </p>
      </div>

      <WarningBanners warnings={weather.warnings} units={units} />
      <TodaySection weather={weather} />
      <HourlySection hours={weather.hourly} warnings={weather.warnings} units={units} />
      <DailySection days={weather.forecast} warnings={weather.warnings} units={units} />

      <p className="text-[12.5px] text-muted">
        Forecast fetched {new Date(weather.fetchedAt).toLocaleTimeString(undefined, { timeStyle: 'short' })}
        {weather.stale ? ' — a refresh failed, so this may be out of date' : ''} · Weather data by{' '}
        <a
          href="https://open-meteo.com/"
          target="_blank"
          rel="noopener noreferrer"
          className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
        >
          Open-Meteo.com
        </a>
      </p>
    </>
  )
}

/**
 * The advisories, as a named list rather than `role="alert"` apiece.
 *
 * They are present on first paint, and `role="alert"` is assertive — three of them would be
 * announced over each other. This application reserves `role="alert"` for errors and
 * `role="status"` for the session check (plan 020, D15).
 */
function WarningBanners({ warnings, units }: { warnings: WeatherWarning[]; units: WeatherUnitsValue }) {
  if (warnings.length === 0) {
    return null
  }

  return (
    <ul aria-label="Weather warnings" className="flex list-none flex-col gap-2.5">
      {warnings.map((warning) => {
        const Icon = weatherWarningIcon(warning.kind)

        return (
          <li
            key={`${warning.kind}-${warning.date}-${warning.fromTime ?? 'day'}`}
            className={`${PANEL} ${severityTint(warning.severity)} flex items-start gap-3 px-4 py-3`}
          >
            <Icon aria-hidden="true" strokeWidth={2.25} className={`mt-0.5 size-5 shrink-0 ${severityText(warning.severity)}`} />
            <div className="flex flex-col gap-0.5">
              <p className="text-[14.5px] font-semibold">
                {/* Severity is never colour alone: the word is the carrier, the tint is the echo. */}
                <span className={`text-[13px] tracking-[0.02em] ${severityText(warning.severity)}`}>
                  {WEATHER_WARNING_SEVERITY_WORD[warning.severity]}
                </span>{' '}
                · {WEATHER_WARNING_KIND_LABEL[warning.kind]}
              </p>
              <p className={`text-[14px] font-medium ${severityText(warning.severity)}`}>
                {warningSentence(warning, units)}
              </p>
            </div>
          </li>
        )
      })}
    </ul>
  )
}

/** Current conditions with today's extremes, and the four figures a reader asks for next. */
function TodaySection({ weather }: { weather: Weather }) {
  const { current, today, units } = weather
  const Icon = weatherConditionIcon(current.condition)

  return (
    <section aria-labelledby="weather-today" className="flex flex-col gap-2.5">
      <h2 id="weather-today" className={SECTION_LABEL}>
        Today
      </h2>
      <div className={PANEL}>
        <div className="flex items-center gap-4 px-4 py-4">
          <Icon aria-hidden="true" strokeWidth={1.5} className="size-12 shrink-0 text-muted sm:size-14" />
          <div className="flex flex-col gap-1">
            <p className="flex flex-wrap items-baseline gap-x-3">
              <span className="mono text-[36px] leading-none font-medium sm:text-[44px]">
                {Math.round(current.temperature)}
                {unitSymbol(units)}
              </span>
              <span className="mono text-[17px] leading-none font-medium text-muted sm:text-[20px]">
                {Math.round(today.high)}° / {Math.round(today.low)}°
              </span>
            </p>
            <p className="text-[14px] text-muted sm:text-[15px]">
              {WEATHER_CONDITION_LABEL[current.condition]} · high {Math.round(today.high)}
              {unitSymbol(units)}, low {Math.round(today.low)}
              {unitSymbol(units)}
            </p>
          </div>
        </div>
        <dl className="grid grid-cols-2 sm:grid-cols-4">
          <Stat label="Feels like" value={`${Math.round(current.apparentTemperature)}${unitSymbol(units)}`} />
          <Stat label="Wind" value={`${Math.round(current.windSpeed)} ${windUnit(units)}`} />
          <Stat
            label="Rain today"
            value={
              today.precipitationSum == null
                ? NO_VALUE
                : `${round1(today.precipitationSum)} ${precipitationUnit(units)}`
            }
          />
          <Stat
            label="Sunrise / sunset"
            value={today.sunrise && today.sunset ? `${today.sunrise} / ${today.sunset}` : NO_VALUE}
            small
          />
        </dl>
      </div>
    </section>
  )
}

function Stat({ label, value, small = false }: { label: string; value: string; small?: boolean }) {
  return (
    <div className="flex flex-col gap-1 border-t border-line px-4 py-3 [&:nth-child(odd)]:border-r sm:[&:nth-child(odd)]:border-r-0 sm:[&:not(:first-child)]:border-l">
      <dt className="text-[11.5px] font-semibold tracking-[0.08em] text-muted uppercase">{label}</dt>
      <dd className={`mono font-medium ${small ? 'text-[15px] sm:text-[17px]' : 'text-[17px] sm:text-[20px]'}`}>
        {value}
      </dd>
    </div>
  )
}

/**
 * The hour-by-hour table, six rows at a time.
 *
 * The step is plain component state, deliberately not remembered in `localStorage` the way
 * collapsed sections and section order are: how far down a table someone has read is a
 * position within one visit, not a preference (plan 020, D14).
 */
function HourlySection({
  hours,
  warnings,
  units,
}: {
  hours: WeatherHour[]
  warnings: WeatherWarning[]
  units: WeatherUnitsValue
}) {
  const [visible, setVisible] = useState(HOURS_PER_STEP)

  const shown = hours.slice(0, visible)
  const remaining = hours.length - shown.length
  const step = Math.min(HOURS_PER_STEP, remaining)

  return (
    <section aria-labelledby="weather-hours" className="flex flex-col gap-2.5">
      <h2 id="weather-hours" className={SECTION_LABEL}>
        Next hours
      </h2>
      {hours.length === 0 ? (
        <EmptyPanel>No hourly forecast in this answer.</EmptyPanel>
      ) : (
        <div className={`${PANEL} overflow-x-auto`}>
          <table aria-label="Hourly forecast" className="w-full border-collapse text-left">
            <thead>
              <tr className={TABLE_HEAD}>
                <th scope="col" className={`w-[76px] ${TH}`}>
                  Time
                </th>
                <th scope="col" className={TH}>
                  Condition
                </th>
                <th scope="col" className={`w-[72px] ${TH} text-right`}>
                  Temp
                </th>
                {/* Phone drops Feels like and Wind; see plan 020, D13 — the class is on the
                    header *and* every matching cell, or the row shifts a column left. */}
                <th scope="col" className={`hidden w-[104px] sm:table-cell ${TH} text-right`}>
                  Feels like
                </th>
                {/* Wide enough for "12 km/h · gusts 94" on one line: a tinted row that wraps
                    is taller than its neighbours, and the table stops reading as a table. */}
                <th scope="col" className={`hidden w-[200px] sm:table-cell ${TH}`}>
                  Wind
                </th>
                <th scope="col" className={`w-[72px] ${TH} text-right`}>
                  Rain
                </th>
              </tr>
            </thead>
            <tbody>
              {shown.map((hour) => {
                const Icon = weatherConditionIcon(hour.condition)
                const covering = hourWarnings(hour, warnings)
                const severity = worst(covering)
                // Only a *wind* advisory may colour the wind cell — a thunderstorm advisory
                // tinting the row must not turn a calm 12 km/h red.
                const windSeverity = worst(covering.filter((warning) => warning.kind === 'wind'))

                return (
                  <tr key={`${hour.date}-${hour.time}`} className={`min-h-12 border-t border-line ${severityTint(severity)}`}>
                    <td className={`mono ${TD} text-[14px]`}>{hour.time}</td>
                    <td className={`${TD} text-[14px]`}>
                      <span className="inline-flex items-center gap-2.5">
                        <Icon aria-hidden="true" strokeWidth={1.75} className="size-5 shrink-0 text-muted" />
                        {WEATHER_CONDITION_LABEL[hour.condition]}
                      </span>
                    </td>
                    <td className={`mono ${TD} text-right text-[14px]`}>{Math.round(hour.temperature)}°</td>
                    <td className={`mono hidden sm:table-cell ${TD} text-right text-[14px]`}>
                      {Math.round(hour.apparentTemperature)}°
                    </td>
                    <td className={`mono hidden whitespace-nowrap sm:table-cell ${TD} text-[14px] ${severityText(windSeverity)}`}>
                      {/* The advisory is about the gust, so the gust is what the coloured cell
                          has to show — otherwise a reader sees a calm mean wind in red and the
                          banner and the table appear to disagree. */}
                      {Math.round(hour.windSpeed)} {windUnit(units)}
                      {windSeverity && hour.windGusts != null
                        ? ` · gusts ${String(Math.round(hour.windGusts))}`
                        : ''}
                    </td>
                    <td className={`mono ${TD} text-right text-[14px]`}>
                      {hour.precipitationProbability == null ? NO_VALUE : `${hour.precipitationProbability}%`}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          {remaining > 0 ? (
            <button
              type="button"
              onClick={() => setVisible((current) => Math.min(current + HOURS_PER_STEP, hours.length))}
              className="h-12 w-full border-t border-line text-[13.5px] font-semibold text-text hover:bg-bg"
            >
              Show {step} more hour{step === 1 ? '' : 's'} — {shown.length} of {hours.length}
            </button>
          ) : null}
        </div>
      )}
    </section>
  )
}

/** The seven-day table. Phone keeps the day, the condition and the extremes. */
function DailySection({
  days,
  warnings,
  units,
}: {
  days: WeatherDay[]
  warnings: WeatherWarning[]
  units: WeatherUnitsValue
}) {
  return (
    <section aria-labelledby="weather-days" className="flex flex-col gap-2.5">
      <h2 id="weather-days" className={SECTION_LABEL}>
        Next 7 days
      </h2>
      {days.length === 0 ? (
        <EmptyPanel>No daily forecast in this answer.</EmptyPanel>
      ) : (
        <div className={`${PANEL} overflow-x-auto`}>
          <table aria-label="Daily forecast" className="w-full border-collapse text-left">
            <thead>
              <tr className={TABLE_HEAD}>
                <th scope="col" className={`w-[92px] ${TH}`}>
                  Day
                </th>
                <th scope="col" className={TH}>
                  Condition
                </th>
                <th scope="col" className={`w-[104px] ${TH} text-right`}>
                  High / low
                </th>
                {/* Phone drops Rain, Wind and Sunrise / sunset; see plan 020, D13. */}
                <th scope="col" className={`hidden w-[88px] sm:table-cell ${TH} text-right`}>
                  Rain
                </th>
                <th scope="col" className={`hidden w-[128px] sm:table-cell ${TH}`}>
                  Wind
                </th>
                <th scope="col" className={`hidden w-[152px] sm:table-cell ${TH}`}>
                  Sunrise / sunset
                </th>
              </tr>
            </thead>
            <tbody>
              {days.map((day) => {
                const Icon = weatherConditionIcon(day.condition)
                const covering = dayWarnings(day, warnings)
                const severity = worst(covering)
                // Likewise: a heat advisory must not colour the rain figure.
                const rainSeverity = worst(
                  covering.filter((warning) => warning.kind === 'rain' || warning.kind === 'snow'),
                )

                return (
                  <tr key={day.date} className={`min-h-12 border-t border-line ${severityTint(severity)}`}>
                    <td className={`mono ${TD} text-[14px]`}>
                      {formatForecastDay(day.date, { weekday: 'short', day: 'numeric' })}
                    </td>
                    <td className={`${TD} text-[14px]`}>
                      <span className="inline-flex items-center gap-2.5">
                        <Icon aria-hidden="true" strokeWidth={1.75} className="size-5 shrink-0 text-muted" />
                        {WEATHER_CONDITION_LABEL[day.condition]}
                      </span>
                    </td>
                    <td className={`mono ${TD} text-right text-[14px]`}>
                      {Math.round(day.high)}° / {Math.round(day.low)}°
                    </td>
                    <td className={`mono hidden sm:table-cell ${TD} text-right text-[14px] ${severityText(rainSeverity)}`}>
                      {day.precipitationSum == null
                        ? NO_VALUE
                        : `${round1(day.precipitationSum)} ${precipitationUnit(units)}`}
                    </td>
                    <td className={`mono hidden sm:table-cell ${TD} text-[14px]`}>
                      {day.windSpeedMax == null ? NO_VALUE : `${Math.round(day.windSpeedMax)} ${windUnit(units)}`}
                    </td>
                    <td className={`mono hidden sm:table-cell ${TD} text-[13px] text-muted`}>
                      {day.sunrise && day.sunset ? `${day.sunrise} / ${day.sunset}` : NO_VALUE}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function EmptyPanel({ children }: { children: ReactNode }) {
  return (
    <div className={`${PANEL} border-dashed border-line-strong px-4 py-3.5`}>
      <p className="text-[13.5px] text-muted">{children}</p>
    </div>
  )
}

/** The row tint for a severity, reusing the dashboard's own tint classes. */
function severityTint(severity: WeatherWarningSeverity | null): string {
  switch (severity) {
    case 'severe':
      return 'row-tint-down'
    case 'caution':
      return 'row-tint-unstable'
    default:
      return ''
  }
}

/** The text colour for a severity — the status colour at 500 weight, as the design brief sets. */
function severityText(severity: WeatherWarningSeverity | null): string {
  switch (severity) {
    case 'severe':
      return 'text-down'
    case 'caution':
      return 'text-unstable'
    default:
      return ''
  }
}

/**
 * The advisories covering this hour, so the row carries the same tint as the banner — and so a
 * cell can ask whether the advisory colouring it is about *its own* figure.
 *
 * The clock comparison is a plain string compare, which is correct for zero-padded `"HH:mm"`
 * and is why that format is the wire contract.
 */
function hourWarnings(hour: WeatherHour, warnings: WeatherWarning[]): WeatherWarning[] {
  return warnings.filter(
    (warning) =>
      warning.date === hour.date &&
      warning.fromTime != null &&
      warning.toTime != null &&
      warning.fromTime <= hour.time &&
      hour.time <= warning.toTime,
  )
}

/** The whole-day advisories for this day. Timed advisories tint their hours, not the day. */
function dayWarnings(day: WeatherDay, warnings: WeatherWarning[]): WeatherWarning[] {
  return warnings.filter((warning) => warning.date === day.date && warning.fromTime == null)
}

function worst(warnings: WeatherWarning[]): WeatherWarningSeverity | null {
  if (warnings.some((warning) => warning.severity === 'severe')) {
    return 'severe'
  }

  return warnings.length > 0 ? 'caution' : null
}

/** One decimal place, with no trailing zero — `0.1`, `28`, `1.6`. */
function round1(value: number): number {
  return Math.round(value * 10) / 10
}
