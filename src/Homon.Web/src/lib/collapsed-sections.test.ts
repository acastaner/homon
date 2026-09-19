import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import {
  COLLAPSED_SECTIONS_STORAGE_KEY,
  parseCollapsedSections,
  readCollapsedSections,
  writeCollapsedSections,
} from '@/lib/collapsed-sections'

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  window.localStorage.clear()
})

describe('collapsed-sections', () => {
  it('parseCollapsedSections parses a stored array, deduplicating', () => {
    expect(parseCollapsedSections(null)).toEqual([])
    expect(parseCollapsedSections('[]')).toEqual([])
    expect(parseCollapsedSections('["a","b"]')).toEqual(['a', 'b'])
    expect(parseCollapsedSections('["a","a","b"]')).toEqual(['a', 'b'])
  })

  it('parseCollapsedSections survives garbage', () => {
    expect(parseCollapsedSections('not json')).toEqual([])
    expect(parseCollapsedSections('{"a":1}')).toEqual([])
    expect(parseCollapsedSections('["a",1,null,""]')).toEqual(['a'])
  })

  it('readCollapsedSections returns an empty set when nothing is stored', () => {
    expect(readCollapsedSections()).toEqual(new Set())
  })

  it('writeCollapsedSections then readCollapsedSections round-trips a two-id set', () => {
    writeCollapsedSections(['a', 'b'])

    expect(readCollapsedSections()).toEqual(new Set(['a', 'b']))
    expect(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)).toBe('["a","b"]')
  })

  it('writing an empty set removes the key', () => {
    writeCollapsedSections(['a'])
    writeCollapsedSections([])

    expect(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)).toBeNull()
    expect(readCollapsedSections().size).toBe(0)
  })

  it('caps at 50 ids, keeping the newest', () => {
    const ids = Array.from({ length: 60 }, (_, i) => `id-${String(i)}`)
    writeCollapsedSections(ids)

    const stored = readCollapsedSections()

    expect(stored.size).toBe(50)
    expect(stored.has('id-59')).toBe(true)
    expect(stored.has('id-0')).toBe(false)
  })
})
