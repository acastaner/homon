import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminApiKeysPage() {
  useDocumentTitle(pageTitle('API keys', 'Admin'))

  return (
    <>
      <h1>API keys</h1>
      <p>Not implemented yet — the Backups module (plan 008) adds key management here. Until then, mint one with `create-api-key`.</p>
    </>
  )
}
