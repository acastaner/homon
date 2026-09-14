import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminLinksPage() {
  useDocumentTitle(pageTitle('Links', 'Admin'))

  return (
    <>
      <h1>Links</h1>
      <p>Not implemented yet — the Links module (plan 006) adds links here.</p>
    </>
  )
}
