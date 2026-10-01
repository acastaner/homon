import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, Circle, HardDrive, RefreshCw, ShieldCheck, type LucideIcon } from 'lucide-react'

import { apiFetch } from '@/lib/api'
import { PROBES_QUERY_KEY } from '@/lib/probes'
import { STATUS_QUERY_KEY } from '@/lib/status'

/** Mirrors MessageStatus in Homon.Domain/Messaging/MessageStatus.cs. */
export type MessageStatus = 'none' | 'success' | 'warning' | 'failure' | 'unknown'

/** Mirrors MessageBodyVisibility in Homon.Domain/Messaging/MessageBodyVisibility.cs. */
export type BodyVisibility = 'administrator' | 'reader'

/** Mirrors ReporterEndpoints.MessageSummaryResponse. No body: see plan 021's Decision 11. */
export interface MessageSummary {
  id: number
  name: string
  status: MessageStatus
  category: string
  receivedAt: string
  nextExpectedAt: string | null
  recurrence: string | null
}

/** Mirrors ReporterEndpoints.MessageResponse — the one shape that carries a body. */
export interface Message extends MessageSummary {
  description: string | null
  body: string | null
  truncated: boolean
}

/** Mirrors ReporterEndpoints.ReporterResponse. */
export interface Reporter {
  id: string
  identifier: string
  name: string
  description: string | null
  bodyVisibility: BodyVisibility
  createdAt: string
  tokenId: string
  keyLastUsedAt: string | null
  keyRevokedAt: string | null
  latest: MessageSummary | null
  messageCount: number
  isWatched: boolean
}

/** The admin form's shape. Trimmed and validated server-side. */
export interface ReporterFields {
  name: string
  description: string
  bodyVisibility: BodyVisibility
}

export const REPORTERS_QUERY_KEY = ['reporters'] as const

export function fetchReporters(): Promise<Reporter[]> {
  return apiFetch<Reporter[]>('/reporters')
}

/**
 * Takes `enabled` from the caller because `AdminProbesPage` only needs reporters while the kind
 * being edited is `message`. Fetching unconditionally there would make every existing probe-page
 * test declare a `/reporters` route, since `src/test/fetch.ts` throws on an undeclared path by
 * design — the gate is what keeps those fixtures untouched.
 */
export function useReporters(options: { enabled?: boolean } = {}) {
  return useQuery({ queryKey: REPORTERS_QUERY_KEY, queryFn: fetchReporters, ...options })
}

export function useReporterMessages(id: string | null) {
  return useQuery({
    queryKey: [...REPORTERS_QUERY_KEY, id, 'messages'] as const,
    queryFn: () => apiFetch<Message[]>(`/reporters/${id}/messages`),
    enabled: id !== null,
  })
}

/**
 * Reporters, probes and the dashboard all move together: renaming a reporter changes the admin
 * list, and deleting one can only happen when no probe watches it, so a stale probe list would
 * misreport why a delete was refused.
 */
function useInvalidateReporters() {
  const queryClient = useQueryClient()

  return () => {
    void queryClient.invalidateQueries({ queryKey: REPORTERS_QUERY_KEY })
    void queryClient.invalidateQueries({ queryKey: PROBES_QUERY_KEY })
    void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY })
  }
}

/** Mirrors ReporterEndpoints.ReporterCreatedResponse. The token is shown once and never again. */
export interface ReporterCreated {
  reporter: Reporter
  token: string
}

export function useCreateReporter() {
  const invalidateReporters = useInvalidateReporters()

  return useMutation({
    mutationFn: (fields: ReporterFields) =>
      apiFetch<ReporterCreated>('/reporters', { method: 'POST', body: JSON.stringify(fields) }),
    onSuccess: () => invalidateReporters(),
  })
}

export function useUpdateReporter() {
  const invalidateReporters = useInvalidateReporters()

  return useMutation({
    mutationFn: ({ id, fields }: { id: string; fields: ReporterFields }) =>
      apiFetch<Reporter>(`/reporters/${id}`, { method: 'PUT', body: JSON.stringify(fields) }),
    onSuccess: () => invalidateReporters(),
  })
}

export function useReplaceReporterKey() {
  const invalidateReporters = useInvalidateReporters()

  return useMutation({
    mutationFn: (id: string) =>
      apiFetch<{ reporterId: string; tokenId: string; token: string }>(
        `/reporters/${id}/key`,
        { method: 'POST', body: '{}' },
      ),
    onSuccess: () => invalidateReporters(),
  })
}

export function useDeleteReporter() {
  const invalidateReporters = useInvalidateReporters()

  return useMutation({
    // The empty body is deliberate: apiFetch only sets the JSON content type when there is one,
    // and the API refuses a mutating request without it — the same CSRF guard as /auth/sign-out.
    mutationFn: (id: string) => apiFetch<void>(`/reporters/${id}`, { method: 'DELETE', body: '{}' }),
    onSuccess: () => invalidateReporters(),
  })
}

/**
 * The word a status chip shows for a message probe, reusing `StatusChip`'s glyphs and colours
 * under the vocabulary `docs/design-brief.md` already set aside for backup outcomes. Keyed on
 * what the reporter claimed rather than on the probe's colour, because a reporter that claimed
 * nothing has "Reported" and not "Succeeded" to say for itself.
 */
export const MESSAGE_STATUS_WORD: Record<MessageStatus, string> = {
  success: 'Succeeded',
  warning: 'Warning',
  failure: 'Failed',
  unknown: 'Unknown',
  none: 'Reported',
}

/**
 * Categories are an open vocabulary — a new one is data, not a deploy (plan 021's Decision 9) —
 * so this map is a convenience and its fallback is the contract. An unknown slug renders with the
 * generic glyph and its own text, never an error.
 */
const CATEGORY_ICONS: Record<string, LucideIcon> = {
  backup: Archive,
  storage: HardDrive,
  certificate: ShieldCheck,
  update: RefreshCw,
}

export function messageCategoryIcon(category: string): LucideIcon {
  return CATEGORY_ICONS[category] ?? Circle
}
