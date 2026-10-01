import { useState, type FormEvent } from 'react'

import { StatusChip } from '@/components/status-chip'
import { problemDetail } from '@/lib/api'
import {
  MESSAGE_STATUS_WORD,
  messageCategoryIcon,
  useCreateReporter,
  useDeleteReporter,
  useReplaceReporterKey,
  useReporterMessages,
  useReporters,
  useUpdateReporter,
  type BodyVisibility,
  type MessageStatus,
  type Reporter,
  type ReporterFields,
} from '@/lib/reporters'
import type { StatusChipState } from '@/components/status-chip'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

const EMPTY_FIELDS: ReporterFields = { name: '', description: '', bodyVisibility: 'administrator' }

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

/**
 * The reporters admin page: register a reporter (which mints the one key paired with it and shows
 * it exactly once), change its label or who may read its message bodies, replace its key, read its
 * history, and delete it.
 *
 * This is the only surface in the application that displays a message body in full — see plan
 * 021's Decision 11 and `docs/ARCHITECTURE.md` §3.25.
 */
export function AdminReportersPage() {
  useDocumentTitle(pageTitle('Reporters', 'Admin'))

  const reporters = useReporters()
  const deleteReporter = useDeleteReporter()
  const replaceKey = useReplaceReporterKey()

  const [editingId, setEditingId] = useState<string | null>(null)
  const [fields, setFields] = useState<ReporterFields>(EMPTY_FIELDS)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)
  const [confirmingKeyId, setConfirmingKeyId] = useState<string | null>(null)
  const [openHistoryId, setOpenHistoryId] = useState<string | null>(null)

  // The revealed token lives here and never in the query cache: a refetch or a stale read must not
  // be able to resurface a secret the administrator was told they would see once.
  const [revealed, setRevealed] = useState<{ name: string; token: string } | null>(null)

  const rows = reporters.data ?? []

  function startEditing(reporter: Reporter) {
    setEditingId(reporter.id)
    setFields({
      name: reporter.name,
      description: reporter.description ?? '',
      bodyVisibility: reporter.bodyVisibility,
    })
  }

  function cancelEditing() {
    setEditingId(null)
    setFields(EMPTY_FIELDS)
  }

  return (
    <>
      <h1 className={PAGE_H1}>Reporters</h1>
      <p className="text-[14px] text-muted">
        A reporter is a script or an agent somewhere else that pushes a report to Homon with its own
        API key. Watch one on the dashboard by adding a probe of kind “Message”.
      </p>
      {revealed !== null ? (
        <div role="alert" className={REVEAL}>
          <p className="font-semibold">This key will not be shown again. Store it now.</p>
          <p className="flex flex-col gap-1">
            <label htmlFor="revealed-key" className={FIELD_LABEL}>
              API key for {revealed.name}
            </label>
            <input id="revealed-key" readOnly value={revealed.token} className={`${FIELD_INPUT} mono`} />
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
      {deleteReporter.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(deleteReporter.error) ?? 'Could not delete the reporter. Try again.'}
        </p>
      ) : null}
      {replaceKey.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(replaceKey.error) ?? 'Could not replace the key. Try again.'}
        </p>
      ) : null}
      {rows.length === 0 ? (
        <p className={EMPTY_STATE}>No reporters yet. Add one below.</p>
      ) : (
        <ol
          aria-label="Reporters"
          className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4"
        >
          {rows.map((reporter) => (
            <li key={reporter.id} className="flex flex-col gap-2 py-3">
              <p className="flex flex-wrap items-center gap-2">
                <StatusChip state={chipState(reporter)} word={chipWord(reporter)} />
                <span className="text-[15px] font-semibold">{reporter.name}</span>
                <span className="mono text-[13px] text-muted">{reporter.identifier}</span>
              </p>
              {reporter.description ? (
                <p className="text-[14px] text-muted">{reporter.description}</p>
              ) : null}
              <p className="text-[13px] text-muted">{describe(reporter)}</p>
              <p className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => setOpenHistoryId(openHistoryId === reporter.id ? null : reporter.id)}
                  className={BUTTON_SECONDARY}
                >
                  Messages from {reporter.name}
                </button>
                <button type="button" onClick={() => startEditing(reporter)} className={BUTTON_SECONDARY}>
                  Edit {reporter.name}
                </button>
                {confirmingKeyId === reporter.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        replaceKey.mutate(reporter.id, {
                          onSuccess: (result) => setRevealed({ name: reporter.name, token: result.token }),
                        })
                        setConfirmingKeyId(null)
                      }}
                      className={BUTTON_DANGER}
                    >
                      Confirm replace the key for {reporter.name}
                    </button>
                    <button type="button" onClick={() => setConfirmingKeyId(null)} className={BUTTON_SECONDARY}>
                      Cancel replace the key for {reporter.name}
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => setConfirmingKeyId(reporter.id)} className={BUTTON_SECONDARY}>
                    Replace the key for {reporter.name}
                  </button>
                )}
                {confirmingDeleteId === reporter.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        deleteReporter.mutate(reporter.id)
                        setConfirmingDeleteId(null)
                      }}
                      className={BUTTON_DANGER}
                    >
                      Confirm delete {reporter.name}
                    </button>
                    <button type="button" onClick={() => setConfirmingDeleteId(null)} className={BUTTON_SECONDARY}>
                      Cancel delete {reporter.name}
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => setConfirmingDeleteId(reporter.id)} className={BUTTON_DANGER}>
                    Delete {reporter.name}
                  </button>
                )}
              </p>
              {openHistoryId === reporter.id ? <ReporterHistory reporter={reporter} /> : null}
            </li>
          ))}
        </ol>
      )}
      <ReporterForm
        key={editingId ?? 'new-reporter'}
        fields={fields}
        onFieldsChange={setFields}
        editingId={editingId}
        editingName={rows.find((reporter) => reporter.id === editingId)?.name ?? null}
        onCancel={cancelEditing}
        onCreated={(name, token) => setRevealed({ name, token })}
      />
    </>
  )
}

