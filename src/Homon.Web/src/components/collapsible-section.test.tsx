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
})
