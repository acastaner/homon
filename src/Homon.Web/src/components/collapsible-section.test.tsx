import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { CollapsibleSection } from '@/components/collapsible-section'
import { renderWithProviders } from '@/test/render'

describe('CollapsibleSection', () => {
  it('is a named region with its content showing when expanded', () => {
    renderWithProviders(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={false} onToggle={() => {}}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('region', { name: 'Hosts' })).toBeInTheDocument()
    expect(screen.getByText('Body')).toBeVisible()
  })

  it("the heading's text is exactly the heading", () => {
    renderWithProviders(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={false} onToggle={() => {}}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent(/^Hosts$/)
  })

  it('aria-expanded follows collapsed', () => {
    const { rerender } = renderWithProviders(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={false} onToggle={() => {}}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('button', { name: 'Hosts' })).toHaveAttribute('aria-expanded', 'true')

    rerender(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={true} onToggle={() => {}}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('button', { name: 'Hosts' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('collapsed hides the body and shows the summary, outside the heading', () => {
    renderWithProviders(
      <CollapsibleSection
        headingId="hosts-heading"
        heading="Hosts"
        collapsed={true}
        onToggle={() => {}}
        summary="1 down · 4 up"
      >
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByText('Body')).not.toBeVisible()
    expect(screen.getByText('1 down · 4 up')).toBeVisible()
    expect(screen.getByRole('heading', { level: 2 })).not.toHaveTextContent('down')
  })

  it('clicking calls onToggle once', async () => {
    const user = userEvent.setup()
    const onToggle = vi.fn()
    renderWithProviders(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={false} onToggle={onToggle}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    await user.click(screen.getByRole('button', { name: 'Hosts' }))

    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('renders no move buttons without arrange controls', () => {
    renderWithProviders(
      <CollapsibleSection headingId="hosts-heading" heading="Hosts" collapsed={false} onToggle={() => {}}>
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getAllByRole('button')).toHaveLength(1)
  })

  it('arrange controls are named for the label, not for the heading', () => {
    renderWithProviders(
      <CollapsibleSection
        headingId="weather-heading"
        heading="Weather · Kitchen"
        label="Weather"
        collapsed={false}
        onToggle={() => {}}
        arrange={{ canMoveUp: true, canMoveDown: true, onMoveUp: () => {}, onMoveDown: () => {} }}
      >
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('button', { name: 'Move Weather up' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move Weather down' })).toBeInTheDocument()
  })

  it("the heading's text is still exactly the heading in arrange mode", () => {
    renderWithProviders(
      <CollapsibleSection
        headingId="hosts-heading"
        heading="Hosts"
        collapsed={false}
        onToggle={() => {}}
        arrange={{ canMoveUp: true, canMoveDown: true, onMoveUp: () => {}, onMoveDown: () => {} }}
      >
        <p>Body</p>
      </CollapsibleSection>,
    )

    // The guard for docs/ARCHITECTURE.md §3.22's markup contract: e2e/dashboard-groups.spec.ts
    // matches every level-2 heading's textContent exactly, and the region's accessible name comes
    // from this heading through aria-labelledby.
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent(/^Hosts$/)
    expect(screen.getByRole('region', { name: 'Hosts' })).toBeInTheDocument()
  })

  it('canMoveUp false disables the up button and leaves the down button alone', () => {
    renderWithProviders(
      <CollapsibleSection
        headingId="hosts-heading"
        heading="Hosts"
        collapsed={false}
        onToggle={() => {}}
        arrange={{ canMoveUp: false, canMoveDown: true, onMoveUp: () => {}, onMoveDown: () => {} }}
      >
        <p>Body</p>
      </CollapsibleSection>,
    )

    expect(screen.getByRole('button', { name: 'Move Hosts up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move Hosts down' })).toBeEnabled()
  })

  it('clicking a move button calls its handler once and does not toggle', async () => {
    const user = userEvent.setup()
    const onToggle = vi.fn()
    const onMoveUp = vi.fn()
    renderWithProviders(
      <CollapsibleSection
        headingId="hosts-heading"
        heading="Hosts"
        collapsed={false}
        onToggle={onToggle}
        arrange={{ canMoveUp: true, canMoveDown: true, onMoveUp, onMoveDown: () => {} }}
      >
        <p>Body</p>
      </CollapsibleSection>,
    )

    await user.click(screen.getByRole('button', { name: 'Move Hosts up' }))

    expect(onMoveUp).toHaveBeenCalledTimes(1)
    expect(onToggle).not.toHaveBeenCalled()
  })
})
