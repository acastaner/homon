import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/**
 * The family's page. Phase 0 renders the frame and nothing in it: the status cards, links,
 * weather and calendar each arrive with their module (docs/MODULES.md). The empty state
 * says so, because a blank page reads as broken.
 */
export function DashboardPage() {
  useDocumentTitle(pageTitle('Dashboard'))

  return (
    <>
      <h1>Dashboard</h1>
      <section aria-labelledby="services-heading">
        <h2 id="services-heading">Services</h2>
        <p>No probes yet. An administrator adds them under Admin → Probes.</p>
      </section>
      <section aria-labelledby="links-heading">
        <h2 id="links-heading">Links</h2>
        <p>No links yet. An administrator adds them under Admin → Links.</p>
      </section>
    </>
  )
}
