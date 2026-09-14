import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminProbesPage() {
  useDocumentTitle(pageTitle('Probes', 'Admin'))

  return (
    <>
      <h1>Probes</h1>
      <p>Not implemented yet — the Monitoring module (plan 002) adds probes here.</p>
    </>
  )
}
