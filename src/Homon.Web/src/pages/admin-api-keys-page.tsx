import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminApiKeysPage() {
  useDocumentTitle(pageTitle('API keys', 'Admin'))

  return (
    <>
      <h1 className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">
        API keys
      </h1>
      <p className="text-muted">
        Not implemented yet — the Backups module (plan 008) adds key management here. Until then, mint one with{' '}
        <code className="mono rounded bg-surface px-1.5 py-0.5">create-api-key</code>.
      </p>
    </>
  )
}
