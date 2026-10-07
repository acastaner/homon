import { useEffect, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Minus } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  BUTTON_SECONDARY,
  CARD_FORM,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELDSET,
  FIELD_INPUT,
  FIELD_LABEL,
  LEGEND,
  PANEL,
  ROW,
} from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { IconButton } from '@/components/icon-button'
import { Stamp } from '@/components/stamp'
import { StatusChip } from '@/components/status-chip'
import { countOf } from '@/lib/admin-summary'
import {
  formatDuration,
  useAlertDeliveries,
  useAlertSettings,
  useSaveAlertSettings,
  useSendTestAlert,
  type AlertDelivery,
  type AlertSettingsInput,
} from '@/lib/alerts'
import { problemDetail } from '@/lib/api'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/*
 * One grid for the history's head and rows, so they line up. Below `lg` a row folds as the other
 * admin pages' do: the probe and the event on the first line, when it happened on the second,
 * the delivery on the third.
 */
const HISTORY_GRID =
  'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[116px_minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.4fr)] lg:gap-x-3.5'

/**
 * Where alert email is set up: the Resend key, the sender, the recipients and the on/off switch,
 * then the newest alerts and what became of each (plan 026). The key is write-only: the page can
 * replace or remove it but never reads it back.
 */
export function AdminAlertsPage() {
  useDocumentTitle(pageTitle('Alerts', 'Admin'))

  const settings = useAlertSettings()
  const deliveries = useAlertDeliveries()
  const saveSettings = useSaveAlertSettings()
  const sendTest = useSendTestAlert()

  const [isEnabled, setIsEnabled] = useState(false)
  const [fromAddress, setFromAddress] = useState('')
  const [fromName, setFromName] = useState('Homon')
  const [recipients, setRecipients] = useState<string[]>([])
  const [newRecipient, setNewRecipient] = useState('')
  const [apiKey, setApiKey] = useState('')
  const hasStoredKey = settings.data?.hasApiKey ?? false
  // Reveal the key input at once unless there is a key to replace: a stored key is never
  // pre-filled, because the API never returns it.
  const [replaceKey, setReplaceKey] = useState(true)
  const showKeyInput = !hasStoredKey || replaceKey

  // Fills the form once the stored settings load, and again after each save (every save moves
  // `updatedAt`), which also puts the key input back to "A key is already set."
  useEffect(() => {
    if (settings.data) {
      setIsEnabled(settings.data.isEnabled)
      setFromAddress(settings.data.fromAddress)
      setFromName(settings.data.fromName)
      setRecipients(settings.data.recipients)
      setApiKey('')
      setReplaceKey(!settings.data.hasApiKey)
    }
  }, [settings.data])

  function addRecipient() {
    const address = newRecipient.trim()

    if (address.length === 0) {
      return
    }

    if (!recipients.some((existing) => existing.toLowerCase() === address.toLowerCase())) {
      setRecipients([...recipients, address])
    }

    setNewRecipient('')
  }

  function onRecipientKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    // Enter adds the address; it must not submit the settings form.
    if (event.key === 'Enter') {
      event.preventDefault()
      addRecipient()
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const input: AlertSettingsInput = {
      isEnabled,
      fromAddress: fromAddress.trim(),
      fromName: fromName.trim(),
      recipients,
      // Only while the input is shown: leaving it out keeps the stored key.
      ...(showKeyInput ? { apiKey: apiKey.trim() } : {}),
    }

    saveSettings.mutate(input)
  }

  const current = settings.data ?? null
  const isReady = current !== null && current.hasApiKey && current.fromAddress.length > 0 && current.recipients.length > 0
  const rows = deliveries.data ?? []

  return (
    <>
      <AdminPageHeader
        title="Alerts"
        description="Homon emails the people below when a probe goes down, and again when it is back up — with how long it was down."
      >
        <button
          type="button"
          onClick={() => sendTest.mutate()}
          disabled={sendTest.isPending}
          className={BUTTON_SECONDARY}
        >
          Send test email
        </button>
      </AdminPageHeader>
      {sendTest.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(sendTest.error) ?? 'Could not queue a test email. Try again.'}
        </p>
      ) : null}
      <AdminSection id="alerts-delivery" heading="Delivery" meta="Down and back-up events only; unstable never mails">
        {current ? (
          <div className={PANEL}>
            <ul aria-label="Delivery" className="flex list-none flex-col">
              <li className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1">
                {isReady && current.isEnabled ? (
                  <StatusChip state="up" word="On" />
                ) : isReady ? (
                  <StatusChip state="paused" word="Off" />
                ) : (
                  <StatusChip state="unknown" word="Not set up" />
                )}
                <span className="mono min-w-0 text-[13px] text-muted">
                  {current.fromAddress ? `${current.fromName} <${current.fromAddress}>` : 'No From address'}
                  {' · '}
                  {countOf(current.recipients.length, 'recipient')}
                  {' · '}
                  {current.hasApiKey ? 'Resend key set' : 'no Resend key'}
                </span>
              </li>
            </ul>
          </div>
        ) : null}
      </AdminSection>
      <form onSubmit={onSubmit} aria-labelledby="alerts-form-heading" className={CARD_FORM}>
        <h2 id="alerts-form-heading" className="text-[15px] font-semibold">
          Email settings
        </h2>
        <div className="flex flex-col gap-1">
          <label className="flex items-center gap-2 text-[14px] text-text">
            <input
              type="checkbox"
              checked={isEnabled}
              onChange={(event) => setIsEnabled(event.target.checked)}
              className="size-4 rounded border-line"
            />
            Send alert emails
          </label>
          <span className="text-[13px] text-muted">
            When off, nothing is mailed; down and up events are still listed below as skipped.
          </span>
        </div>
        <div className="flex flex-col gap-1">
          {showKeyInput ? (
            <>
              <label htmlFor="alerts-api-key" className={FIELD_LABEL}>
                Resend API key
              </label>
              <input
                id="alerts-api-key"
                type="password"
                autoComplete="off"
                value={apiKey}
                onChange={(event) => setApiKey(event.target.value)}
                className={`${FIELD_INPUT} mono`}
              />
              {hasStoredKey ? (
                <>
                  <span className="text-[13px] text-muted">Leave empty and save to remove the key.</span>
                  <button
                    type="button"
                    onClick={() => {
                      setReplaceKey(false)
                      setApiKey('')
                    }}
                    className={`${BUTTON_SECONDARY} w-fit`}
                  >
                    Keep the current key
                  </button>
                </>
              ) : null}
            </>
          ) : (
            <p className="text-[14px] text-muted">
              A key is already set.{' '}
              <button type="button" onClick={() => setReplaceKey(true)} className={`${BUTTON_SECONDARY} ml-1`}>
                Replace key
              </button>
            </p>
          )}
        </div>
        <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,220px),1fr))] gap-4">
          <p className="flex flex-col gap-1">
            <label htmlFor="alerts-from-address" className={FIELD_LABEL}>
              From address
            </label>
            <input
              id="alerts-from-address"
              type="email"
              required={isEnabled}
              value={fromAddress}
              onChange={(event) => setFromAddress(event.target.value)}
              className={`${FIELD_INPUT} mono`}
            />
          </p>
          <p className="flex flex-col gap-1">
            <label htmlFor="alerts-from-name" className={FIELD_LABEL}>
              From name
            </label>
            <input
              id="alerts-from-name"
              required
              value={fromName}
              onChange={(event) => setFromName(event.target.value)}
              className={FIELD_INPUT}
            />
          </p>
        </div>
        <span className="-mt-2 text-[13px] text-muted">The address must be on a domain verified in Resend.</span>
        {/* `min-w-0`: a fieldset's default minimum width is its content's, so one long address
            would otherwise stretch it past a phone's width instead of truncating. */}
        <fieldset className={`${FIELDSET} min-w-0`}>
          <legend className={LEGEND}>Recipients</legend>
          {recipients.length === 0 ? (
            <p className={EMPTY_STATE}>No recipient yet. Add an address below.</p>
          ) : (
            <ul aria-label="Recipients" className="flex list-none flex-col rounded-md border border-line bg-surface">
              {recipients.map((address) => (
                <li
                  key={address}
                  className="flex items-center gap-x-3 border-t border-line px-3 py-1.5 first:border-t-0"
                >
                  <span className="mono min-w-0 flex-1 truncate text-[13.5px]">{address}</span>
                  <IconButton
                    icon={Minus}
                    tone="danger"
                    label={`Remove ${address}`}
                    onClick={() => setRecipients(recipients.filter((existing) => existing !== address))}
                  />
                </li>
              ))}
            </ul>
          )}
          <div className="flex flex-col gap-1">
            <label htmlFor="alerts-new-recipient" className={FIELD_LABEL}>
              Add recipient
            </label>
            <span className="flex flex-wrap gap-2">
              <input
                id="alerts-new-recipient"
                type="email"
                value={newRecipient}
                onChange={(event) => setNewRecipient(event.target.value)}
                onKeyDown={onRecipientKeyDown}
                className={`${FIELD_INPUT} mono max-w-[320px] flex-1`}
              />
              <button type="button" onClick={addRecipient} className={BUTTON_SECONDARY}>
                Add
              </button>
            </span>
            <span className="text-[13px] text-muted">Up to 10. Changes apply when you save.</span>
          </div>
        </fieldset>
        {saveSettings.isError ? (
          <p role="alert" className={ALERT}>
            {problemDetail(saveSettings.error) ?? 'Could not save the alert settings. Try again.'}
          </p>
        ) : null}
        <p>
          <button type="submit" disabled={saveSettings.isPending} className={BUTTON_PRIMARY}>
            Save alert settings
          </button>
        </p>
      </form>
      <AdminSection
        id="alerts-history"
        heading="Recent alerts"
        meta={deliveries.data ? countOf(rows.length, 'alert') : undefined}
      >
        {deliveries.isSuccess && rows.length === 0 ? (
          <p className={EMPTY_STATE}>
            Nothing has been mailed yet. Down and up events appear here, and so does a test email.
          </p>
        ) : rows.length > 0 ? (
          <div className={PANEL}>
            <div aria-hidden="true" className={`${HISTORY_GRID} ${COLUMN_HEAD}`}>
              <span>Event</span>
              <span>Probe</span>
              <span>When</span>
              <span>Delivery</span>
            </div>
            <ol aria-label="Alert deliveries" className="flex list-none flex-col">
              {rows.map((delivery) => (
                <li key={delivery.id} className={ROW}>
                  <div className={`${HISTORY_GRID} px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1`}>
                    <span className="col-start-2 row-start-1 flex lg:col-start-auto lg:row-start-auto">
                      <EventChip delivery={delivery} />
                    </span>
                    <span className="col-start-1 row-start-1 min-w-0 truncate text-[15px] font-semibold lg:col-start-auto lg:row-start-auto">
                      {delivery.probeName}
                    </span>
                    <span className="col-span-2 text-[13px] text-muted lg:col-span-1">
                      <Stamp iso={delivery.occurredAt} />
                      {delivery.kind === 'up' && delivery.downSince ? (
                        <>
                          {' '}
                          · after{' '}
                          {formatDuration(Date.parse(delivery.occurredAt) - Date.parse(delivery.downSince))}
                        </>
                      ) : null}
                    </span>
                    <span className="col-span-2 min-w-0 text-[13px] lg:col-span-1">
                      <DeliveryState delivery={delivery} />
                    </span>
                  </div>
                </li>
              ))}
            </ol>
          </div>
        ) : null}
      </AdminSection>
    </>
  )
}

function EventChip({ delivery }: { delivery: AlertDelivery }) {
  if (delivery.kind === 'down') {
    return <StatusChip state="down" />
  }

  if (delivery.kind === 'up') {
    return <StatusChip state="up" word="Back up" />
  }

  return <StatusChip state="unknown" word="Test" />
}

/** Always words, never colour alone: what became of the mail, and why when it did not go. */
function DeliveryState({ delivery }: { delivery: AlertDelivery }) {
  switch (delivery.state) {
    case 'sent':
      return (
        <span>
          Sent{delivery.sentAt ? <> · <Stamp iso={delivery.sentAt} /></> : null}
        </span>
      )
    case 'pending':
      return (
        <span className="text-muted">
          Pending{delivery.attempts > 0 ? ` (attempt ${String(delivery.attempts + 1)})` : ''}
          {delivery.lastError ? ` — ${delivery.lastError}` : ''}
        </span>
      )
    case 'failed':
      return <span className="text-down">Failed — {delivery.lastError ?? 'unknown error'}</span>
    case 'skipped':
      return <span className="text-muted">Skipped — {delivery.lastError ?? 'not sent'}</span>
  }
}
