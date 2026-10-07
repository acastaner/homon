import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminPagesPage } from '@/pages/admin-pages-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const pages = {
  '/api/v1/admin/pages': {
    body: [
      { id: 'page-1', slug: 'welcome', title: 'Welcome', isPublished: true, updatedAt: '2026-10-01T04:30:00+00:00' },
      { id: 'page-2', slug: 'wifi', title: 'Wi-Fi', isPublished: false, updatedAt: '2026-10-02T08:00:00+00:00' },
    ],
  },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminPagesPage', () => {
  it('lists drafts and published pages in separate regions', async () => {
    stubFetch(pages)

    renderWithProviders(<AdminPagesPage />)

    const published = await screen.findByRole('region', { name: 'Published' })
    const drafts = screen.getByRole('region', { name: 'Drafts' })

    expect(within(published).getByText('Welcome')).toBeInTheDocument()
    expect(within(published).queryByText('Wi-Fi')).not.toBeInTheDocument()
    expect(within(drafts).getByText('Wi-Fi')).toBeInTheDocument()
    expect(within(drafts).queryByText('Welcome')).not.toBeInTheDocument()
  })

  it('Edit is a link to the editor route', async () => {
    stubFetch(pages)

    renderWithProviders(<AdminPagesPage />)

    const link = await screen.findByRole('link', { name: 'Edit Welcome' })

    expect(link).toHaveAttribute('href', '/admin/pages/page-1')
  })

  it('Delete asks first, then Confirm delete sends DELETE /pages/{id}', async () => {
    const calls = stubFetch({ ...pages, '/api/v1/pages/page-2': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminPagesPage />)

    await user.click(await screen.findByRole('button', { name: 'Delete Wi-Fi' }))

    expect(calls.some((call) => call.init?.method === 'DELETE')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Confirm delete Wi-Fi' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/pages/page-2' && call.init?.method === 'DELETE')).toBe(true),
    )
  })
})
