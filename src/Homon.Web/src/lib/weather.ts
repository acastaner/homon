import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Cloud,
  CloudDrizzle,
  CloudFog,
  CloudLightning,
  CloudRain,
  CloudSnow,
  CloudSun,
  HelpCircle,
  Snowflake,
  Sun,
  Thermometer,
  ThermometerSnowflake,
  Wind,
  type LucideIcon,
} from 'lucide-react'

import { apiFetch } from '@/lib/api'

/** Mirrors the WeatherCondition enum in Homon.Domain/Weather/WeatherCondition.cs. */
export type WeatherCondition =
  | 'clear'
  | 'partlyCloudy'
  | 'cloudy'
  | 'fog'
  | 'drizzle'
  | 'rain'
  | 'snow'
  | 'thunderstorm'
  | 'unknown'

/** Mirrors the WeatherUnits enum in Homon.Domain/Weather/WeatherUnits.cs. */
export type WeatherUnitsValue = 'metric' | 'imperial'

/** Mirrors WeatherCurrentResponse in Homon.Api/Endpoints/WeatherEndpoints.cs. */
export interface WeatherCurrent {
  temperature: number
  apparentTemperature: number
  windSpeed: number
  condition: WeatherCondition
  isDay: boolean
}

/**
 * Mirrors WeatherDayResponse in Homon.Api/Endpoints/WeatherEndpoints.cs.
 *
 * `snowfallSum` is **centimetres** under metric while `precipitationSum` is millimetres —
 * Open-Meteo's own asymmetry, not a bug here. Every nullable field is a variable the forecast
 * model may not report, and renders as an em dash.
 */
export interface WeatherDay {
  date: string
  condition: WeatherCondition
  high: number
  low: number
  precipitationSum: number | null
  snowfallSum: number | null
  windSpeedMax: number | null
  windGustsMax: number | null
  /** The location's own clock, `"HH:mm"`. Null where the sun does not rise. */
  sunrise: string | null
  sunset: string | null
}

/**
 * Mirrors WeatherHourResponse in Homon.Api/Endpoints/WeatherEndpoints.cs.
 *
 * `time` is a string on purpose: it is already the location's own clock, and handing it to
 * `new Date()` would re-read it in the *reader's* timezone — the same trap
 * `formatForecastDay` below documents for calendar dates.
 */
export interface WeatherHour {
  date: string
  time: string
  condition: WeatherCondition
  temperature: number
  apparentTemperature: number
  windSpeed: number
  windGusts: number | null
  precipitationProbability: number | null
}

/** Mirrors WeatherWarningKind in Homon.Domain/Weather/WeatherWarning.cs. */
export type WeatherWarningKind = 'wind' | 'thunderstorm' | 'snow' | 'rain' | 'heat' | 'cold'

/** Mirrors WeatherWarningSeverity in Homon.Domain/Weather/WeatherWarning.cs. */
export type WeatherWarningSeverity = 'caution' | 'severe'

/**
 * Mirrors WeatherWarningResponse in Homon.Api/Endpoints/WeatherEndpoints.cs.
 *
 * Derived by Homon from thresholds applied to the forecast — Open-Meteo publishes no warnings
 * endpoint — so nothing rendered from this may claim an official source.
 */
export interface WeatherWarning {
  kind: WeatherWarningKind
  severity: WeatherWarningSeverity
  /** The figure that tripped the threshold, or null for a kind with no figure. */
  value: number | null
  date: string
  /** The location's own clock, `"HH:mm"`, or null for a whole-day advisory. */
  fromTime: string | null
  toTime: string | null
}

/** Mirrors WeatherResponse in Homon.Api/Endpoints/WeatherEndpoints.cs. */
export interface Weather {
  place: string | null
  units: WeatherUnitsValue
  current: WeatherCurrent
  /** Today, including its high and low — which `current` cannot give. */
  today: WeatherDay
  /** The next seven days, starting tomorrow. The dashboard widget shows the first three. */
  forecast: WeatherDay[]
  /** Up to 24 rows, from the current hour at the location. */
  hourly: WeatherHour[]
  /** Up to three advisories for the next 48 hours, severest first. Empty when nothing trips. */
  warnings: WeatherWarning[]
  fetchedAt: string
  stale: boolean
}

/** Mirrors WeatherSettingsResponse in Homon.Api/Endpoints/WeatherEndpoints.cs. */
export interface WeatherSettings {
  latitude: number
  longitude: number
  place: string | null
  units: WeatherUnitsValue
}

