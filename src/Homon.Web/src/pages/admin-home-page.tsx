import { Link } from 'react-router'
import { ChevronRight, Clock } from 'lucide-react'

import { COLUMN_HEAD, PANEL, ROW, ROW_CELLS, ROW_TINT } from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { StatusChip } from '@/components/status-chip'
import {
  summariseApiKeys,
  summariseGroups,
  summariseLinks,
  summarisePages,
  summariseProbes,
  summariseReporters,
  summariseWeather,
  type AdminSummary,
} from '@/lib/admin-summary'
import { useApiKeys } from '@/lib/api-keys'
import { useLinks } from '@/lib/links'
import { useAdminPages } from '@/lib/pages'
import { useProbeGroups } from '@/lib/probe-groups'
import { useProbes } from '@/lib/probes'
import { useReporters } from '@/lib/reporters'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'
import { useWeatherSettings } from '@/lib/weather'

interface AdminItem {
  to: string
  label: string
  description: string
  /** `null` while the section's list is loading or failed: the summary is a convenience, the link still works. */
  summary: AdminSummary | null
  /** What `down` counts, in the chip's own words: a probe is down, a reporter is overdue. */
  downWord: string
}

/*
 * One grid for the column head and every row. Below `lg` a row folds: the section's name and
 * chevron on the first line, what it sets up on the second, its current state on the third.
 */
const ROW_GRID =
  'grid grid-cols-[minmax(0,1fr)_24px] items-center gap-x-3 gap-y-1 lg:grid-cols-[200px_minmax(0,1fr)_minmax(0,300px)_24px] lg:gap-x-3.5'

/**
 * The admin front door: every section, what it sets up, and a one-line summary of where it stands
 * now. The summaries come from the same list queries the section pages use (same cache keys, so
 * opening a section after this page is instant) and are worked out in `lib/admin-summary.ts` — no
 * endpoint of their own (plan 025's D5).
 *
 * The `<Link>` holds only the section's name, so its accessible name stays "Probes" and not
 * "Probes 12 probes · 1 paused"; the row is made wholly clickable by stretching the link's
 * pseudo-element over it, not by wrapping the row in the link.
 */
export function AdminHomePage() {
  useDocumentTitle(pageTitle('Admin'))

  const probes = useProbes()
  const groups = useProbeGroups()
  const reporters = useReporters()
  const links = useLinks()
  const pages = useAdminPages()
  const weather = useWeatherSettings()
  const apiKeys = useApiKeys()

  const monitoring: AdminItem[] = [
    {
      to: '/admin/probes',
      label: 'Probes',
      description: 'What is polled, how often, and in which group.',
      summary: probes.data ? summariseProbes(probes.data) : null,
      downWord: 'down',
    },
    {
      to: '/admin/probe-groups',
      label: 'Probe groups',
      description: "The dashboard's sections and their order.",
      summary: groups.data && probes.data ? summariseGroups(groups.data, probes.data) : null,
      downWord: 'down',
    },
    {
      to: '/admin/reporters',
      label: 'Reporters',
      description: 'Scripts and agents that push reports in.',
      summary: reporters.data ? summariseReporters(reporters.data) : null,
      downWord: 'overdue',
    },
  ]
  const content: AdminItem[] = [
    {
      to: '/admin/links',
      label: 'Links',
      description: 'The link list on the dashboard.',
      summary: links.data ? summariseLinks(links.data) : null,
      downWord: 'down',
    },
    {
      to: '/admin/pages',
      label: 'Pages',
      description: 'Free-form pages readers open from the dashboard.',
      summary: pages.data ? summarisePages(pages.data) : null,
      downWord: 'down',
    },
    {
      to: '/admin/weather',
      label: 'Weather',
      description: "The household's location and units.",
      // `null` is a real answer here (no location yet), which is not the same as not loaded.
      summary: weather.isSuccess ? summariseWeather(weather.data) : null,
      downWord: 'down',
    },
  ]
  const access: AdminItem[] = [
    {
      to: '/admin/api-keys',
      label: 'API keys',
      description: 'Keys for scripts: minted, scoped, revoked.',
      summary: apiKeys.data ? summariseApiKeys(apiKeys.data) : null,
      downWord: 'down',
    },
  ]

  return (
    <>
      <AdminPageHeader
        title="Admin"
        description="Everything the dashboard shows is set up here. Each section opens its own page."
      />
      <nav aria-label="Admin sections" className="flex flex-col gap-6">
        <SectionTable id="monitoring" heading="Monitoring" items={monitoring} />
        <SectionTable id="content" heading="Content" items={content} />
        <SectionTable id="access" heading="Access" items={access} />
      </nav>
    </>
  )
}

function SectionTable({ id, heading, items }: { id: string; heading: string; items: AdminItem[] }) {
  return (
    <AdminSection id={`admin-${id}`} heading={heading}>
      <div className={PANEL}>
        <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
          <span>Section</span>
          <span>What it sets up</span>
          <span>Now</span>
          <span />
        </div>
        <ul className="flex list-none flex-col">
          {items.map((item) => (
            <AdminRow key={item.to} item={item} />
          ))}
        </ul>
      </div>
    </AdminSection>
  )
}

function AdminRow({ item }: { item: AdminItem }) {
  const { summary } = item
  const tint = summary?.down ? ROW_TINT.down : summary?.unstable ? ROW_TINT.unstable : ''

  return (
    <li className={`${ROW} relative`}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${tint}`}>
        <Link
          to={item.to}
          className="col-start-1 row-start-1 text-[15px] font-semibold text-text no-underline after:absolute after:inset-0 hover:underline lg:col-start-auto lg:row-start-auto"
        >
          {item.label}
        </Link>
        <span className="col-span-2 text-[13.5px] text-muted lg:col-span-1 lg:col-start-auto">{item.description}</span>
        <span className="col-span-2 flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 lg:col-span-1">
          {summary?.down ? (
            <StatusChip
              state="down"
              word={`${String(summary.down)} ${item.downWord}`}
              glyph={item.downWord === 'overdue' ? Clock : undefined}
            />
          ) : null}
          {summary?.unstable ? <StatusChip state="unstable" word={`${String(summary.unstable)} unstable`} /> : null}
          {summary ? <span className="mono text-[13px] text-muted">{summary.text}</span> : null}
        </span>
        <ChevronRight
          aria-hidden="true"
          size={18}
          strokeWidth={2}
          className="col-start-2 row-start-1 justify-self-end text-muted lg:col-start-auto lg:row-start-auto"
        />
      </div>
    </li>
  )
}
