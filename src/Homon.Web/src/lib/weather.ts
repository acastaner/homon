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
  Sun,
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

/** Mirrors WeatherForecastDayResponse in Homon.Api/Endpoints/WeatherEndpoints.cs. */
export interface WeatherForecastDay {
  date: string
  condition: WeatherCondition
  high: number
  low: number
}

/** Mirrors WeatherResponse in Homon.Api/Endpoints/WeatherEndpoints.cs. */
export interface Weather {
  place: string | null
  units: WeatherUnitsValue
  current: WeatherCurrent
  forecast: WeatherForecastDay[]
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
 */
export function useWeather() {
  return useQuery({
    queryKey: WEATHER_QUERY_KEY,
    queryFn: fetchWeather,
    refetchInterval: 15 * 60 * 1000,
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
