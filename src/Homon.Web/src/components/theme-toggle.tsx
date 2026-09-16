import { Moon, Sun } from 'lucide-react'

import { useTheme } from '@/lib/theme'

/**
 * A real button, not a `<select>` — one click flips the scheme. The accessible name states
 * the action ("Switch to light theme" / "Switch to dark theme"), not the current state, so
 * `getByRole('button', { name: … })` in `e2e/theme.spec.ts` is unambiguous regardless of
 * which scheme is active when the test runs.
 */
export function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()
  const label = theme === 'light' ? 'Switch to dark theme' : 'Switch to light theme'

  return (
    <button
      type="button"
      onClick={toggleTheme}
      className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-line text-muted hover:border-line-strong hover:text-text"
    >
      {theme === 'light' ? <Moon aria-hidden="true" size={18} /> : <Sun aria-hidden="true" size={18} />}
      <span className="sr-only">{label}</span>
    </button>
  )
}
