import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Ban, Clock, Plus } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  CARD_FORM,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELD_INPUT,
  FIELD_LABEL,
  PANEL,
  ROW,
  ROW_CELLS,
  ROW_TINT,
} from '@/components/admin-classes'
import { AdminPageHeader, jumpToField } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton } from '@/components/icon-button'
import { KeyReveal } from '@/components/key-reveal'
import { Stamp } from '@/components/stamp'
import { StatusChip } from '@/components/status-chip'
import { summariseApiKeys } from '@/lib/admin-summary'
import { problemDetail } from '@/lib/api'
import {
  useApiKeys,
  useCreateApiKey,
  useRevokeApiKey,
  type ApiKey,
  type ApiKeyFields,
  type ApiKeyScope,
} from '@/lib/api-keys'
import { formatStamp } from '@/lib/format-stamp'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

const EMPTY_FIELDS: ApiKeyFields = { name: '', scope: 'read', expiresAt: '' }

const SCOPE_LABELS: Record<ApiKeyScope, string> = { read: 'Read', readWrite: 'Read and write' }

/*
 * One grid per row, so every row and the column head share this template or the columns stop
 * lining up. Below `lg` a row folds as the Probes page's does: key and state on the first line, the
 * muted facts (token, pairing, scope, dates) on the second, the revoke button on the third.
 */
const ROW_GRID =
  'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[156px_minmax(0,1.2fr)_124px_112px_128px_128px_64px] lg:gap-x-3.5'

