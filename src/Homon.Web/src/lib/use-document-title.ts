import { useEffect } from 'react'

/** The suffix every page carries, and the whole title of the one that has nothing else. */
export const SITE_NAME = 'Homon'

/**
 * Composes a document title from its parts, most specific first. Blank and null parts are
 * dropped rather than rendered as empty segments.
 */
export function pageTitle(...parts: (string | null | undefined)[]): string {
  return [...parts.filter((part) => part != null && part.trim().length > 0), SITE_NAME].join(' | ')
}

/**
 * Sets `document.title` while the calling component is mounted, and puts back whatever was
 * there when it unmounts. A hook rather than a helmet library: one string per page does not
 * earn a dependency.
 */
export function useDocumentTitle(title: string): void {
  useEffect(() => {
    const previous = document.title
    document.title = title

    return () => {
      document.title = previous
    }
  }, [title])
}
