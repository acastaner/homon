import { useParams } from 'react-router'

import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/** `/pages/:slug` — an administrator-written page. Placeholder until the Pages module lands. */
export function PagePage() {
  const { slug } = useParams()

  useDocumentTitle(pageTitle(slug ?? 'Page'))

  return (
    <>
      <h1>{slug}</h1>
      <p>Pages are not implemented yet.</p>
    </>
  )
}