/**
 * The API keys admin page: what exists, minting one, and revoking one. A key is never deleted —
 * revoking stamps it and leaves the row, which is the audit trail.
 *
 * A reporter's own key is listed here, in its own section, but is replaced from the Reporters page,
 * where the consequence of doing so is visible. Revoke stays on it all the same (plan 025's D12):
 * a styling pass must not take a capability away.
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
  const scriptKeys = rows.filter((key) => key.reporterName === null)
  const reporterKeys = rows.filter((key) => key.reporterName !== null)

  return (
    <>
      <AdminPageHeader
        title="API keys"
        description={
          <>
            A key lets a script read the API; a read-and-write key may also file reports. Keys never administer
            anything. A fresh install can still mint its first key with{' '}
            <code className="mono rounded bg-surface px-1.5 py-0.5">create-api-key</code>, before anyone can sign in.
          </>
        }
        count={summariseApiKeys(rows).text}
      >
        <button type="button" onClick={() => jumpToField('api-key-name')} className={BUTTON_PRIMARY}>
          <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
          New key
        </button>
      </AdminPageHeader>
      {revealed !== null ? (
        <KeyReveal inputId="revealed-api-key" name={revealed.name} token={revealed.token} onDone={() => setRevealed(null)} />
      ) : null}
      {revokeApiKey.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(revokeApiKey.error) ?? 'Could not revoke the key. Try again.'}
        </p>
      ) : null}
      {rows.length === 0 ? <p className={EMPTY_STATE}>No API keys yet. Mint one below.</p> : null}
      {scriptKeys.length > 0 ? (
        <KeySection
          id="script-keys"
          heading="Script keys"
          meta="Minted and revoked here"
          keys={scriptKeys}
          confirmingRevokeId={confirmingRevokeId}
          isRevoking={revokeApiKey.isPending}
          onAskRevoke={(id) => setConfirmingRevokeId((current) => (current === id ? null : id))}
          onCancelRevoke={() => setConfirmingRevokeId(null)}
          onConfirmRevoke={(id) => {
            revokeApiKey.mutate(id)
            setConfirmingRevokeId(null)
          }}
        />
      ) : null}
      {reporterKeys.length > 0 ? (
        <KeySection
          id="reporter-keys"
          heading="Reporter keys"
          meta="One per reporter · replace it on the Reporters page"
          keys={reporterKeys}
          confirmingRevokeId={confirmingRevokeId}
          isRevoking={revokeApiKey.isPending}
          onAskRevoke={(id) => setConfirmingRevokeId((current) => (current === id ? null : id))}
          onCancelRevoke={() => setConfirmingRevokeId(null)}
          onConfirmRevoke={(id) => {
            revokeApiKey.mutate(id)
            setConfirmingRevokeId(null)
          }}
        />
      ) : null}
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

function KeySection({
  id,
  heading,
  meta,
  keys,
  confirmingRevokeId,
  isRevoking,
  onAskRevoke,
  onCancelRevoke,
  onConfirmRevoke,
}: {
  id: string
  heading: string
  meta: string
  keys: ApiKey[]
  confirmingRevokeId: string | null
  isRevoking: boolean
  onAskRevoke: (id: string) => void
  onCancelRevoke: () => void
  onConfirmRevoke: (id: string) => void
}) {
  return (
    <AdminSection id={id} heading={heading} meta={meta}>
      <div className={PANEL}>
        <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
          <span>State</span>
          <span>Key</span>
          <span>Scope</span>
          <span>Created</span>
          <span>Last used</span>
          <span>Expires</span>
          <span className="text-right">Actions</span>
        </div>
        <ol aria-label={heading} className="flex list-none flex-col">
          {keys.map((key) => (
            <KeyRow
              key={key.id}
              apiKey={key}
              isConfirmingRevoke={confirmingRevokeId === key.id}
              isRevoking={isRevoking}
              onAskRevoke={() => onAskRevoke(key.id)}
              onCancelRevoke={onCancelRevoke}
              onConfirmRevoke={() => onConfirmRevoke(key.id)}
            />
          ))}
        </ol>
      </div>
    </AdminSection>
  )
}

function KeyRow({
  apiKey,
  isConfirmingRevoke,
  isRevoking,
  onAskRevoke,
  onCancelRevoke,
  onConfirmRevoke,
}: {
  apiKey: ApiKey
  isConfirmingRevoke: boolean
  isRevoking: boolean
  onAskRevoke: () => void
  onCancelRevoke: () => void
  onConfirmRevoke: () => void
}) {
  const isRevoked = apiKey.revokedAt !== null
  const label = `${apiKey.name} (${apiKey.tokenId})`
  const confirmId = `api-key-confirm-revoke-${apiKey.id}`
  // Revoked wins over expired: a key that is both reads Revoked, here and in the page's count.
  const isExpired = !isRevoked && apiKey.isExpired

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${isRevoked ? ROW_TINT.paused : ''}`}>
        <span className="col-start-2 row-start-1 flex lg:col-start-auto lg:row-start-auto">
          {apiKey.revokedAt !== null ? (
            // The revocation date lives inside the chip, so "Revoked" is written exactly once per row.
            <StatusChip
              state="paused"
              word={`Revoked ${formatStamp(apiKey.revokedAt, { time: false })}`}
              glyph={Ban}
            />
          ) : isExpired ? (
            <StatusChip state="unknown" word="Expired" glyph={Clock} />
          ) : (
            <StatusChip state="up" word="Active" />
          )}
        </span>
        <span className="col-start-1 row-start-1 flex min-w-0 flex-col lg:col-start-auto lg:row-start-auto">
          <span className={`truncate text-[15px] font-semibold ${isRevoked ? 'text-muted' : ''}`}>{apiKey.name}</span>
          <span className="mono truncate text-[13px] text-muted">{apiKey.tokenId}</span>
          {apiKey.reporterName !== null ? (
            <Link
              to="/admin/reporters"
              className="w-fit text-[13px] text-muted underline decoration-line-strong underline-offset-[3px] hover:text-text hover:decoration-text"
            >
              {`Paired with the reporter ${apiKey.reporterName}`}
            </Link>
          ) : null}
        </span>
        {/* One wrapping line under the name below lg; four cells of the grid at lg (`contents`). */}
        <span className="col-span-2 flex flex-wrap gap-x-3 gap-y-0.5 text-[13px] text-muted lg:contents">
          <span className="text-text lg:text-muted">{SCOPE_LABELS[apiKey.scope]}</span>
          <span>
            <span className="lg:hidden">Created </span>
            <Stamp iso={apiKey.createdAt} time={false} />
          </span>
          <span>
            {apiKey.lastUsedAt === null ? (
              'Never used'
            ) : (
              <>
                <span className="lg:hidden">Last used </span>
                <Stamp iso={apiKey.lastUsedAt} />
              </>
            )}
          </span>
          <span>
            {apiKey.expiresAt === null ? (
              'Never'
            ) : (
              <>
                {apiKey.isExpired ? 'Expired ' : <span className="lg:hidden">Expires </span>}
                <Stamp iso={apiKey.expiresAt} time={false} />
              </>
            )}
          </span>
        </span>
        <span className="col-span-2 flex items-center justify-end lg:col-span-1">
          <IconButton
            icon={Ban}
            tone="danger"
            label={`Revoke ${label}`}
            title="Revoke"
            disabled={isRevoked}
            onClick={onAskRevoke}
            aria-expanded={isConfirmingRevoke}
            aria-controls={isConfirmingRevoke ? confirmId : undefined}
          />
        </span>
      </div>
      {isConfirmingRevoke ? (
        <ConfirmStrip
          id={confirmId}
          question={`Revoke ${apiKey.name}? It stops working at once.`}
          confirmText="Revoke"
          confirmLabel={`Confirm revoke ${label}`}
          cancelLabel={`Cancel revoke ${label}`}
          onConfirm={onConfirmRevoke}
          onCancel={onCancelRevoke}
          isPending={isRevoking}
        />
      ) : null}
    </li>
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
    <form onSubmit={onSubmit} aria-labelledby="api-key-form-heading" className={CARD_FORM}>
      <h2 id="api-key-form-heading" className="text-[15px] font-semibold">
        New key
      </h2>
      <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,200px),1fr))] gap-4">
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
        </p>
      </div>
      {createApiKey.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(createApiKey.error) ?? 'Could not mint the key. Try again.'}
        </p>
      ) : null}
      <p className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={createApiKey.isPending} className={BUTTON_PRIMARY}>
          Create key
        </button>
        <span className="text-[13px] text-muted">
          Leave Expires empty for a key that does not expire. The key is shown once, at the top of this page.
        </span>
      </p>
    </form>
  )
}
