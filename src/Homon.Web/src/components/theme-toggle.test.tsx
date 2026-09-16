import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { ThemeToggle } from '@/components/theme-toggle'
import { THEME_STORAGE_KEY } from '@/lib/theme'
import { renderWithProviders } from '@/test/render'

beforeEach(() => {
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
})

afterEach(() => {
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
})

describe('ThemeToggle', () => {
  it('offers a button naming the action, not the current state', () => {
    renderWithProviders(<ThemeToggle />)

    expect(screen.getByRole('button', { name: /switch to (light|dark) theme/i })).toBeInTheDocument()
  })

  it('clicking it flips the DOM attribute and persists the choice', async () => {
    const user = userEvent.setup()
    renderWithProviders(<ThemeToggle />)

    await user.click(screen.getByRole('button', { name: 'Switch to light theme' }))

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(screen.getByRole('button', { name: 'Switch to dark theme' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Switch to dark theme' }))

    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })
})
