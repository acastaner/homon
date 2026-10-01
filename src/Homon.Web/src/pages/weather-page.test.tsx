import { afterEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'

import { WeatherPage } from '@/pages/weather-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

/** One day of the payload — ten fields since plan 020, few of which any assertion needs. */
function day(
  date: string,
  condition: string,
  high: number,
  low: number,
  overrides: Record<string, unknown> = {},
) {
  return {
    date,
    condition,
    high,
    low,
    precipitationSum: 2,
    snowfallSum: 0,
    windSpeedMax: 18,
    windGustsMax: 30,
    sunrise: '07:10',
    sunset: '18:53',
    ...overrides,
  }
}

/** One hourly row. `hour` is the clock string the table prints verbatim. */
function hour(time: string, overrides: Record<string, unknown> = {}) {
  return {
    date: '2026-01-01',
    time,
    condition: 'clear',
    temperature: 16,
    apparentTemperature: 15,
    windSpeed: 12,
    windGusts: 20,
    precipitationProbability: 10,
    ...overrides,
  }
}

/** 24 hourly rows, 00:00 through 23:00 — the full window the API sends. */
const twentyFourHours = Array.from({ length: 24 }, (_, index) =>
  hour(`${String(index).padStart(2, '0')}:00`),
)

const weatherBody = {
  place: 'Test location',
  units: 'metric',
  current: { temperature: 16.4, apparentTemperature: 15.1, windSpeed: 14.2, condition: 'rain', isDay: true },
  today: day('2026-01-01', 'rain', 18, 9, { precipitationSum: 7 }),
  forecast: [
    day('2026-01-02', 'cloudy', 17, 11),
    day('2026-01-03', 'rain', 14, 10),
    day('2026-01-04', 'partlyCloudy', 16, 8),
    day('2026-01-05', 'clear', 19, 7),
    day('2026-01-06', 'clear', 20, 9),
    day('2026-01-07', 'cloudy', 15, 9),
    day('2026-01-08', 'drizzle', 13, 8),
  ],
  hourly: twentyFourHours,
  warnings: [],
  fetchedAt: '2026-01-01T12:58:00Z',
  stale: false,
}

function render(body: unknown = weatherBody, status?: number) {
  stubFetch({ '/api/v1/weather': status == null ? { body } : { status, body } })
  renderWithProviders(<WeatherPage />, { initialEntries: ['/weather'] })
}

describe('WeatherPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('summarises today above the tables', async () => {
    render()

    expect(await screen.findByRole('heading', { level: 1, name: 'Weather' })).toBeInTheDocument()

    const today = screen.getByRole('region', { name: 'Today' })

    expect(within(today).getByText(/16°C/)).toBeInTheDocument()
    expect(within(today).getByText(/18° \/ 9°/)).toBeInTheDocument()
    expect(within(today).getByText(/high 18°C, low 9°C/)).toBeInTheDocument()
    expect(within(today).getByText('15°C')).toBeInTheDocument()
    expect(within(today).getByText('14 km/h')).toBeInTheDocument()
    expect(within(today).getByText('7 mm')).toBeInTheDocument()
    expect(within(today).getByText('07:10 / 18:53')).toBeInTheDocument()
  })

  it('names the place beside the heading rather than inside it', async () => {
    render()

    // The h1's accessible name is the word alone — the e2e specs match it exactly, and the
    // place is household data.
    expect(await screen.findByRole('heading', { level: 1, name: 'Weather' })).toBeInTheDocument()
    expect(screen.getByText(/Test location/)).toBeInTheDocument()
  })

  it('shows six hourly rows and steps to twenty-four, six at a time', async () => {
    const user = userEvent.setup()
    render()

    const hourly = await screen.findByRole('table', { name: 'Hourly forecast' })

    const rows = () => within(within(hourly).getAllByRole('rowgroup')[1]).getAllByRole('row')
    expect(rows()).toHaveLength(6)

    await user.click(screen.getByRole('button', { name: 'Show 6 more hours — 6 of 24' }))
    expect(rows()).toHaveLength(12)

    await user.click(screen.getByRole('button', { name: 'Show 6 more hours — 12 of 24' }))
    await user.click(screen.getByRole('button', { name: 'Show 6 more hours — 18 of 24' }))
    expect(rows()).toHaveLength(24)

    // Everything is shown, so the affordance goes away rather than sitting there inert.
    expect(screen.queryByRole('button', { name: /more hour/ })).not.toBeInTheDocument()
  })

  it('offers only the hours that remain when fewer than six are left', async () => {
    const user = userEvent.setup()
    render({ ...weatherBody, hourly: twentyFourHours.slice(0, 8) })

    await screen.findByRole('table', { name: 'Hourly forecast' })

    await user.click(screen.getByRole('button', { name: 'Show 2 more hours — 6 of 8' }))

    expect(screen.queryByRole('button', { name: /more hour/ })).not.toBeInTheDocument()
  })

  it('tabulates the next seven days', async () => {
    render()

    const daily = await screen.findByRole('table', { name: 'Daily forecast' })
    const rows = within(within(daily).getAllByRole('rowgroup')[1]).getAllByRole('row')

    expect(rows).toHaveLength(7)
    expect(within(rows[0]).getByText(/17° \/ 11°/)).toBeInTheDocument()
    expect(within(rows[6]).getByText(/13° \/ 8°/)).toBeInTheDocument()
  })

  it('banners every advisory with its severity word, not colour alone', async () => {
    render({
      ...weatherBody,
      warnings: [
        {
          kind: 'wind',
          severity: 'severe',
          value: 94,
          date: '2026-01-01',
          fromTime: '14:00',
          toTime: '16:00',
        },
        { kind: 'rain', severity: 'caution', value: 28, date: '2026-01-02', fromTime: null, toTime: null },
      ],
    })

    const banners = await screen.findByRole('list', { name: 'Weather warnings' })
    const items = within(banners).getAllByRole('listitem')

    expect(items).toHaveLength(2)
    expect(within(items[0]).getByText('Severe')).toBeInTheDocument()
    expect(within(items[0]).getByText(/Gale-force wind/)).toBeInTheDocument()
    expect(within(items[0]).getByText(/Gusts to 94 km\/h expected between 14:00 and 16:00/)).toBeInTheDocument()

    expect(within(items[1]).getByText('Caution')).toBeInTheDocument()
    expect(within(items[1]).getByText(/28 mm of rain forecast/)).toBeInTheDocument()
  })

  it('shows the gust in the wind cell it colours, not the mean wind', async () => {
    render({
      ...weatherBody,
      hourly: [hour('13:00', { windSpeed: 12, windGusts: 94 }), hour('14:00', { windSpeed: 12, windGusts: 20 })],
      warnings: [
        { kind: 'wind', severity: 'severe', value: 94, date: '2026-01-01', fromTime: '13:00', toTime: '13:00' },
      ],
    })

    const body = within(await screen.findByRole('table', { name: 'Hourly forecast' })).getAllByRole(
      'rowgroup',
    )[1]
    const rows = within(body).getAllByRole('row')

    // The advisory is about the gust, so the coloured cell has to show the gust — otherwise a
    // reader sees a calm 12 km/h in red and the banner and the table appear to disagree.
    expect(within(rows[0]).getByText(/12 km\/h · gusts 94/)).toBeInTheDocument()

    // The untouched hour keeps the plain mean.
    expect(within(rows[1]).getByText('12 km/h')).toBeInTheDocument()
  })

  it('does not let one kind of advisory colour another kind of figure', async () => {
    render({
      ...weatherBody,
      hourly: [hour('13:00', { windSpeed: 12, windGusts: 20 })],
      forecast: [day('2026-01-02', 'clear', 35, 20, { precipitationSum: 1 })],
      warnings: [
        // A thunderstorm covering the hour, and a heat advisory covering the day. Neither is
        // about wind or rain, so neither may colour the wind or rain cell.
        { kind: 'thunderstorm', severity: 'severe', value: null, date: '2026-01-01', fromTime: '13:00', toTime: '13:00' },
        { kind: 'heat', severity: 'caution', value: 35, date: '2026-01-02', fromTime: null, toTime: null },
      ],
    })

    const hourly = within(await screen.findByRole('table', { name: 'Hourly forecast' })).getAllByRole(
      'rowgroup',
    )[1]
    expect(within(hourly).getByText('12 km/h')).not.toHaveClass('text-down')

    const daily = within(screen.getByRole('table', { name: 'Daily forecast' })).getAllByRole('rowgroup')[1]
    expect(within(daily).getByText('1 mm')).not.toHaveClass('text-unstable')
  })

  it('renders no advisory list when nothing trips', async () => {
    render()

    await screen.findByRole('heading', { level: 1, name: 'Weather' })

    expect(screen.queryByRole('list', { name: 'Weather warnings' })).not.toBeInTheDocument()
  })

  it('shows an em dash for a reading the forecast model did not report', async () => {
    render({
      ...weatherBody,
      today: day('2026-01-01', 'rain', 18, 9, { precipitationSum: null, sunrise: null, sunset: null }),
      hourly: [hour('13:00', { precipitationProbability: null })],
    })

    const today = await screen.findByRole('region', { name: 'Today' })

    expect(within(today).getAllByText('—').length).toBeGreaterThan(0)
  })

  it('credits Open-Meteo and says when the forecast was fetched', async () => {
    render()

    expect(await screen.findByRole('link', { name: 'Open-Meteo.com' })).toHaveAttribute(
      'href',
      'https://open-meteo.com/',
    )
    expect(screen.getByText(/Forecast fetched/)).toBeInTheDocument()
  })

  it('says so when the forecast could not be refreshed', async () => {
    render({ ...weatherBody, stale: true })

    expect(await screen.findByText(/a refresh failed, so this may be out of date/)).toBeInTheDocument()
  })

  it('points at the admin page when no location is configured', async () => {
    render(undefined, 204)

    expect(await screen.findByText(/No weather location yet/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Weather' })).toHaveAttribute('href', '/admin/weather')
  })

  it('shows the same unavailable sentence the dashboard widget shows, on a 503', async () => {
    render({ title: 'Weather unavailable', detail: 'No recent answer is available.' }, 503)

    expect(await screen.findByText('Weather is temporarily unavailable.')).toBeInTheDocument()
  })
})
