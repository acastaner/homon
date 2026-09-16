import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminPageEditorPage } from '@/pages/admin-page-editor-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const TOOLBAR_BUTTON_NAMES = [
  'Bold',
  'Italic',
  'Strikethrough',
  'Code',
  'Heading 2',
  'Heading 3',
  'Heading 4',
  'Bullet list',
  'Numbered list',
  'Blockquote',
  'Link',
  'Horizontal rule',
  'Undo',
  'Redo',
]

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminPageEditorPage (new)', () => {
  it('every toolbar button exists, named after its action', () => {
    stubFetch({ '/api/v1/admin/pages': { body: [] } })

    renderWithProviders(<AdminPageEditorPage />)

    for (const name of TOOLBAR_BUTTON_NAMES) {
      expect(screen.getByRole('button', { name })).toBeInTheDocument()
    }
  })

  it("the Body editor has the accessible name 'Body'", () => {
    stubFetch({ '/api/v1/admin/pages': { body: [] } })

    renderWithProviders(<AdminPageEditorPage />)

    expect(screen.getByRole('textbox', { name: 'Body' })).toBeInTheDocument()
  })

  it('the slug field auto-fills from the title until it is hand-edited', async () => {
    stubFetch({ '/api/v1/admin/pages': { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminPageEditorPage />)

    await user.type(screen.getByLabelText('Title'), 'How to Connect')

    await waitFor(() => expect(screen.getByLabelText('Slug')).toHaveValue('how-to-connect'))

    // Once the admin hand-edits the slug, further title changes must not overwrite it.
    await user.clear(screen.getByLabelText('Slug'))
    await user.type(screen.getByLabelText('Slug'), 'custom-slug')
    await user.type(screen.getByLabelText('Title'), ' updated')

    expect(screen.getByLabelText('Slug')).toHaveValue('custom-slug')
  })
})
