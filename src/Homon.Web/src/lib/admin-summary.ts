import type { AlertSettings } from '@/lib/alerts'
import type { ApiKey } from '@/lib/api-keys'
import type { Link } from '@/lib/links'
import type { AdminPageSummary } from '@/lib/pages'
import type { ProbeGroup } from '@/lib/probe-groups'
import type { Probe } from '@/lib/probes'
import { isOverdue, type Reporter } from '@/lib/reporters'
import type { WeatherSettings } from '@/lib/weather'

/**
 * What the Admin home says about each section, worked out in the browser from the list responses
 * the section pages already fetch (plan 025's D5). A `/admin/summary` endpoint would have
 * duplicated seven derivations in C# for the sake of one page, and its cache would have been a
 * second one to keep in step. These are pure functions: the hooks stay in the page, so the maths
 * can be tested without a fetch stub.
 *
 * `down` and `unstable` are what the page turns into chips; `text` is the quiet mono line.
 */
export interface AdminSummary {
  text: string
  down?: number
  unstable?: number
}

/** "1 probe", "2 probes": the one pluralisation rule every admin header and summary shares. */
export function countOf(count: number, noun: string): string {
  return `${String(count)} ${noun}${count === 1 ? '' : 's'}`
}

function isPausedProbe(probe: Probe): boolean {
  return probe.isPaused || probe.status === 'paused'
}

export function summariseProbes(probes: readonly Probe[]): AdminSummary {
  const paused = probes.filter(isPausedProbe).length
  // A paused probe's status is 'paused', so it is never also counted down.
  const down = probes.filter((probe) => !isPausedProbe(probe) && probe.status === 'down').length
  const unstable = probes.filter((probe) => !isPausedProbe(probe) && probe.status === 'unstable').length

  return {
    text: `${countOf(probes.length, 'probe')}${paused > 0 ? ` · ${String(paused)} paused` : ''}`,
    down,
    unstable,
  }
}

export function summariseGroups(groups: readonly ProbeGroup[], probes: readonly Probe[]): AdminSummary {
  const inNone = probes.filter((probe) => probe.groupIds.length === 0).length

  return {
    text: `${countOf(groups.length, 'group')}${inNone > 0 ? ` · ${String(inNone)} ${inNone === 1 ? 'probe' : 'probes'} in none` : ''}`,
  }
}

export function summariseReporters(reporters: readonly Reporter[], now: number = Date.now()): AdminSummary {
  return {
    text: countOf(reporters.length, 'reporter'),
    down: reporters.filter((reporter) => isOverdue(reporter, now)).length,
  }
}

export function summariseLinks(links: readonly Link[]): AdminSummary {
  return { text: countOf(links.length, 'link') }
}

export function summarisePages(pages: readonly AdminPageSummary[]): AdminSummary {
  const drafts = pages.filter((page) => !page.isPublished).length

  return {
    text: `${countOf(pages.length, 'page')}${drafts > 0 ? ` · ${countOf(drafts, 'draft')}` : ''}`,
  }
}

export function summariseWeather(settings: WeatherSettings | null): AdminSummary {
  if (settings === null) {
    return { text: 'Not set' }
  }

  const where = `${settings.latitude.toFixed(2)}, ${settings.longitude.toFixed(2)} · ${settings.units}`

  return { text: settings.place ? `${settings.place} · ${where}` : where }
}

export function summariseApiKeys(keys: readonly ApiKey[]): AdminSummary {
  // Revoked wins over expired (plan 025's D12): a key that is both reads Revoked everywhere.
  const revoked = keys.filter((key) => key.revokedAt !== null).length
  const expired = keys.filter((key) => key.revokedAt === null && key.isExpired).length
  const active = keys.length - revoked - expired
  const parts = [
    active > 0 ? `${String(active)} active` : null,
    revoked > 0 ? `${String(revoked)} revoked` : null,
    expired > 0 ? `${String(expired)} expired` : null,
  ].filter((part) => part !== null)

  return { text: parts.length > 0 ? parts.join(' · ') : '0 keys' }
}

export function summariseAlerts(settings: AlertSettings): AdminSummary {
  const recipients = countOf(settings.recipients.length, 'recipient')
  const isReady = settings.hasApiKey && settings.fromAddress.length > 0 && settings.recipients.length > 0

  if (!isReady) {
    return { text: 'Not set up' }
  }

  return { text: `${settings.isEnabled ? 'On' : 'Off'} · ${recipients}` }
}
