import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'

import { PagePage } from '@/pages/page-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PagePage', () => {
  it('renders the fetched title and body', async () => {
    stubFetch({
      '/api/v1/pages/welcome': {
        body: {
          id: 'page-1',
          slug: 'welcome',
          title: 'Welcome',
          bodyHtml: '<p>Hello, family.</p>',
          isPublished: true,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      },
    })

    renderWithProviders(
      <Routes>
        <Route path="/pages/:slug" element={<PagePage />} />
      </Routes>,
      { initialEntries: ['/pages/welcome'] },
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome' })).toBeInTheDocument()
    expect(screen.getByText('Hello, family.')).toBeInTheDocument()
  })

  it("renders 'Page not found' with the problem's detail for an unknown slug", async () => {
    stubFetch({
      '/api/v1/pages/no-such-page': {
        status: 404,
        body: { detail: 'This page does not exist.' },
      },
    })

    renderWithProviders(
      <Routes>
        <Route path="/pages/:slug" element={<PagePage />} />
      </Routes>,
      { initialEntries: ['/pages/no-such-page'] },
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByText('This page does not exist.')).toBeInTheDocument()
  })
})
