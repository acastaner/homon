import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import {
  hydrateSectionOrder,
  isNaturalOrder,
  orderSections,
  parseSectionOrder,
  readSectionOrder,
  SECTION_ORDER_STORAGE_KEY,
  swapSectionIds,
  writeSectionOrder,
} from '@/lib/section-order'

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  window.localStorage.clear()
})

/** Section-shaped, and nothing more: every helper here is generic over `{ id }`. */
function sectionsFor(ids: readonly string[]): { id: string }[] {
  return ids.map((id) => ({ id }))
}

describe('parseSectionOrder', () => {
  it('parses a stored array, preserving order and keeping the first of a duplicate pair', () => {
    expect(parseSectionOrder(null)).toEqual([])
    expect(parseSectionOrder('[]')).toEqual([])
    expect(parseSectionOrder('["b","a"]')).toEqual(['b', 'a'])
    expect(parseSectionOrder('["a","b","a"]')).toEqual(['a', 'b'])
  })

  it('survives garbage', () => {
    expect(parseSectionOrder('not json')).toEqual([])
    expect(parseSectionOrder('{"a":1}')).toEqual([])
    expect(parseSectionOrder('["a",1,null,""]')).toEqual(['a'])
  })

  it('caps at 50 ids keeping the FIRST, unlike the collapsed-sections set', () => {
    const ids = Array.from({ length: 60 }, (_, i) => `id-${String(i)}`)

    const parsed = parseSectionOrder(JSON.stringify(ids))

    expect(parsed).toHaveLength(50)
    // The head is what an order means. collapsed-sections.test.ts asserts the mirror image of this
    // pair on purpose; if this test ever flips, the slice direction was "simplified" back.
    expect(parsed[0]).toBe('id-0')
    expect(parsed).not.toContain('id-59')
  })
})

describe('readSectionOrder / writeSectionOrder', () => {
  it('returns an empty list when nothing is stored', () => {
    expect(readSectionOrder()).toEqual([])
  })

  it('round-trips, preserving the sequence', () => {
    writeSectionOrder(['weather', 'links', 'group-hosts'])

    expect(readSectionOrder()).toEqual(['weather', 'links', 'group-hosts'])
    expect(window.localStorage.getItem(SECTION_ORDER_STORAGE_KEY)).toBe('["weather","links","group-hosts"]')
  })

  it('writing an empty list removes the key', () => {
    writeSectionOrder(['weather'])
    writeSectionOrder([])

    expect(window.localStorage.getItem(SECTION_ORDER_STORAGE_KEY)).toBeNull()
    expect(readSectionOrder()).toEqual([])
  })
})

describe('orderSections', () => {
  it('leaves sections alone when nothing is stored', () => {
    const sections = sectionsFor(['a', 'b', 'c'])

    expect(orderSections(sections, []).map((s) => s.id)).toEqual(['a', 'b', 'c'])
  })

  it('arranges sections by their index in the stored order', () => {
    const sections = sectionsFor(['a', 'b', 'c'])

    expect(orderSections(sections, ['c', 'a', 'b']).map((s) => s.id)).toEqual(['c', 'a', 'b'])
  })

  it('puts a section the stored order has never seen last, keeping natural order among them', () => {
    const sections = sectionsFor(['a', 'new-1', 'b', 'new-2'])

    expect(orderSections(sections, ['b', 'a']).map((s) => s.id)).toEqual(['b', 'a', 'new-1', 'new-2'])
  })

  it('ignores a stored id whose section is gone', () => {
    const sections = sectionsFor(['a', 'b'])

    expect(orderSections(sections, ['gone', 'b', 'a']).map((s) => s.id)).toEqual(['b', 'a'])
  })

  it('does not mutate the array it is given', () => {
    const sections = sectionsFor(['a', 'b', 'c'])

    orderSections(sections, ['c', 'b', 'a'])

    expect(sections.map((s) => s.id)).toEqual(['a', 'b', 'c'])
  })

  it('renders a hydrated order exactly as it renders the order it came from', () => {
    const known = ['a', 'b', 'c', 'hidden']
    const sections = sectionsFor(['a', 'b', 'c'])
    const stored = ['c', 'a']

    expect(orderSections(sections, hydrateSectionOrder(stored, known)).map((s) => s.id)).toEqual(
      orderSections(sections, stored).map((s) => s.id),
    )
  })
})

describe('hydrateSectionOrder', () => {
  it('seeds the natural order from nothing', () => {
    expect(hydrateSectionOrder([], ['a', 'b', 'c'])).toEqual(['a', 'b', 'c'])
  })

  it('prunes an id whose section no longer exists', () => {
    expect(hydrateSectionOrder(['gone', 'b', 'a'], ['a', 'b'])).toEqual(['b', 'a'])
  })

  it('appends a known id the stored order has never seen, in natural order', () => {
    expect(hydrateSectionOrder(['c', 'a'], ['a', 'b', 'c', 'd'])).toEqual(['c', 'a', 'b', 'd'])
  })
})

describe('swapSectionIds', () => {
  const known = ['links', 'pages', 'weather']

  it('exchanges two adjacent ids', () => {
    expect(swapSectionIds(known, known, 'pages', 'weather')).toEqual(['links', 'weather', 'pages'])
  })

  it('leaves an id whose section is off screen at its own index', () => {
    // `pages` is not visible (nothing published), so the reader swaps `weather` with `links` — and
    // `pages` must not drift off index 1.
    expect(swapSectionIds(known, known, 'weather', 'links')).toEqual(['weather', 'pages', 'links'])
  })

  it('is its own inverse', () => {
    const once = swapSectionIds(known, known, 'weather', 'links')

    expect(swapSectionIds(once, known, 'weather', 'links')).toEqual(known)
  })

  it('seeds the natural order on the very first swap', () => {
    expect(swapSectionIds([], known, 'links', 'pages')).toEqual(['pages', 'links', 'weather'])
  })

  it('ignores an id that is not known', () => {
    expect(swapSectionIds(known, known, 'links', 'nonsense')).toEqual(known)
  })
})

describe('isNaturalOrder', () => {
  const known = ['a', 'b', 'c']

  it('is true for nothing stored and for the known ids in their own order', () => {
    expect(isNaturalOrder([], known)).toBe(true)
    expect(isNaturalOrder(['a', 'b', 'c'], known)).toBe(true)
  })

  it('is false for a permutation', () => {
    expect(isNaturalOrder(['b', 'a', 'c'], known)).toBe(false)
  })

  it('is true for a prefix, because hydration completes it', () => {
    expect(isNaturalOrder(['a'], known)).toBe(true)
  })
})
