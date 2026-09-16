import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import { THEME_STORAGE_KEY, getStoredTheme, setTheme } from '@/lib/theme'

beforeEach(() => {
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
})

afterEach(() => {
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
})

describe('theme', () => {
  it('getStoredTheme returns null when nothing is stored', () => {
    expect(getStoredTheme()).toBeNull()
  })

  it('setTheme writes the DOM attribute and localStorage together', () => {
    setTheme('light')

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(getStoredTheme()).toBe('light')
  })

  it('setTheme back to dark removes the DOM attribute — dark is the bare :root', () => {
    setTheme('light')
    setTheme('dark')

    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(getStoredTheme()).toBeNull()
  })
})
