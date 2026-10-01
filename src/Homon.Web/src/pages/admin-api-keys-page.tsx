import { useState, type FormEvent } from 'react'

import { problemDetail } from '@/lib/api'
import {
  useApiKeys,
  useCreateApiKey,
  useRevokeApiKey,
  type ApiKey,
  type ApiKeyFields,
  type ApiKeyScope,
} from '@/lib/api-keys'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

const EMPTY_FIELDS: ApiKeyFields = { name: '', scope: 'read', expiresAt: '' }

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const BUTTON_DANGER =
  'inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'
const REVEAL =
  'flex flex-col gap-2 rounded-md border border-unstable/40 bg-unstable-bg px-3 py-2.5 text-[14px] text-text'
const EMPTY_STATE =
  'rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted'

const SCOPE_LABELS: Record<ApiKeyScope, string> = { read: 'Read', readWrite: 'Read and write' }

/**
 * The API keys admin page: what exists, minting one, and revoking one. A key is never deleted —
 * revoking stamps it and leaves the row, which is the audit trail.
 *
 * A reporter's own key is listed here but can only be replaced or removed from the Reporters page,
 * where the consequence of doing so is visible.
 */
export function AdminApiKeysPage() {
  useDocumentTitle(pageTitle('API keys', 'Admin'))

  const apiKeys = useApiKeys()
  const revokeApiKey = useRevokeApiKey()

  const [fields, setFields] = useState<ApiKeyFields>(EMPTY_FIELDS)
  const [confirmingRevokeId, setConfirmingRevokeId] = useState<string | null>(null)

  // Local state, never the query cache: a refetch must not resurface a secret shown once.
  const [revealed, setRevealed] = useState<{ name: string; token: string } | null>(null)

  const rows = apiKeys.data ?? []

  return (
    <>
      <h1 className={PAGE_H1}>API keys</h1>
      <p className="text-[14px] text-muted">
        A key lets a script read the API; a read-and-write key may also file reports. Keys never
        administer anything. A fresh install can still mint its first key with{' '}
        <code className="mono rounded bg-surface px-1.5 py-0.5">create-api-key</code>, before anyone
        can sign in.
      </p>
      {revealed !== null ? (
        <div role="alert" className={REVEAL}>
          <p className="font-semibold">This key will not be shown again. Store it now.</p>
          <p className="flex flex-col gap-1">
            <label htmlFor="revealed-api-key" className={FIELD_LABEL}>
              API key for {revealed.name}
            </label>
            <input id="revealed-api-key" readOnly value={revealed.token} className={`${FIELD_INPUT} mono`} />
          </p>
          <p className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void navigator.clipboard?.writeText(revealed.token)}
              className={BUTTON_SECONDARY}
            >
              Copy key
            </button>
            <button type="button" onClick={() => setRevealed(null)} className={BUTTON_SECONDARY}>
              Done
            </button>
          </p>
        </div>
      ) : null}
      {revokeApiKey.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(revokeApiKey.error) ?? 'Could not revoke the key. Try again.'}
        </p>
      ) : null}
      {rows.length === 0 ? (
        <p className={EMPTY_STATE}>No API keys yet. Mint one below.</p>
      ) : (
        <ol
          aria-label="API keys"
          className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4"
        >
          {rows.map((key) => (
            <li key={key.id} className="flex flex-col gap-2 py-3">
              <p className="flex flex-wrap items-baseline gap-2">
                <span className="text-[15px] font-semibold">{key.name}</span>
                <span className="mono text-[13px] text-muted">{key.tokenId}</span>
                <span className="text-[13px] text-muted">{SCOPE_LABELS[key.scope]}</span>
              </p>
              <p className="text-[13px] text-muted">{describe(key)}</p>
              <p className="flex flex-wrap gap-2">
                {confirmingRevokeId === key.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        revokeApiKey.mutate(key.id)
                        setConfirmingRevokeId(null)
                      }}
                      className={BUTTON_DANGER}
                    >
                      Confirm revoke {key.name} ({key.tokenId})
                    </button>
                    <button
                      type="button"
                      onClick={() => setConfirmingRevokeId(null)}
                      className={BUTTON_SECONDARY}
                    >
                      Cancel revoke {key.name} ({key.tokenId})
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    onClick={() => setConfirmingRevokeId(key.id)}
                    disabled={key.revokedAt !== null}
                    className={BUTTON_DANGER}
                  >
                    Revoke {key.name} ({key.tokenId})
                  </button>
                )}
              </p>
            </li>
          ))}
        </ol>
      )}
      <ApiKeyForm
        fields={fields}
        onFieldsChange={setFields}
        onCreated={(name, token) => {
          setRevealed({ name, token })
          setFields(EMPTY_FIELDS)
        }}
      />
    </>
  )
}