/** A reporter's last report, as a table. The one place a body is shown in full. */
function ReporterHistory({ reporter }: { reporter: Reporter }) {
  const messages = useReporterMessages(reporter.id)

  if (messages.isError) {
    return (
      <p role="alert" className={ALERT}>
        {problemDetail(messages.error) ?? 'Could not load the messages. Try again.'}
      </p>
    )
  }

  const rows = messages.data ?? []

  if (rows.length === 0) {
    return <p className={EMPTY_STATE}>No messages from this reporter yet.</p>
  }

  return (
    <div className="overflow-x-auto rounded-md border border-line">
      <table aria-label={`Messages from ${reporter.name}`} className="w-full border-collapse text-left">
        <thead className="text-[12px] uppercase tracking-[0.08em] text-muted">
          <tr>
            <th scope="col" className="px-3 py-2 font-semibold">
              Received
            </th>
            <th scope="col" className="px-3 py-2 font-semibold">
              Status
            </th>
            <th scope="col" className="px-3 py-2 font-semibold">
              Category
            </th>
            <th scope="col" className="px-3 py-2 font-semibold">
              Message
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((message) => {
            const CategoryIcon = messageCategoryIcon(message.category)

            return (
              <tr key={message.id} className="border-t border-line align-top">
                <td className="mono px-3 py-2 text-[13px] whitespace-nowrap">{message.receivedAt}</td>
                <td className="px-3 py-2 text-[13.5px]">{MESSAGE_STATUS_WORD[message.status]}</td>
                <td className="px-3 py-2 text-[13.5px]">
                  <span className="inline-flex items-center gap-1.5">
                    <CategoryIcon aria-hidden className="size-3.5" />
                    {message.category}
                  </span>
                </td>
                <td className="px-3 py-2 text-[13px]">
                  <span className="block font-semibold">{message.name}</span>
                  {message.truncated ? (
                    <span className="block text-[12px] text-muted">Truncated to the last 64 KiB.</span>
                  ) : null}
                  {message.body ? (
                    <pre className="mono mt-1 max-h-60 overflow-auto whitespace-pre-wrap text-[12.5px] text-muted">
                      {message.body}
                    </pre>
                  ) : null}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function ReporterForm({
  fields,
  onFieldsChange,
  editingId,
  editingName,
  onCancel,
  onCreated,
}: {
  fields: ReporterFields
  onFieldsChange: (fields: ReporterFields) => void
  editingId: string | null
  editingName: string | null
  onCancel: () => void
  onCreated: (name: string, token: string) => void
}) {
  const createReporter = useCreateReporter()
  const updateReporter = useUpdateReporter()
  const isEditing = editingId !== null
  const mutation = isEditing ? updateReporter : createReporter

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed: ReporterFields = {
      name: fields.name.trim(),
      description: fields.description.trim(),
      bodyVisibility: fields.bodyVisibility,
    }

    if (isEditing) {
      updateReporter.mutate({ id: editingId, fields: trimmed }, { onSuccess: onCancel })
      return
    }

    createReporter.mutate(trimmed, {
      onSuccess: (created) => {
        onCreated(created.reporter.name, created.token)
        onFieldsChange(EMPTY_FIELDS)
      },
    })
  }

  return (
    <form
      onSubmit={onSubmit}
      aria-labelledby="reporter-form-heading"
      className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5"
    >
      <h2 id="reporter-form-heading" className="text-[15px] font-semibold">
        {isEditing ? `Edit ${editingName ?? 'reporter'}` : 'Add a reporter'}
      </h2>
      <p className="flex flex-col gap-1">
        <label htmlFor="reporter-name" className={FIELD_LABEL}>
          Name
        </label>
        <input
          id="reporter-name"
          name="name"
          required
          maxLength={100}
          value={fields.name}
          onChange={(event) => onFieldsChange({ ...fields, name: event.target.value })}
          className={FIELD_INPUT}
        />
      </p>
      <p className="flex flex-col gap-1">
        <label htmlFor="reporter-description" className={FIELD_LABEL}>
          Description
        </label>
        <input
          id="reporter-description"
          name="description"
          maxLength={280}
          value={fields.description}
          onChange={(event) => onFieldsChange({ ...fields, description: event.target.value })}
          className={FIELD_INPUT}
        />
      </p>
      <p className="flex flex-col gap-1">
        <label htmlFor="reporter-visibility" className={FIELD_LABEL}>
          Message visibility
        </label>
        <select
          id="reporter-visibility"
          name="bodyVisibility"
          value={fields.bodyVisibility}
          onChange={(event) =>
            onFieldsChange({ ...fields, bodyVisibility: event.target.value as BodyVisibility })
          }
          className={FIELD_INPUT}
        >
          <option value="administrator">Administrators only</option>
          <option value="reader">Everyone who can read the dashboard</option>
        </select>
      </p>
      {mutation.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(mutation.error) ?? 'Could not save the reporter. Try again.'}
        </p>
      ) : null}
      <p className="flex flex-wrap gap-2">
        <button type="submit" disabled={mutation.isPending} className={BUTTON_PRIMARY}>
          {isEditing ? 'Save' : 'Add reporter'}
        </button>
        {isEditing ? (
          <button type="button" onClick={onCancel} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        ) : null}
      </p>
    </form>
  )
}

/**
 * The chip for a row on this page is about the reporter's own last word, which is not the same
 * question the dashboard asks: there is no probe here, so there is no streak and no paused state.
 */
function chipState(reporter: Reporter): StatusChipState {
  if (reporter.latest === null) {
    return 'unknown'
  }

  if (isOverdue(reporter)) {
    return 'down'
  }

  const states: Record<MessageStatus, StatusChipState> = {
    success: 'up',
    none: 'up',
    warning: 'unstable',
    failure: 'down',
    unknown: 'unknown',
  }

  return states[reporter.latest.status]
}

function chipWord(reporter: Reporter): string {
  if (reporter.latest === null) {
    return 'No report'
  }

  return isOverdue(reporter) ? 'Overdue' : MESSAGE_STATUS_WORD[reporter.latest.status]
}

function isOverdue(reporter: Reporter): boolean {
  const due = reporter.latest?.nextExpectedAt

  return due != null && new Date(due).getTime() < Date.now()
}

/**
 * The line under a row. It names the missing recurrence explicitly, because a reporter that never
 * declared one can never be overdue — it looks monitored while only its own failures can take it
 * off green, and that is worth saying out loud rather than leaving blank.
 */
function describe(reporter: Reporter): string {
  const parts: string[] = []

  if (reporter.latest === null) {
    parts.push('No report received yet')
  } else {
    parts.push(`Last report ${reporter.latest.receivedAt}`)
    parts.push(
      reporter.latest.nextExpectedAt === null
        ? 'No recurrence declared — this reporter can never be overdue'
        : `Next expected by ${reporter.latest.nextExpectedAt}`,
    )
  }

  parts.push(`${reporter.messageCount} message${reporter.messageCount === 1 ? '' : 's'} kept`)
  parts.push(reporter.isWatched ? 'Watched by a probe' : 'No probe watches this reporter')

  if (reporter.keyRevokedAt !== null) {
    parts.push('Its key is revoked — replace it')
  }

  return parts.join(' · ')
}
