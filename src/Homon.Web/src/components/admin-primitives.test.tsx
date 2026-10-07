import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Pencil } from 'lucide-react'

import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton, MoveButtons } from '@/components/icon-button'
import { KeyReveal } from '@/components/key-reveal'

describe('IconButton', () => {
  it('exposes its label as the accessible name and keeps aria-expanded', () => {
    render(<IconButton icon={Pencil} label="Edit NAS" title="Edit" aria-expanded={true} />)

    const button = screen.getByRole('button', { name: 'Edit NAS' })

    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(button).toHaveAttribute('title', 'Edit')
    // type="button": the page editor's toolbar sits inside a <form> and must not submit it.
    expect(button).toHaveAttribute('type', 'button')
  })
})

describe('MoveButtons', () => {
  it('disables up on the first row and down on the last', () => {
    const { rerender } = render(<MoveButtons name="NAS" isFirst isLast={false} onMove={() => undefined} />)

    expect(screen.getByRole('button', { name: 'Move NAS up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move NAS down' })).toBeEnabled()

    rerender(<MoveButtons name="NAS" isFirst={false} isLast onMove={() => undefined} />)

    expect(screen.getByRole('button', { name: 'Move NAS up' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Move NAS down' })).toBeDisabled()
  })
})

describe('ConfirmStrip', () => {
  it('shows the question and calls onConfirm from the button named by confirmLabel', async () => {
    const onConfirm = vi.fn()
    const user = userEvent.setup()

    render(
      <ConfirmStrip
        id="strip"
        question="Delete NAS?"
        confirmLabel="Confirm delete NAS"
        cancelLabel="Cancel delete NAS"
        onConfirm={onConfirm}
        onCancel={() => undefined}
      />,
    )

    expect(screen.getByText('Delete NAS?')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Confirm delete NAS' }))

    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('a neutral strip without onConfirm offers only Close', () => {
    render(
      <ConfirmStrip
        id="strip"
        tone="neutral"
        question="A probe watches this reporter."
        confirmLabel="Confirm delete NAS"
        cancelLabel="Close the explanation for NAS"
        onCancel={() => undefined}
      />,
    )

    expect(screen.getByRole('button', { name: 'Close the explanation for NAS' })).toHaveTextContent('Close')
    expect(screen.queryByRole('button', { name: 'Confirm delete NAS' })).not.toBeInTheDocument()
  })
})

describe('KeyReveal', () => {
  it('is an alert that labels the token input with the key name', () => {
    render(<KeyReveal inputId="revealed-key" name="NAS" token="hmn_secret" onDone={() => undefined} />)

    expect(screen.getByRole('alert')).toHaveTextContent('This key will not be shown again. Store it now.')
    expect(screen.getByLabelText('API key for NAS')).toHaveValue('hmn_secret')
    expect(screen.getByRole('button', { name: 'Done' })).toBeInTheDocument()
  })
})