function ApiKeyForm({
  fields,
  onFieldsChange,
  onCreated,
}: {
  fields: ApiKeyFields
  onFieldsChange: (fields: ApiKeyFields) => void
  onCreated: (name: string, token: string) => void
}) {
  const createApiKey = useCreateApiKey()

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    createApiKey.mutate(
      { ...fields, name: fields.name.trim() },
      { onSuccess: (created) => onCreated(created.name, created.token) },
    )
  }

  return (
    <form
      onSubmit={onSubmit}
      aria-labelledby="api-key-form-heading"
      className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5"
    >
      <h2 id="api-key-form-heading" className="text-[15px] font-semibold">
        Mint a key
      </h2>
      <p className="flex flex-col gap-1">
        <label htmlFor="api-key-name" className={FIELD_LABEL}>
          Name
        </label>
        <input
          id="api-key-name"
          name="name"
          required
          maxLength={100}
          value={fields.name}
          onChange={(event) => onFieldsChange({ ...fields, name: event.target.value })}
          className={FIELD_INPUT}
        />
      </p>
      <p className="flex flex-col gap-1">
        <label htmlFor="api-key-scope" className={FIELD_LABEL}>
          Scope
        </label>
        <select
          id="api-key-scope"
          name="scope"
          value={fields.scope}
          onChange={(event) => onFieldsChange({ ...fields, scope: event.target.value as ApiKeyScope })}
          className={FIELD_INPUT}
        >
          <option value="read">Read</option>
          <option value="readWrite">Read and write</option>
        </select>
      </p>
      <p className="flex flex-col gap-1">
        <label htmlFor="api-key-expires" className={FIELD_LABEL}>
          Expires
        </label>
        <input
          id="api-key-expires"
          name="expiresAt"
          type="date"
          value={fields.expiresAt}
          onChange={(event) => onFieldsChange({ ...fields, expiresAt: event.target.value })}
          className={FIELD_INPUT}
        />
        <span className="text-[12.5px] text-muted">Leave empty for a key that does not expire.</span>
      </p>
      {createApiKey.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(createApiKey.error) ?? 'Could not mint the key. Try again.'}
        </p>
      ) : null}
      <p>
        <button type="submit" disabled={createApiKey.isPending} className={BUTTON_PRIMARY}>
          Create key
        </button>
      </p>
    </form>
  )
}

function describe(key: ApiKey): string {
  const parts = [`Created ${key.createdAt}`]

  parts.push(key.lastUsedAt === null ? 'Never used' : `Last used ${key.lastUsedAt}`)

  if (key.expiresAt !== null) {
    parts.push(key.isExpired ? `Expired ${key.expiresAt}` : `Expires ${key.expiresAt}`)
  }

  if (key.revokedAt !== null) {
    parts.push(`Revoked ${key.revokedAt}`)
  }

  if (key.reporterName !== null) {
    parts.push(`Paired with the reporter ${key.reporterName}`)
  }

  return parts.join(' · ')
}
