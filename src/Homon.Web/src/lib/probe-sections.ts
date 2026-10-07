import type { ProbeGroup } from '@/lib/probe-groups'
import type { Probe } from '@/lib/probes'
import type { ProbeKind } from '@/lib/status'

/**
 * One section of the probe admin page. The page lists probes the way the dashboard lays them out
 * by default — one section per group in group order, each in that group's own member order, then
 * the ungrouped rest in `Probe.Position` order — because those are the two orders that actually
 * decide where a probe appears. The flat list this replaced reordered `Probe.Position` only,
 * which moves nothing on the dashboard for a probe that is in a group.
 */
export interface ProbeSection {
  /** The group's id, or `'ungrouped'`. */
  id: string
  kind: 'group' | 'ungrouped'
  heading: string
  rows: ProbeSectionRow[]
}

export interface ProbeSectionRow {
  probe: Probe
  /** The other groups this probe also sits in, in group order — never this section's own. */
  alsoIn: string[]
}

/**
 * Short kind words for a table column. `PROBE_KIND_LABEL` is the form's wording ("Message (a
 * reporter pushes to us)"), which is right in a select and far too long for a 96px cell.
 */
export const PROBE_KIND_SHORT: Record<ProbeKind, string> = {
  ping: 'Ping',
  http: 'HTTP',
  smb: 'SMB',
  snmp: 'SNMP',
  message: 'Message',
}

/**
 * Every group — empty ones too, unlike the dashboard, because an administrator needs to see a
 * group exists to fill it — then the ungrouped section under the dashboard's own label rule
 * (`dashboardSections`): "Services" when no group has members, "Other" once one does. The
 * ungrouped section is left out when every probe is grouped, as on the dashboard.
 */
export function probeSections(probes: readonly Probe[], groups: readonly ProbeGroup[]): ProbeSection[] {
  const byId = new Map(probes.map((probe) => [probe.id, probe]))
  const grouped = new Set(groups.flatMap((group) => group.probeIds))

  function alsoIn(probeId: string, exceptGroupId: string | null): string[] {
    return groups
      .filter((group) => group.id !== exceptGroupId && group.probeIds.includes(probeId))
      .map((group) => group.name)
  }

  const sections: ProbeSection[] = groups.map((group) => ({
    id: group.id,
    kind: 'group',
    heading: group.name,
    rows: group.probeIds
      .map((id) => byId.get(id))
      .filter((probe): probe is Probe => probe !== undefined)
      .map((probe) => ({ probe, alsoIn: alsoIn(probe.id, group.id) })),
  }))

  const ungrouped = probes.filter((probe) => !grouped.has(probe.id))
  const anyGroupHasMembers = sections.some((section) => section.rows.length > 0)

  if (ungrouped.length > 0) {
    sections.push({
      id: 'ungrouped',
      kind: 'ungrouped',
      heading: anyGroupHasMembers ? 'Other' : 'Services',
      rows: ungrouped.map((probe) => ({ probe, alsoIn: [] })),
    })
  }

  return sections
}

/**
 * The id list to send after moving `id` one place up or down among `sectionIds`, or `null` when
 * it is already at that end. `allIds` is the list the endpoint takes: for a group it is the
 * section itself; for the ungrouped section it is every probe in `Probe.Position` order, and only
 * the two swapped probes change place in it — the grouped probes interleaved between them keep
 * their positions, which matter nowhere on the dashboard but are still somebody's data.
 */
export function moveWithinSection(
  allIds: readonly string[],
  sectionIds: readonly string[],
  id: string,
  direction: -1 | 1,
): string[] | null {
  const index = sectionIds.indexOf(id)
  const neighbour = sectionIds[index + direction]

  if (index < 0 || neighbour === undefined) {
    return null
  }

  const next = [...allIds]
  const a = next.indexOf(id)
  const b = next.indexOf(neighbour)

  if (a < 0 || b < 0) {
    return null
  }

  ;[next[a], next[b]] = [next[b], next[a]]
  return next
}

/** What the probe polls, as an administrator would type it: the URL for HTTP, else the host. */
export function probeTarget(probe: Probe): string {
  const http = probe.kind === 'http' ? probe.http : null

  if (http) {
    return `${http.useHttps ? 'https' : 'http'}://${probe.host}/${http.path}`
  }

  return probe.host
}
