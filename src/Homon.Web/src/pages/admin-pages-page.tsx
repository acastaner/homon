import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminPagesPage() {
  useDocumentTitle(pageTitle('Pages', 'Admin'))

  return (
    <>
      <h1>Pages</h1>
      <p>Not implemented yet — the Pages module (plan 007) adds the editor here.</p>
    </>
  )
}
