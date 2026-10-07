import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Clock, History, KeyRound, Pencil, Plus, Trash2 } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  BUTTON_SECONDARY,
  CARD_FORM,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELD_INPUT,
  FIELD_LABEL,
  INLINE_FORM,
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
import { summariseReporters } from '@/lib/admin-summary'
import { problemDetail } from '@/lib/api'
import { useProbes, type Probe } from '@/lib/probes'
import {
  isOverdue,
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

/*
 * One grid per row, so every row and the column head share this template or the columns stop
 * lining up. The action column is `minmax(176px, auto)` rather than fixed because it is the one
 * cell that may need more: four 40px buttons and a divider is 175px. Below `lg` a row folds as the
 * Probes page's does: reporter and status on the first line, the muted facts (last report, next
 * expected, kept, watched) on the second, actions on the third.
 */
const ROW_GRID =
  'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[124px_minmax(0,1.3fr)_128px_minmax(0,1fr)_56px_minmax(0,1fr)_minmax(176px,auto)] lg:gap-x-3.5'

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
  // Only to find which probe watches a reporter, so Delete can explain instead of attempting
  // (plan 025's D6). The same query the Probes page uses, so it is usually already cached.
  const probes = useProbes()
  const deleteReporter = useDeleteReporter()
  const replaceKey = useReplaceReporterKey()

  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)
  const [confirmingKeyId, setConfirmingKeyId] = useState<string | null>(null)
  const [openHistoryId, setOpenHistoryId] = useState<string | null>(null)

  // The revealed token lives here and never in the query cache: a refetch or a stale read must not
  // be able to resurface a secret the administrator was told they would see once.
  const [revealed, setRevealed] = useState<{ name: string; token: string } | null>(null)

  const rows = reporters.data ?? []
  const allProbes = probes.data ?? []
  const summary = summariseReporters(rows)

  return (
    <>
      <AdminPageHeader
        title="Reporters"
        description="A reporter is a script or an agent somewhere else that pushes a report to Homon with its own API key. Watch one on the dashboard by adding a probe of kind “Message”."
        count={summary.down ? `${summary.text} · ${String(summary.down)} overdue` : summary.text}
      >
        {editingId === null ? (
          <button type="button" onClick={() => jumpToField('reporter-name')} className={BUTTON_PRIMARY}>
            <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
            New reporter
          </button>
        ) : null}
      </AdminPageHeader>
      {revealed !== null ? (
        <KeyReveal inputId="revealed-key" name={revealed.name} token={revealed.token} onDone={() => setRevealed(null)} />
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
      <AdminSection id="reporters" heading="Registered" meta="Status is each reporter's own last word">
        {rows.length === 0 ? (
          <p className={EMPTY_STATE}>No reporters yet. Add one below.</p>
        ) : (
          <div className={PANEL}>
            <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
              <span>Status</span>
              <span>Reporter</span>
              <span>Last report</span>
              <span>Next expected</span>
              <span className="text-right">Kept</span>
              <span>Watched</span>
              <span className="text-right">Actions</span>
            </div>
            <ol aria-label="Reporters" className="flex list-none flex-col">
              {rows.map((reporter) => (
                <ReporterRow
                  key={reporter.id}
                  reporter={reporter}
                  watcher={allProbes.find((probe) => probe.kind === 'message' && probe.host === reporter.identifier)}
                  isEditing={editingId === reporter.id}
                  isHistoryOpen={openHistoryId === reporter.id}
                  isConfirmingDelete={confirmingDeleteId === reporter.id}
                  isConfirmingKey={confirmingKeyId === reporter.id}
                  isDeleting={deleteReporter.isPending}
                  onToggleHistory={() => setOpenHistoryId(openHistoryId === reporter.id ? null : reporter.id)}
                  onEdit={() => {
                    setConfirmingDeleteId(null)
                    setConfirmingKeyId(null)
                    setEditingId((current) => (current === reporter.id ? null : reporter.id))
                  }}
                  onDoneEditing={() => setEditingId(null)}
                  onAskKey={() => {
                    setEditingId(null)
                    setConfirmingDeleteId(null)
                    setConfirmingKeyId((current) => (current === reporter.id ? null : reporter.id))
                  }}
                  onCancelKey={() => setConfirmingKeyId(null)}
                  onConfirmKey={() => {
                    replaceKey.mutate(reporter.id, {
                      onSuccess: (result) => setRevealed({ name: reporter.name, token: result.token }),
                    })
                    setConfirmingKeyId(null)
                  }}
                  onAskDelete={() => {
                    setEditingId(null)
                    setConfirmingKeyId(null)
                    setConfirmingDeleteId((current) => (current === reporter.id ? null : reporter.id))
                  }}
                  onCancelDelete={() => setConfirmingDeleteId(null)}
                  onConfirmDelete={() => {
                    deleteReporter.mutate(reporter.id)
                    setConfirmingDeleteId(null)
                  }}
                />
              ))}
            </ol>
          </div>
        )}
      </AdminSection>
      {/*
        Not rendered while a row is being edited: ReporterForm's inputs carry fixed ids, so two at
        once would hand both "Name" labels to whichever input came first.
      */}
      {editingId === null ? (
        <ReporterForm
          key="new-reporter"
          reporter={null}
          variant="card"
          onDoneEditing={() => undefined}
          onCreated={(name, token) => setRevealed({ name, token })}
        />
      ) : null}
    </>
  )
}

function ReporterRow({
  reporter,
  watcher,
  isEditing,
  isHistoryOpen,
  isConfirmingDelete,
  isConfirmingKey,
  isDeleting,
  onToggleHistory,
  onEdit,
  onDoneEditing,
  onAskKey,
  onCancelKey,
  onConfirmKey,
  onAskDelete,
  onCancelDelete,
  onConfirmDelete,
}: {
  reporter: Reporter
  watcher: Probe | undefined
  isEditing: boolean
  isHistoryOpen: boolean
  isConfirmingDelete: boolean
  isConfirmingKey: boolean
  isDeleting: boolean
  onToggleHistory: () => void
  onEdit: () => void
  onDoneEditing: () => void
  onAskKey: () => void
  onCancelKey: () => void
  onConfirmKey: () => void
  onAskDelete: () => void
  onCancelDelete: () => void
  onConfirmDelete: () => void
}) {
  const state = chipState(reporter)
  const overdue = isOverdue(reporter)
  const isWatched = reporter.isWatched || watcher !== undefined
  const editorId = `reporter-editor-${reporter.id}`
  const historyId = `reporter-history-${reporter.id}`
  const keyConfirmId = `reporter-confirm-key-${reporter.id}`
  const deleteConfirmId = `reporter-confirm-delete-${reporter.id}`
  const latest = reporter.latest

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${isEditing ? 'bg-bg' : (ROW_TINT[state] ?? '')}`}>
        <span className="col-start-2 row-start-1 flex lg:col-start-auto lg:row-start-auto">
          <StatusChip state={state} word={chipWord(reporter)} glyph={overdue ? Clock : undefined} />
        </span>
        <span className="col-start-1 row-start-1 flex min-w-0 flex-col lg:col-start-auto lg:row-start-auto">
          <span className="flex flex-wrap items-baseline gap-x-2">
            <span className="text-[15px] font-semibold">{reporter.name}</span>
            <span className="mono text-[13px] text-muted">{reporter.identifier}</span>
          </span>
          {reporter.description ? <span className="text-[13px] text-muted">{reporter.description}</span> : null}
          {reporter.keyRevokedAt !== null ? (
            <span className="text-[13px] text-down">Its key is revoked: replace it</span>
          ) : null}
        </span>
        {/* One wrapping line under the name below lg; four cells of the grid at lg (`contents`). */}
        <span className="col-span-2 flex flex-wrap gap-x-3 gap-y-0.5 text-[13px] text-muted lg:contents">
          <span className="min-w-0">
            {latest === null ? 'No report received yet' : <Stamp iso={latest.receivedAt} />}
          </span>
          <span className={`min-w-0 ${overdue ? 'text-down' : ''}`}>
            {latest === null ? (
              '—'
            ) : latest.nextExpectedAt === null ? (
              'Not declared — can never be overdue'
            ) : (
              <>
                by <Stamp iso={latest.nextExpectedAt} />
              </>
            )}
          </span>
          <span className="mono lg:text-right">
            {reporter.messageCount}
            <span className="lg:hidden"> kept</span>
          </span>
          <span className="min-w-0">
            {isWatched ? (
              watcher ? (
                <Link to={`/probes/${watcher.id}`} className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
                  Watched by a probe
                </Link>
              ) : (
                'Watched by a probe'
              )
            ) : (
              'No probe watches this reporter'
            )}
          </span>
        </span>
        <span className="col-span-2 flex items-center justify-end lg:col-span-1">
          <IconButton
            icon={History}
            label={`Messages from ${reporter.name}`}
            title="Messages"
            onClick={onToggleHistory}
            aria-expanded={isHistoryOpen}
            aria-controls={isHistoryOpen ? historyId : undefined}
          />
          <span aria-hidden="true" className="mx-1.5 h-5 w-px bg-line" />
          <IconButton
            icon={Pencil}
            label={`Edit ${reporter.name}`}
            title="Edit"
            onClick={onEdit}
            aria-expanded={isEditing}
            aria-controls={isEditing ? editorId : undefined}
          />
          <IconButton
            icon={KeyRound}
            label={`Replace the key for ${reporter.name}`}
            title="Replace the key"
            onClick={onAskKey}
            aria-expanded={isConfirmingKey}
            aria-controls={isConfirmingKey ? keyConfirmId : undefined}
          />
          {/*
            A watched reporter's Delete stays a real, enabled button — D6: `disabled` cannot say
            why, and `aria-disabled` would make Playwright refuse to click it. It is only styled
            quiet, with no red hover, and clicking it explains.
          */}
          <IconButton
            icon={Trash2}
            tone={isWatched ? 'default' : 'danger'}
            label={`Delete ${reporter.name}`}
            title={isWatched ? 'A probe watches this reporter' : 'Delete'}
            onClick={onAskDelete}
            aria-expanded={isConfirmingDelete}
            aria-controls={isConfirmingDelete ? deleteConfirmId : undefined}
          />
        </span>
      </div>
      {isConfirmingKey ? (
        <ConfirmStrip
          id={keyConfirmId}
          question={`Replace the key for ${reporter.name}? The old one stops working at once.`}
          confirmText="Replace key"
          confirmLabel={`Confirm replace the key for ${reporter.name}`}
          cancelLabel={`Cancel replace the key for ${reporter.name}`}
          onConfirm={onConfirmKey}
          onCancel={onCancelKey}
        />
      ) : null}
      {isConfirmingDelete ? (
        isWatched ? (
          <ConfirmStrip
            id={deleteConfirmId}
            tone="neutral"
            question={
              <>
                {watcher ? (
                  <Link to={`/probes/${watcher.id}`} className="underline decoration-line-strong underline-offset-[3px] hover:decoration-text">
                    {watcher.name}
                  </Link>
                ) : (
                  'A probe'
                )}{' '}
                watches {reporter.name}, so it cannot be deleted. Delete that probe or point it at another reporter
                first.
              </>
            }
            confirmLabel={`Confirm delete ${reporter.name}`}
            cancelLabel={`Close the explanation for ${reporter.name}`}
            onCancel={onCancelDelete}
          />
        ) : (
          <ConfirmStrip
            id={deleteConfirmId}
            question={`Delete ${reporter.name}? Its key stops working and its messages are deleted.`}
            confirmLabel={`Confirm delete ${reporter.name}`}
            cancelLabel={`Cancel delete ${reporter.name}`}
            onConfirm={onConfirmDelete}
            onCancel={onCancelDelete}
            isPending={isDeleting}
          />
        )
      ) : null}
      {isHistoryOpen ? (
        <div id={historyId} className="border-t border-line bg-bg px-3.5 py-3 lg:px-4">
          <ReporterHistory reporter={reporter} />
        </div>
      ) : null}
      {isEditing ? (
        <div id={editorId}>
          <ReporterForm reporter={reporter} variant="inline" onDoneEditing={onDoneEditing} onCreated={() => undefined} />
        </div>
      ) : null}
    </li>
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
    <div className="overflow-x-auto rounded-md border border-line bg-surface">
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
                <td className="px-3 py-2 text-[13px] whitespace-nowrap">
                  <Stamp iso={message.receivedAt} />
                </td>
                <td className="px-3 py-2 text-[13.5px]">
                  <StatusChip state={MESSAGE_CHIP_STATE[message.status]} word={MESSAGE_STATUS_WORD[message.status]} />
                </td>
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

/**
 * Registers a reporter (`variant="card"`, the panel at the bottom of the page) or edits one
 * (`variant="inline"`, opened inside that reporter's row). Same fields either way; the inline one
 * drops the panel chrome.
 */
function ReporterForm({
  reporter,
  variant,
  onDoneEditing,
  onCreated,
}: {
  reporter: Reporter | null
  variant: 'card' | 'inline'
  onDoneEditing: () => void
  onCreated: (name: string, token: string) => void
}) {
  const createReporter = useCreateReporter()
  const updateReporter = useUpdateReporter()
  const isEditing = reporter !== null
  const isInline = variant === 'inline'
  const Heading = isInline ? 'h3' : 'h2'
  const mutation = isEditing ? updateReporter : createReporter

  const [fields, setFields] = useState<ReporterFields>(
    reporter
      ? { name: reporter.name, description: reporter.description ?? '', bodyVisibility: reporter.bodyVisibility }
      : EMPTY_FIELDS,
  )

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed: ReporterFields = {
      name: fields.name.trim(),
      description: fields.description.trim(),
      bodyVisibility: fields.bodyVisibility,
    }

    if (isEditing) {
      updateReporter.mutate({ id: reporter.id, fields: trimmed }, { onSuccess: onDoneEditing })
      return
    }

    createReporter.mutate(trimmed, {
      onSuccess: (created) => {
        onCreated(created.reporter.name, created.token)
        setFields(EMPTY_FIELDS)
      },
    })
  }

  return (
    <form
      onSubmit={onSubmit}
      aria-labelledby="reporter-form-heading"
      className={isInline ? INLINE_FORM : CARD_FORM}
    >
      <Heading id="reporter-form-heading" className={isInline ? 'text-[14px] font-semibold' : 'text-[15px] font-semibold'}>
        {isEditing ? `Edit ${reporter.name}` : 'New reporter'}
      </Heading>
      <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,220px),1fr))] gap-4">
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
            onChange={(event) => setFields({ ...fields, name: event.target.value })}
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
            onChange={(event) => setFields({ ...fields, description: event.target.value })}
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
            onChange={(event) => setFields({ ...fields, bodyVisibility: event.target.value as BodyVisibility })}
            className={FIELD_INPUT}
          >
            <option value="administrator">Administrators only</option>
            <option value="reader">Everyone who can read the dashboard</option>
          </select>
        </p>
      </div>
      {mutation.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(mutation.error) ?? 'Could not save the reporter. Try again.'}
        </p>
      ) : null}
      <p className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={mutation.isPending} className={BUTTON_PRIMARY}>
          {isEditing ? 'Save' : 'Add reporter'}
        </button>
        {isEditing ? (
          <button type="button" onClick={onDoneEditing} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        ) : (
          <span className="text-[13px] text-muted">Adding one mints its key and shows it once, at the top of this page.</span>
        )}
      </p>
    </form>
  )
}

/** What a message's own status looks like as a chip: the same mapping the reporter's row uses. */
const MESSAGE_CHIP_STATE: Record<MessageStatus, StatusChipState> = {
  success: 'up',
  none: 'up',
  warning: 'unstable',
  failure: 'down',
  unknown: 'unknown',
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

  return MESSAGE_CHIP_STATE[reporter.latest.status]
}

function chipWord(reporter: Reporter): string {
  if (reporter.latest === null) {
    return 'No report'
  }

  return isOverdue(reporter) ? 'Overdue' : MESSAGE_STATUS_WORD[reporter.latest.status]
}
