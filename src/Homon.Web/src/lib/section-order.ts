import { useCallback, useState } from 'react'

/**
 * The order this browser's reader has put the dashboard's sections in.
 *
 * `localStorage`, for the reasons `lib/collapsed-sections.ts` sets out at length — one origin in
 * every deployment, plain HTTP on the production LAN, no server-side code that reads it. This is
 * the same class of thing as the theme toggle and that module: a per-browser display preference.
 *
 * **Why a SECOND key rather than widening `homon-collapsed-sections`.** Plan 018's maintenance
 * notes asked for the opposite — grow the array into an object under the one key. Collapse and
 * order are two preferences with two independent defaults and, crucially, two independent
 * "back to default" states, and one key would give them one lifecycle:
 * `e2e/dashboard-collapse.spec.ts` asserts `homon-collapsed-sections` is `null` once every section
 * is expanded again, which stops being true the moment a stored order keeps the key alive, and that
 * null then has to mean "no collapse AND no custom order" to everyone who reads it afterwards. Two
 * keys keep each signal truthful and keep that landed feature's module untouched. The cost is one
 * more try/catch — the same trade plan 018 itself made against merging with `lib/theme.ts`.
 */
export const SECTION_ORDER_STORAGE_KEY = 'homon-section-order'

/**
 * The ceiling on how many ids are kept. Same number as `collapsed-sections.ts`, and the same
 * reason: a long-lived browser must not accumulate the ids of a thousand deleted probe groups.
 */
const MAX_IDS = 50

/**
 * Anything that is not an array of non-empty strings is discarded, and so is a value that will not
 * parse at all — the read happens inside a `useState` initialiser, where a throw blanks the whole
 * dashboard, so an older version's value, a newer one's, and a curious reader with dev tools open
 * all have to degrade to "natural order" rather than to an exception.
 *
 * ONE deliberate difference from `parseCollapsedSections`: the cap is `slice(0, MAX_IDS)` — keep
 * the FIRST ids, not the last. That module holds an unordered set, where the newest entries are the
 * interesting ones; this one holds a sequence whose whole meaning is its front, and `slice(-50)`
 * here would forget where the TOP sections go while faithfully remembering the bottom ones. Do not
 * "simplify" this back into symmetry with its sibling. Deduping keeps the first occurrence, for the
 * same reason: the first position is the one the reader chose.
 */
export function parseSectionOrder(raw: string | null): string[] {
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

  return [...new Set(items.filter((id): id is string => typeof id === 'string' && id !== ''))].slice(0, MAX_IDS)
}

export function readSectionOrder(): string[] {
  try {
    return parseSectionOrder(window.localStorage.getItem(SECTION_ORDER_STORAGE_KEY))
  } catch {
    // localStorage throws in a sandboxed frame and in some private-browsing modes. Natural order is
    // the right fallback — it is exactly what a first-ever visitor sees.
    return []
  }
}

export function writeSectionOrder(ids: readonly string[]): void {
  const kept = [...new Set(ids.filter((id) => id !== ''))].slice(0, MAX_IDS)

  try {
    if (kept.length === 0) {
      // REMOVE, not setItem('[]'): "never arranged" and "arranged, then reset" are the same state
      // and must look the same in dev tools, and the key's absence is what the e2e suite asserts.
      window.localStorage.removeItem(SECTION_ORDER_STORAGE_KEY)
    } else {
      window.localStorage.setItem(SECTION_ORDER_STORAGE_KEY, JSON.stringify(kept))
    }
  } catch {
    // Unavailable storage — the React state still took effect for this page life, exactly as
    // lib/theme.ts's setTheme still flips the DOM attribute when its own write fails.
  }
}

/**
 * Natural order in, the reader's order out.
 *
 * A section whose id the stored order has never seen keeps its natural relative position and goes
 * to the END, rather than being anchored beside its natural neighbours. That is a choice, not an
 * accident: a probe group created after the reader arranged the dashboard turns up below Weather
 * until it is moved, which is one stable sort with no special case, agrees with what
 * `hydrateSectionOrder` does, and is answered by the Reset order control in one click.
 *
 * `.toSorted`, never `.sort` — this must not mutate the caller's array, and `.sort(` is the one
 * lint rule in this repo to have ever fired (see plan 018's revision round).
 */
