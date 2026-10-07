import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Trash2 } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  BUTTON_SECONDARY,
  CARD_FORM,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELD_INPUT,
  FIELD_LABEL,
  PANEL,
} from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton } from '@/components/icon-button'
import { StatusChip } from '@/components/status-chip'
import { problemDetail } from '@/lib/api'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'
import {
  useDeleteWeatherSettings,
  useSaveWeatherSettings,
  useWeatherSettings,
  type WeatherSettingsFields,
  type WeatherUnitsValue,
} from '@/lib/weather'

const EMPTY_FIELDS: WeatherSettingsFields = { latitude: '', longitude: '', place: '', units: 'metric' }

const UNITS_WORD: Record<WeatherUnitsValue, string> = {
  metric: 'Metric · °C, km/h',
  imperial: 'Imperial · °F, mph',
}

/*
 * One grid for the head and the single row, so they line up. Below `lg` the row folds as the
 * Probes page's does: place and state on the first line, coordinates and units on the second, the
 * action on the third.
 */
const ROW_GRID =
  'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[116px_minmax(0,1fr)_minmax(0,1fr)_160px_64px] lg:gap-x-3.5'

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
  const [isConfirmingRemove, setIsConfirmingRemove] = useState(false)

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

  const current = settings.data ?? null

  return (
    <>
      <AdminPageHeader
        title="Weather"
        description="One location for the whole household. Paste its coordinates; there is no place search."
      >
        <Link to="/weather" className={BUTTON_SECONDARY}>
          Weather page
        </Link>
      </AdminPageHeader>
      <AdminSection id="weather-location" heading="Location" meta="Feeds the dashboard widget and the weather page">
        {current ? (
          <div className={PANEL}>
            <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
              <span>State</span>
              <span>Place</span>
              <span>Coordinates</span>
              <span>Units</span>
              <span className="text-right">Actions</span>
            </div>
            <ul aria-label="Location" className="flex list-none flex-col">
              <li>
                <div className={`${ROW_GRID} px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1`}>
                  <span className="col-start-2 row-start-1 flex lg:col-start-auto lg:row-start-auto">
                    <StatusChip state="up" word="Set" />
                  </span>
                  <span className="col-start-1 row-start-1 min-w-0 truncate text-[15px] font-semibold lg:col-start-auto lg:row-start-auto">
                    {current.place || '—'}
                  </span>
                  {/* One line under the place below lg; two cells of the grid at lg (`contents`). */}
                  <span className="col-span-2 flex flex-wrap gap-x-3 text-[13px] text-muted lg:contents">
                    <span className="mono">
                      {current.latitude.toFixed(4)}, {current.longitude.toFixed(4)}
                    </span>
                    <span>{UNITS_WORD[current.units]}</span>
                  </span>
                  <span className="col-span-2 flex items-center justify-end lg:col-span-1">
                    <IconButton
                      icon={Trash2}
                      tone="danger"
                      label="Remove location"
                      title="Remove"
                      onClick={() => setIsConfirmingRemove((open) => !open)}
                      aria-expanded={isConfirmingRemove}
                      aria-controls={isConfirmingRemove ? 'weather-confirm-remove' : undefined}
                    />
                  </span>
                </div>
                {isConfirmingRemove ? (
                  <ConfirmStrip
                    id="weather-confirm-remove"
                    question="Remove the location? The dashboard shows no weather until one is set."
                    confirmText="Remove"
                    confirmLabel="Confirm remove location"
                    cancelLabel="Cancel remove location"
                    onConfirm={() => deleteSettings.mutate(undefined, { onSuccess: () => setIsConfirmingRemove(false) })}
                    onCancel={() => setIsConfirmingRemove(false)}
                    isPending={deleteSettings.isPending}
                  />
                ) : null}
              </li>
            </ul>
          </div>
        ) : settings.isSuccess ? (
          <p className={EMPTY_STATE}>No location yet, so the dashboard shows no weather.</p>
        ) : null}
      </AdminSection>
      {deleteSettings.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(deleteSettings.error) ?? 'Could not remove the location. Try again.'}
        </p>
      ) : null}
      {/*
        Always on screen, set or not (plan 025's D11): the canvas drew it behind an Edit button, but
        e2e/weather.spec.ts fills Latitude on a fresh install and reads the values back after a reload.
      */}
      <form onSubmit={onSubmit} aria-labelledby="weather-form-heading" className={CARD_FORM}>
        <h2 id="weather-form-heading" className="text-[15px] font-semibold">
          {current ? 'Change the location' : 'Set the location'}
        </h2>
        <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,180px),1fr))] gap-4">
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
        </div>
        {saveSettings.isError ? (
          <p role="alert" className={ALERT}>
            {problemDetail(saveSettings.error) ?? 'Could not save the location. Try again.'}
          </p>
        ) : null}
        <p className="flex flex-wrap items-center gap-2">
          <button type="submit" disabled={saveSettings.isPending} className={BUTTON_PRIMARY}>
            Save location
          </button>
          <span className="text-[13px] text-muted">
            Decimal degrees: latitude −90 to 90, longitude −180 to 180.
          </span>
        </p>
      </form>
    </>
  )
}
