import { useCallback, useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

/**
 * Must match the literal string `public/theme-bootstrap.js` reads — the two are independent
 * copies by construction (the bootstrap script runs before any bundle exists), so a rename
 * on one side without the other silently breaks the no-flash guarantee. Grepped together in
 * `e2e/theme.spec.ts`.
 */
export const THEME_STORAGE_KEY = 'homon-theme'

/** The DOM attribute the bootstrap script sets before React mounts (Decision 1: `data-theme`,
 * not a class, for exactly that reason). */
function readDomTheme(): Theme {
  return document.documentElement.dataset.theme === 'light' ? 'light' : 'dark'
}

/** `null` when nothing is stored — dark renders by default, the bootstrap script's own rule. */
export function getStoredTheme(): Theme | null {
  try {
    return window.localStorage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : null
  } catch {
    return null
  }
}

/**
 * Writes the choice and flips the DOM attribute immediately, so the toggle is instant rather
 * than waiting on a re-render. `try/catch` around the write only — the DOM update must still
 * happen even where storage is unavailable (private browsing, disabled storage).
 */
export function setTheme(theme: Theme): void {
  if (theme === 'light') {
    document.documentElement.dataset.theme = 'light'
  } else {
    delete document.documentElement.dataset.theme
  }

  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // Unavailable storage — the DOM attribute above still took effect for this page life.
  }
}

/**
 * Syncs React state with whatever the DOM attribute already carries — the bootstrap script
 * may have set it before this component, or even this bundle, existed.
 */
export function useTheme(): { theme: Theme; toggleTheme: () => void } {
  const [theme, setThemeState] = useState<Theme>(() => readDomTheme())

  useEffect(() => {
    setThemeState(readDomTheme())
  }, [])

  const toggleTheme = useCallback(() => {
    const next: Theme = theme === 'light' ? 'dark' : 'light'
    setTheme(next)
    setThemeState(next)
  }, [theme])

  return { theme, toggleTheme }
}
