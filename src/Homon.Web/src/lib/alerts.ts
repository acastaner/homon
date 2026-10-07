import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/**
 * Mirrors AlertSettingsResponse in Homon.Api/Endpoints/AlertEndpoints.cs. The Resend key is
 * write-only: the API reports only whether one is stored, never the key.
 */
export interface AlertSettings {
  isEnabled: boolean
  hasApiKey: boolean
  fromAddress: string
  fromName: string
  recipients: string[]
  updatedAt: string | null
}

/**
 * Mirrors AlertSettingsRequest in AlertEndpoints.cs. `apiKey` follows plan 003's write-only rule:
 * left out keeps the stored key, `''` removes it, anything else replaces it. The page sends it
 * only while its key input is on screen.
 */
export interface AlertSettingsInput {
  isEnabled: boolean
  apiKey?: string
  fromAddress: string
  fromName: string
  recipients: string[]
}

/** Mirrors the AlertKind enum in Homon.Domain/Alerts/AlertKind.cs. */
export type AlertKindValue = 'down' | 'up' | 'test'

/** Mirrors the AlertDeliveryState enum in Homon.Domain/Alerts/AlertDeliveryState.cs. */
export type AlertDeliveryStateValue = 'pending' | 'sent' | 'failed' | 'skipped'

/** Mirrors AlertDeliveryResponse in AlertEndpoints.cs. */
export interface AlertDelivery {
  id: number
  kind: AlertKindValue
  probeId: string | null
  probeName: string
  occurredAt: string
  downSince: string | null
  state: AlertDeliveryStateValue
  attempts: number
  sentAt: string | null
  lastError: string | null
}

export const ALERT_SETTINGS_QUERY_KEY = ['alerts', 'settings'] as const
export const ALERT_DELIVERIES_QUERY_KEY = ['alerts', 'deliveries'] as const

export function useAlertSettings() {
  return useQuery({
    queryKey: ALERT_SETTINGS_QUERY_KEY,
    queryFn: () => apiFetch<AlertSettings>('/alerts/settings'),
  })
}

/**
 * The newest alerts. While one is Pending the list re-reads every two seconds, so a test email
 * turns Sent or Failed on screen without a reload; once nothing is pending it stops.
 */
export function useAlertDeliveries() {
  return useQuery({
    queryKey: ALERT_DELIVERIES_QUERY_KEY,
    queryFn: () => apiFetch<AlertDelivery[]>('/alerts/deliveries'),
    refetchInterval: (query) => (query.state.data?.some((delivery) => delivery.state === 'pending') ? 2000 : false),
  })
}

export function useSaveAlertSettings() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: AlertSettingsInput) =>
      apiFetch<AlertSettings>('/alerts/settings', { method: 'PUT', body: JSON.stringify(input) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ALERT_SETTINGS_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: ALERT_DELIVERIES_QUERY_KEY })
    },
  })
}

export function useSendTestAlert() {
  const queryClient = useQueryClient()

  return useMutation({
    // The body is empty but deliberately present: apiFetch only sets the JSON content type when
    // there is one, and the endpoint rejects a POST that does not carry it (the CSRF guard).
    mutationFn: () => apiFetch<AlertDelivery>('/alerts/test', { method: 'POST', body: '{}' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ALERT_DELIVERIES_QUERY_KEY }),
  })
}

/**
 * A downtime in words, mirroring AlertMessageBuilder.FormatDuration in C# so the page and the
 * email say the same thing: "less than a minute", "5 min", "1 h 12 min", "1 d 1 h".
 */
export function formatDuration(milliseconds: number): string {
  const minutes = Math.floor(milliseconds / 60_000)

  if (minutes < 1) {
    return 'less than a minute'
  }

  if (minutes < 60) {
    return `${String(minutes)} min`
  }

  if (minutes < 24 * 60) {
    const hours = Math.floor(minutes / 60)
    const rest = minutes % 60

    return rest === 0 ? `${String(hours)} h` : `${String(hours)} h ${String(rest)} min`
  }

  const days = Math.floor(minutes / (24 * 60))
  const hours = Math.floor((minutes % (24 * 60)) / 60)

  return hours === 0 ? `${String(days)} d` : `${String(days)} d ${String(hours)} h`
}
