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
      <h1>Weather</h1>
      <form onSubmit={onSubmit} aria-labelledby="weather-form-heading">
        <p id="weather-form-heading">The household's location</p>
        <p>
          <label htmlFor="weather-latitude">Latitude</label>
          <input
            id="weather-latitude"
            type="number"
            step="any"
            min={-90}
            max={90}
            required
            value={fields.latitude}
            onChange={(event) => setFields({ ...fields, latitude: event.target.value })}
          />
        </p>
        <p>
          <label htmlFor="weather-longitude">Longitude</label>
          <input
            id="weather-longitude"
            type="number"
            step="any"
            min={-180}
            max={180}
            required
            value={fields.longitude}
            onChange={(event) => setFields({ ...fields, longitude: event.target.value })}
          />
        </p>
        <p>
          <label htmlFor="weather-place">Place (optional)</label>
          <input
            id="weather-place"
            value={fields.place}
            onChange={(event) => setFields({ ...fields, place: event.target.value })}
          />
        </p>
        <p>
          <label htmlFor="weather-units">Units</label>
          <select
            id="weather-units"
            value={fields.units}
            onChange={(event) => setFields({ ...fields, units: event.target.value as WeatherSettingsFields['units'] })}
          >
            <option value="metric">Metric (°C, km/h)</option>
            <option value="imperial">Imperial (°F, mph)</option>
          </select>
        </p>
        {saveSettings.isError ? (
          <p role="alert">{problemDetail(saveSettings.error) ?? 'Could not save the location. Try again.'}</p>
        ) : null}
        <p>
          <button type="submit" disabled={saveSettings.isPending}>
            Save location
          </button>
        </p>
      </form>
      {settings.data ? (
        <p>
          <button type="button" onClick={() => deleteSettings.mutate()} disabled={deleteSettings.isPending}>
            Remove location
          </button>
        </p>
      ) : null}
      {deleteSettings.isError ? (
        <p role="alert">{problemDetail(deleteSettings.error) ?? 'Could not remove the location. Try again.'}</p>
      ) : null}
    </>
  )
}