/** The admin form's shape — always strings, parsed and validated server-side. */
export interface WeatherSettingsFields {
  latitude: string
  longitude: string
  place: string
  units: WeatherUnitsValue
}

export const WEATHER_QUERY_KEY = ['weather'] as const
export const WEATHER_SETTINGS_QUERY_KEY = ['weather', 'settings'] as const

/** A label for every condition, so the SPA never shows the raw camelCase wire value. */
export const WEATHER_CONDITION_LABEL: Record<WeatherCondition, string> = {
  clear: 'Clear',
  partlyCloudy: 'Partly cloudy',
  cloudy: 'Cloudy',
  fog: 'Fog',
  drizzle: 'Drizzle',
  rain: 'Rain',
  snow: 'Snow',
  thunderstorm: 'Thunderstorm',
  unknown: 'Unknown',
}

const WEATHER_CONDITION_ICON: Record<WeatherCondition, LucideIcon> = {
  clear: Sun,
  partlyCloudy: CloudSun,
  cloudy: Cloud,
  fog: CloudFog,
  drizzle: CloudDrizzle,
  rain: CloudRain,
  snow: CloudSnow,
  thunderstorm: CloudLightning,
  unknown: HelpCircle,
}

/** The Lucide icon for a condition — decorative; the condition word beside it is what carries meaning. */
export function weatherConditionIcon(condition: WeatherCondition): LucideIcon {
  return WEATHER_CONDITION_ICON[condition]
}

/**
 * A label for every advisory kind. The wording is deliberately Homon's own voice and names no
 * authority: Open-Meteo publishes no warnings endpoint, so these are thresholds this
 * application applied to a forecast, never a relayed official warning.
 */
export const WEATHER_WARNING_KIND_LABEL: Record<WeatherWarningKind, string> = {
  wind: 'Gale-force wind',
  thunderstorm: 'Thunderstorm',
  snow: 'Heavy snow',
  rain: 'Heavy rain',
  heat: 'Extreme heat',
  cold: 'Extreme cold',
}

/**
 * The word beside an advisory's glyph. Severity is never colour alone — the design brief's
 * status-chip rule, applied to banners.
 */
export const WEATHER_WARNING_SEVERITY_WORD: Record<WeatherWarningSeverity, string> = {
  caution: 'Caution',
  severe: 'Severe',
}

const WEATHER_WARNING_ICON: Record<WeatherWarningKind, LucideIcon> = {
  wind: Wind,
  thunderstorm: CloudLightning,
  snow: Snowflake,
  rain: CloudRain,
  heat: Thermometer,
  cold: ThermometerSnowflake,
}

/** The Lucide icon for an advisory kind — decorative; the kind's word carries the meaning. */
export function weatherWarningIcon(kind: WeatherWarningKind): LucideIcon {
  return WEATHER_WARNING_ICON[kind]
}

/** The unit an advisory's figure is in, by kind. Snowfall is centimetres under metric. */
function warningValueUnit(kind: WeatherWarningKind, units: WeatherUnitsValue): string {
  const imperial = units === 'imperial'

  switch (kind) {
    case 'wind':
      return imperial ? 'mph' : 'km/h'
    case 'rain':
      return imperial ? 'in' : 'mm'
    case 'snow':
      return imperial ? 'in' : 'cm'
    default:
      return imperial ? '°F' : '°C'
  }
}

/**
 * "today", "tomorrow", or the weekday — how far off `date` is from `reference`.
 *
 * `reference` is a parameter rather than `new Date()` so a test can pin it; both are reduced
 * to local calendar components before being compared, because a difference in hours is not a
 * difference in days.
 */
export function relativeDayLabel(date: string, reference: Date = new Date()): string {
  const [year, month, day] = date.split('-').map(Number)
  const target = new Date(year, month - 1, day)
  const today = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate())

  const days = Math.round((target.getTime() - today.getTime()) / 86_400_000)

  if (days === 0) {
    return 'today'
  }

  if (days === 1) {
    return 'tomorrow'
  }

  return target.toLocaleDateString(undefined, { weekday: 'long' })
}

/**
 * One sentence describing an advisory, in the reader's own terms. Composed here rather than on
 * the server so fixing a word does not need a redeployment — the same reasoning that keeps
 * `WEATHER_CONDITION_LABEL` on this side.
 */
