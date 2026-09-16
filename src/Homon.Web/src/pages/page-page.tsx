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
        <h1>Page not found</h1>
        <p>{problemDetail(error) ?? 'This page does not exist.'}</p>
      </>
    )
  }

  if (!page) {
    return null
  }

  return (
    <>
      <h1>{page.title}</h1>
      {/*
        Safe only because the server sanitised bodyHtml before it was stored
        (Homon.Infrastructure/Pages/PageHtmlSanitizer.cs) — the client never sanitises, and
        must not be trusted to. Do not render any other field this way.
      */}
      <div dangerouslySetInnerHTML={{ __html: page.bodyHtml }} />
    </>
  )
}
