import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminLinksPage } from '@/pages/admin-links-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const threeLinks = {
  '/api/v1/links': {
    body: [
      { id: 'link-1', title: 'NAS', url: 'https://nas.invalid', description: null, createdAt: '', updatedAt: '' },
      { id: 'link-2', title: 'Router', url: 'https://router.invalid', description: null, createdAt: '', updatedAt: '' },
      { id: 'link-3', title: 'Recipes', url: 'https://recipes.invalid', description: null, createdAt: '', updatedAt: '' },
    ],
  },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminLinksPage', () => {
  it('the move button sends the swapped order to PUT /links/order', async () => {
    const calls = stubFetch({ ...threeLinks, '/api/v1/links/order': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminLinksPage />)

    await waitFor(() => expect(screen.getByText('NAS')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Move NAS down' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/links/order' && call.init?.method === 'PUT')).toBe(true),
    )

    const orderCall = calls.find((call) => call.path === '/api/v1/links/order')
    expect(JSON.parse(String(orderCall?.init?.body))).toEqual({
      linkIds: ['link-2', 'link-1', 'link-3'],
    })
  })

  it('the add-link form posts the trimmed field values', async () => {
    const calls = stubFetch({ '/api/v1/links': { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminLinksPage />)

    await waitFor(() => expect(screen.getByLabelText('Title')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Title'), '  NAS  ')
    await user.type(screen.getByLabelText('URL'), 'https://nas.invalid')
    await user.type(screen.getByLabelText('Description'), '  Storage box  ')
    await user.click(screen.getByRole('button', { name: 'Add link' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/links' && call.init?.method === 'POST')).toBe(true),
    )

    const post = calls.find((call) => call.path === '/api/v1/links' && call.init?.method === 'POST')
    expect(JSON.parse(String(post?.init?.body))).toEqual({
      title: 'NAS',
      url: 'https://nas.invalid',
      description: 'Storage box',
    })
  })

  it('Delete asks first: nothing is sent until Confirm delete', async () => {
    const calls = stubFetch({ ...threeLinks, '/api/v1/links/link-1': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminLinksPage />)

    await waitFor(() => expect(screen.getByText('NAS')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Delete NAS' }))

    expect(calls.some((call) => call.init?.method === 'DELETE')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Confirm delete NAS' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/links/link-1' && call.init?.method === 'DELETE')).toBe(true),
    )
  })

  it('Edit opens the form inside the row, and Cancel brings back the add form', async () => {
    stubFetch(threeLinks)
    const user = userEvent.setup()

    renderWithProviders(<AdminLinksPage />)

    await waitFor(() => expect(screen.getByText('NAS')).toBeInTheDocument())
    expect(screen.getByRole('form', { name: 'New link' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Edit NAS' }))

    const row = screen.getAllByRole('listitem')[0]

    expect(within(row).getByRole('form', { name: 'Edit NAS' })).toBeInTheDocument()
    expect(screen.getAllByRole('form')).toHaveLength(1)
    expect(screen.queryByRole('form', { name: 'New link' })).not.toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: 'Cancel' }))

    expect(screen.getByRole('form', { name: 'New link' })).toBeInTheDocument()
    expect(screen.getAllByRole('form')).toHaveLength(1)
  })
})