export function warningSentence(
  warning: WeatherWarning,
  units: WeatherUnitsValue,
  reference: Date = new Date(),
): string {
  const when = relativeDayLabel(warning.date, reference)
  const unit = warningValueUnit(warning.kind, units)
  const figure = warning.value == null ? null : `${Math.round(warning.value * 10) / 10} ${unit}`

  const window =
    warning.fromTime && warning.toTime
      ? warning.fromTime === warning.toTime
        ? `at ${warning.fromTime} ${when}`
        : `between ${warning.fromTime} and ${warning.toTime} ${when}`
      : when

  switch (warning.kind) {
    case 'wind':
      return `Gusts to ${figure} expected ${window}.`
    case 'thunderstorm':
      return `Thunderstorms expected ${window}.`
    case 'snow':
      return `${figure} of snow forecast ${when}.`
    case 'rain':
      return `${figure} of rain forecast ${when}.`
    case 'heat':
      return `Highs of ${figure} forecast ${when}.`
    case 'cold':
      return `Lows of ${figure} forecast ${when}.`
  }
}

/** The symbol for a temperature, by unit system. */
export function unitSymbol(units: WeatherUnitsValue): string {
  return units === 'imperial' ? '°F' : '°C'
}

/** The label for a wind speed, by unit system. */
export function windUnit(units: WeatherUnitsValue): string {
  return units === 'imperial' ? 'mph' : 'km/h'
}

/** The label for an accumulation, by unit system. Rain in mm, snow in cm, both in inches. */
export function precipitationUnit(units: WeatherUnitsValue): string {
  return units === 'imperial' ? 'in' : 'mm'
}

/**
 * Parses `date` (an ISO calendar date, e.g. "2026-09-16") as local calendar components, not
 * `new Date(dateString)` — the latter constructs UTC midnight, which renders as the
 * *previous* day in a negative-offset timezone.
 *
 * Lives here rather than in a page file because the dashboard widget and the weather page both
 * need it; `format` picks how much of the date to show.
 */
export function formatForecastDay(
  date: string,
  format: Intl.DateTimeFormatOptions = { weekday: 'short' },
): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString(undefined, format)
}

/** A missing reading. The same em dash uptime shows when there is nothing to compute. */
export const NO_VALUE = '\u2014'

/**
 * The API answers 204 when no location is set, which apiFetch turns into `undefined`.
 * Normalising to `null` — the same idiom lib/session.ts uses — keeps it an ordinary,
 * cacheable value instead of a failed query.
 */
export async function fetchWeather(): Promise<Weather | null> {
  return (await apiFetch<Weather | undefined>('/weather')) ?? null
}

export async function fetchWeatherSettings(): Promise<WeatherSettings | null> {
  return (await apiFetch<WeatherSettings | undefined>('/weather/settings')) ?? null
}

/**
 * `refetchInterval` matches `WeatherCache.FreshFor` on the server — anything shorter would
 * only re-read the same cached answer.
 *
 * `refetchOnWindowFocus: true` — same reasoning as `useStatus` in `lib/status.ts` (plan 014, D5).
 */
export function useWeather() {
  return useQuery({
    queryKey: WEATHER_QUERY_KEY,
    queryFn: fetchWeather,
    refetchInterval: 15 * 60 * 1000,
    refetchOnWindowFocus: true,
  })
}

export function useWeatherSettings() {
  return useQuery({ queryKey: WEATHER_SETTINGS_QUERY_KEY, queryFn: fetchWeatherSettings })
}

function useInvalidateWeather() {
  const queryClient = useQueryClient()

  return () => {
    queryClient.invalidateQueries({ queryKey: WEATHER_QUERY_KEY })
    queryClient.invalidateQueries({ queryKey: WEATHER_SETTINGS_QUERY_KEY })
  }
}

export function useSaveWeatherSettings() {
  const invalidateWeather = useInvalidateWeather()

  return useMutation({
    mutationFn: (fields: WeatherSettingsFields) =>
      apiFetch<WeatherSettings>('/weather/settings', {
        method: 'PUT',
        body: JSON.stringify({
          latitude: Number(fields.latitude),
          longitude: Number(fields.longitude),
          place: fields.place,
          units: fields.units,
        }),
      }),
    onSuccess: () => invalidateWeather(),
  })
}

export function useDeleteWeatherSettings() {
  const invalidateWeather = useInvalidateWeather()

  return useMutation({
    // The body is empty but deliberately present: apiFetch only sets the JSON content type
    // when there is one, and the endpoint rejects a delete that does not carry it — the same
    // CSRF guard as /auth/sign-out and Links' useDeleteLink.
    mutationFn: () => apiFetch<void>('/weather/settings', { method: 'DELETE', body: '{}' }),
    onSuccess: () => invalidateWeather(),
  })
}
