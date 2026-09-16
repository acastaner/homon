import { useParams } from 'react-router'

import { problemDetail } from '@/lib/api'
import { usePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/** `/pages/:slug` — an administrator-written page. */
export function PagePage() {
  const { slug } = useParams()
  const { data: page, error } = usePage(slug)

  useDocumentTitle(pageTitle(page?.title ?? slug ?? 'Page'))

  if (error) {
    return (
      <>
        <h1 className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">
          Page not found
        </h1>
        <p className="text-muted">{problemDetail(error) ?? 'This page does not exist.'}</p>
      </>
    )
  }

  if (!page) {
    return null
  }

  return (
    <>
      <h1 className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">
        {page.title}
      </h1>
      {/*
        Safe only because the server sanitised bodyHtml before it was stored
        (Homon.Infrastructure/Pages/PageHtmlSanitizer.cs) — the client never sanitises, and
        must not be trusted to. Do not render any other field this way. `.prose` (index.css)
        covers exactly the sanitiser's allow-listed tags.
      */}
      <div className="prose" dangerouslySetInnerHTML={{ __html: page.bodyHtml }} />
    </>
  )
}
