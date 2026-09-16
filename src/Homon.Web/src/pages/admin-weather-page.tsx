import { useEffect, useState, type FormEvent } from 'react'

import { problemDetail } from '@/lib/api'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'
import {
  useDeleteWeatherSettings,
  useSaveWeatherSettings,
  useWeatherSettings,
  type WeatherSettingsFields,
} from '@/lib/weather'

const EMPTY_FIELDS: WeatherSettingsFields = { latitude: '', longitude: '', place: '', units: 'metric' }

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'

/**
 * The administrator's one-location form: latitude/longitude pasted in directly (no
 * geocoding search — plan 010's Decisions), an optional place label, and the unit system
 * the forecast is reported in.
 */
export function AdminWeatherPage() {
  useDocumentTitle(pageTitle('Weather', 'Admin'))

  const settings = useWeatherSettings()
  const saveSettings = useSaveWeatherSettings()
  const deleteSettings = useDeleteWeatherSettings()

  const [fields, setFields] = useState<WeatherSettingsFields>(EMPTY_FIELDS)

  // Fills the form once the current settings load — an admin editing an already-set
  // location sees its values, not a blank form.
  useEffect(() => {
    if (settings.data) {
      setFields({
        latitude: String(settings.data.latitude),
        longitude: String(settings.data.longitude),
        place: settings.data.place ?? '',
        units: settings.data.units,
      })
    }
  }, [settings.data])

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    saveSettings.mutate(fields)
  }

  return (
    <>
      <h1 className={PAGE_H1}>Weather</h1>
      <form onSubmit={onSubmit} aria-labelledby="weather-form-heading" className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5">
        <p id="weather-form-heading" className="text-[13px] font-medium text-muted">
          The household's location
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="weather-latitude" className={FIELD_LABEL}>
            Latitude
          </label>
          <input
            id="weather-latitude"
            type="number"
            step="any"
            min={-90}
            max={90}
            required
            value={fields.latitude}
            onChange={(event) => setFields({ ...fields, latitude: event.target.value })}
            className={`${FIELD_INPUT} mono`}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="weather-longitude" className={FIELD_LABEL}>
            Longitude
          </label>
          <input
            id="weather-longitude"
            type="number"
            step="any"
            min={-180}
            max={180}
            required
            value={fields.longitude}
            onChange={(event) => setFields({ ...fields, longitude: event.target.value })}
            className={`${FIELD_INPUT} mono`}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="weather-place" className={FIELD_LABEL}>
            Place (optional)
          </label>
          <input
            id="weather-place"
            value={fields.place}
            onChange={(event) => setFields({ ...fields, place: event.target.value })}
            className={FIELD_INPUT}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="weather-units" className={FIELD_LABEL}>
            Units
          </label>
          <select
            id="weather-units"
            value={fields.units}
            onChange={(event) => setFields({ ...fields, units: event.target.value as WeatherSettingsFields['units'] })}
            className={FIELD_INPUT}
          >
            <option value="metric">Metric (°C, km/h)</option>
            <option value="imperial">Imperial (°F, mph)</option>
          </select>
        </p>
        {saveSettings.isError ? (
          <p role="alert" className={ALERT}>
            {problemDetail(saveSettings.error) ?? 'Could not save the location. Try again.'}
          </p>
        ) : null}
        <p>
          <button type="submit" disabled={saveSettings.isPending} className={BUTTON_PRIMARY}>
            Save location
          </button>
        </p>
      </form>
      {settings.data ? (
        <p>
          <button type="button" onClick={() => deleteSettings.mutate()} disabled={deleteSettings.isPending} className={BUTTON_SECONDARY}>
            Remove location
          </button>
        </p>
      ) : null}
      {deleteSettings.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(deleteSettings.error) ?? 'Could not remove the location. Try again.'}
        </p>
      ) : null}
    </>
  )
}
