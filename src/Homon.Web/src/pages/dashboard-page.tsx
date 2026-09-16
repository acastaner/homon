import { useEffect, useState } from 'react'
import { Link as RouterLink } from 'react-router'

import { useLinks } from '@/lib/links'
import { usePublishedPages } from '@/lib/pages'
import { dashboardSections, formatCheckedAt, useStatus } from '@/lib/status'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

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

  return (
    <>
      <h1>Dashboard</h1>
      {totals ? (
        <p>
          {totals.up} up · {totals.unstable} unstable · {totals.down} down · {totals.paused} paused ·{' '}
          {formatUptime(totals.uptimePercent)} uptime, 30 days
        </p>
      ) : null}
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
    </>
  )
}
