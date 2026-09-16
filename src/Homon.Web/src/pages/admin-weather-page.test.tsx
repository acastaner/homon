import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminWeatherPage } from '@/pages/admin-weather-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminWeatherPage', () => {
  it('submitting valid fields issues PUT /weather/settings with the parsed numeric body', async () => {
    const calls = stubFetch({
      '/api/v1/weather/settings': { status: 204 },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminWeatherPage />)

    await waitFor(() => expect(screen.getByLabelText('Latitude')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Latitude'), '51.5')
    await user.type(screen.getByLabelText('Longitude'), '-0.12')
    await user.type(screen.getByLabelText('Place (optional)'), 'Test location')
    await user.selectOptions(screen.getByLabelText('Units'), 'imperial')
    await user.click(screen.getByRole('button', { name: 'Save location' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/weather/settings' && call.init?.method === 'PUT')).toBe(
        true,
      ),
    )

    const put = calls.find((call) => call.path === '/api/v1/weather/settings' && call.init?.method === 'PUT')
    expect(JSON.parse(String(put?.init?.body))).toEqual({
      latitude: 51.5,
      longitude: -0.12,
      place: 'Test location',
      units: 'imperial',
    })
  })

  it('shows "Remove location" only once a location exists, and it issues DELETE with an empty body', async () => {
    const calls = stubFetch({
      '/api/v1/weather/settings': {
        body: { latitude: 51.5, longitude: -0.12, place: 'Test location', units: 'metric' },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminWeatherPage />)

    const removeButton = await screen.findByRole('button', { name: 'Remove location' })

    await user.click(removeButton)

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/weather/settings' && call.init?.method === 'DELETE')).toBe(
        true,
      ),
    )

    const deleteCall = calls.find((call) => call.path === '/api/v1/weather/settings' && call.init?.method === 'DELETE')
    expect(deleteCall?.init?.body).toBe('{}')
  })

  it('does not show "Remove location" before any settings exist', async () => {
    stubFetch({ '/api/v1/weather/settings': { status: 204 } })

    renderWithProviders(<AdminWeatherPage />)

    await waitFor(() => expect(screen.getByLabelText('Latitude')).toBeInTheDocument())

    expect(screen.queryByRole('button', { name: 'Remove location' })).not.toBeInTheDocument()
  })
})