export function orderSections<T extends { id: string }>(sections: readonly T[], order: readonly string[]): T[] {
  if (order.length === 0) {
    return [...sections]
  }

  const rank = new Map(order.map((id, index) => [id, index]))

  return sections.toSorted(
    (a, b) => (rank.get(a.id) ?? Number.MAX_SAFE_INTEGER) - (rank.get(b.id) ?? Number.MAX_SAFE_INTEGER),
  )
}

/**
 * `order` pruned to ids that still exist, then extended with every known id it has not seen, in
 * natural order — so every known id appears exactly once, which is what makes a swap total.
 *
 * Pruning is what stops a deleted probe group's id sitting in storage forever, and it matters more
 * here than it does for collapse: a stale id at the HEAD of this list would survive the cap and
 * evict a live one. It agrees with `orderSections` by construction — hydrating a stored order never
 * changes what that order renders.
 */
export function hydrateSectionOrder(order: readonly string[], knownSectionIds: readonly string[]): string[] {
  const known = new Set(knownSectionIds)
  const kept = order.filter((id) => known.has(id))
  const seen = new Set(kept)

  return [...kept, ...knownSectionIds.filter((id) => !seen.has(id))]
}

/**
 * Exchanges two ids in place.
 *
 * A swap, deliberately, and not "lift the id out and splice it in next to its neighbour". Only a
 * swap is its own inverse, and only a swap leaves every OTHER id — including one whose section is
 * not on screen right now, like `pages` with nothing published — at its exact index. Splicing
 * nudges those hidden ids one position down the list on every single press, so a reader who moves a
 * section up and straight back down has silently rearranged something they cannot see.
 */
export function swapSectionIds(
  order: readonly string[],
  knownSectionIds: readonly string[],
  a: string,
  b: string,
): string[] {
  const next = hydrateSectionOrder(order, knownSectionIds)
  const i = next.indexOf(a)
  const j = next.indexOf(b)

  if (i === -1 || j === -1 || i === j) {
    return next
  }

  ;[next[i], next[j]] = [next[j], next[i]]

  return next
}

/**
 * Whether `order` is the arrangement a first-ever visitor sees.
 *
 * Needed because "back to default" is not emptiness here, the way it is for a set of collapsed ids:
 * an order can be a full list of every id and still be the natural one. Without this, undoing your
 * only move would leave the key in storage spelling out the natural order — the exact
 * "used, then reset, but it looks different in dev tools" state docs/ARCHITECTURE.md §3.22 argues
 * against.
 */
export function isNaturalOrder(order: readonly string[], knownSectionIds: readonly string[]): boolean {
  if (order.length === 0) {
    return true
  }

  const hydrated = hydrateSectionOrder(order, knownSectionIds)

  return hydrated.length === knownSectionIds.length && hydrated.every((id, index) => id === knownSectionIds[index])
}

/**
 * Reads storage once, synchronously, in the state initialiser — early enough that the first paint
 * which could show a section already knows the answer, which is why this needs no equivalent of
 * `public/theme-bootstrap.js` (see `lib/collapsed-sections.ts` for the full argument).
 *
 * `knownSectionIds` is the natural order AND the prune list, and it is `null` until `/status` has
 * answered. A swap then does nothing at all: hydrating against the single placeholder section the
 * page believes it has would invent an order out of nothing and write it.
 */
export function useSectionOrder(): {
  order: readonly string[]
  swap: (a: string, b: string, knownSectionIds: readonly string[] | null) => void
  reset: () => void
} {
  const [order, setOrder] = useState<readonly string[]>(() => readSectionOrder())

  const swap = useCallback(
    (a: string, b: string, knownSectionIds: readonly string[] | null) => {
      if (knownSectionIds === null) {
        return
      }

      const next = swapSectionIds(order, knownSectionIds, a, b)
      const kept = isNaturalOrder(next, knownSectionIds) ? [] : next

      writeSectionOrder(kept)
      setOrder(kept)
    },
    [order],
  )

  const reset = useCallback(() => {
    writeSectionOrder([])
    setOrder([])
  }, [])

  return { order, swap, reset }
}
