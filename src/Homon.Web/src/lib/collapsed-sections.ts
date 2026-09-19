import { useCallback, useState } from 'react'

/**
 * Which dashboard sections this browser has folded away.
 *
 * `localStorage`, although the feature was first requested as a cookie (plan 018, D1). Three
 * reasons, in the order they matter:
 *
 * 1. A cookie here has a silent failure mode. The SPA and the API are ONE origin in every
 *    deployment, and the production host serves the LAN over plain HTTP (docs/ARCHITECTURE.md
 *    §3.21) — so a `Secure` attribute, which every cookie checklist tells you to add, would make
 *    this work in every test and on any HTTPS deployment and remember nothing on the household's
 *    actual dashboard. There is no equivalent mistake available here.
 * 2. A cookie would ride on every /status poll (every 30 s) and on links, pages and weather, for
 *    nothing: no server-side code reads it. The API reads only its own `homon.sid`.
 * 3. lib/theme.ts already persists a per-browser display preference exactly this way.
 *
 * The one thing a cookie buys — a value the SERVER can read — has no use while Homon is a Vite
 * SPA behind nginx. If that changes, this module is the only thing to swap.
 */
export const COLLAPSED_SECTIONS_STORAGE_KEY = 'homon-collapsed-sections'

/**
 * The ceiling on how many ids are kept, newest last. A household collapses a handful of
 * sections; this exists so that a long-lived browser cannot accumulate the ids of a thousand
 * deleted probe groups, not because 50 is a meaningful number.
 */
const MAX_IDS = 50

/**
 * Anything that is not an array of non-empty strings is discarded, and so is a value that will
 * not parse at all.
 *
 * This runs inside a `useState` initialiser, where a throw blanks the whole dashboard — so a
 * value written by an older version of this code, by a newer one, or by a curious reader with
 * dev tools open must all degrade to "everything expanded" rather than to an exception.
 */
export function parseCollapsedSections(raw: string | null): string[] {
  if (raw === null) {
    return []
  }

  let parsed: unknown

  try {
    parsed = JSON.parse(raw)
  } catch {
    return []
  }

  if (!Array.isArray(parsed)) {
    return []
  }

  const items = parsed as unknown[]

  return [...new Set(items.filter((id): id is string => typeof id === 'string' && id !== ''))].slice(-MAX_IDS)
}

export function readCollapsedSections(): Set<string> {
  try {
    return new Set(parseCollapsedSections(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)))
  } catch {
    // localStorage throws in a sandboxed frame and in some private-browsing modes. Everything
    // expanded is the right fallback — it is exactly what a first-ever visitor sees.
    return new Set()
  }
}

export function writeCollapsedSections(ids: Iterable<string>): void {
  const kept = [...new Set([...ids].filter((id) => id !== ''))].slice(-MAX_IDS)

  try {
    if (kept.length === 0) {
      // REMOVE, not setItem('[]'): "never used" and "used, then everything re-expanded" are the
      // same state and should look the same in dev tools — and the key's presence is what
      // e2e/dashboard-collapse.spec.ts asserts on.
      window.localStorage.removeItem(COLLAPSED_SECTIONS_STORAGE_KEY)
    } else {
      window.localStorage.setItem(COLLAPSED_SECTIONS_STORAGE_KEY, JSON.stringify(kept))
    }
  } catch {
    // Unavailable storage — the React state below still took effect for this page life, exactly
    // as lib/theme.ts's setTheme still flips the DOM attribute when its own write fails.
  }
}

/**
 * Reads storage once, synchronously, in the state initialiser — early enough that the first
 * paint which could show a section already knows the answer, which is why this needs no
 * equivalent of `public/theme-bootstrap.js` (the theme has to be right before ANY paint; a
 * section cannot render before its data has loaded anyway).
 *
 * `knownSectionIds` prunes ids whose section no longer exists — a deleted probe group would
 * otherwise sit in storage forever. Pass `null` while the section list is not yet known: before
 * `/status` resolves the page believes it has exactly one section, and pruning against that
 * would wipe every group's remembered state.
 */
export function useCollapsedSections(): {
  collapsed: ReadonlySet<string>
  toggle: (id: string, knownSectionIds: readonly string[] | null) => void
} {
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => readCollapsedSections())

  const toggle = useCallback(
    (id: string, knownSectionIds: readonly string[] | null) => {
      const known = knownSectionIds === null ? null : new Set(knownSectionIds)
      const next = new Set(known === null ? collapsed : [...collapsed].filter((candidate) => known.has(candidate)))

      if (collapsed.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }

      writeCollapsedSections(next)
      setCollapsed(next)
    },
    [collapsed],
  )

  return { collapsed, toggle }
}
