import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'

import { StatusChip, type StatusChipState } from '@/components/status-chip'

const CASES: { state: StatusChipState; word: string }[] = [
  { state: 'up', word: 'Up' },
  { state: 'unstable', word: 'Unstable' },
  { state: 'down', word: 'Down' },
  { state: 'unknown', word: 'Unknown' },
  { state: 'paused', word: 'Paused' },
]

describe('StatusChip', () => {
  it.each(CASES)('renders the word and a decorative glyph for $state', ({ state, word }) => {
    render(<StatusChip state={state} />)

    // The word is always visible text — "Status is not colour alone" (docs/design-brief.md).
    expect(screen.getByText(word)).toBeInTheDocument()

    // The glyph is an SVG the screen reader never announces on its own — the word carries
    // the state, never colour or the icon alone.
    const glyph = document.querySelector('svg[aria-hidden="true"]')
    expect(glyph).toBeInTheDocument()
  })

  it('an explicit word overrides the state default without dropping the glyph', () => {
    render(<StatusChip state="up" word="Succeeded" />)

    expect(screen.getByText('Succeeded')).toBeInTheDocument()
    expect(screen.queryByText('Up')).not.toBeInTheDocument()
    expect(document.querySelector('svg[aria-hidden="true"]')).toBeInTheDocument()
  })
})
