import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Pause, Pencil, Play, Plus, Trash2 } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  BUTTON_SECONDARY,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELD_INPUT,
  FIELD_LABEL,
  FIELDSET,
  INLINE_FORM,
  CARD_FORM,
  LEGEND,
  PANEL,
  ROW,
  ROW_CELLS,
  ROW_TINT,
} from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton, MoveButtons } from '@/components/icon-button'
import { StatusChip } from '@/components/status-chip'
import { problemDetail } from '@/lib/api'
import { formatDuration } from '@/lib/probe-detail'
import { type ProbeGroup, useProbeGroups, useSetProbeGroupMembers } from '@/lib/probe-groups'
import {
  moveWithinSection,
  PROBE_KIND_SHORT,
  probeSections,
  probeTarget,
  type ProbeSection,
  type ProbeSectionRow,
} from '@/lib/probe-sections'
import {
  useCreateProbe,
  useDeleteProbe,
  useProbes,
  useReorderProbes,
  useSetProbePaused,
  useUpdateProbe,
  type HttpProbeOptionsInput,
  type Probe,
} from '@/lib/probes'
import { useReporters } from '@/lib/reporters'
import { PROBE_KIND_LABEL } from '@/lib/status'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/** Every kind the create form offers today — `smb`/`snmp` arrive with plans 004/005. */
const CREATABLE_KINDS: readonly ('ping' | 'http' | 'message')[] = ['ping', 'http', 'message']

/*
 * One grid per row, so every row and the header must share exactly this template or the columns
 * stop lining up — hence the fixed 216px action column (five 40px buttons and a divider) rather
 * than `auto`. Below `lg` a row folds instead: position, name and status on the first line, kind,
 * target and interval on the second, actions on the third, as the canvas's 412px artboard draws
 * it. A table at that width would need a sideways scroll for every row.
 */
const ROW_GRID =
  'grid grid-cols-[24px_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[28px_116px_minmax(0,1.15fr)_72px_minmax(0,1.5fr)_64px_216px] lg:gap-x-3.5'

/**
 * The probe admin page: every probe, sectioned and ordered exactly as the dashboard lays them out
 * by default (`probeSections`), each row with move / edit / pause / delete. Edit opens the form
 * inside the row; the add form waits at the bottom.
 */
export function AdminProbesPage() {
  useDocumentTitle(pageTitle('Probes', 'Admin'))

  const probes = useProbes()
  const groups = useProbeGroups()
  const reorder = useReorderProbes()
  const setMembers = useSetProbeGroupMembers()

  // Keyed by section AND probe: a probe in two groups is two rows, and only the one whose
  // button was pressed opens.
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [confirmingDeleteKey, setConfirmingDeleteKey] = useState<string | null>(null)

  const allProbes = probes.data ?? []
  const allGroups = groups.data ?? []
  const sections = probeSections(allProbes, allGroups)
  const isEditingARow = sections.some((section) =>
    section.rows.some((row) => rowKey(section, row) === editingKey),
  )

  function move(section: ProbeSection, probeId: string, direction: -1 | 1) {
    const sectionIds = section.rows.map((row) => row.probe.id)

    if (section.kind === 'group') {
      const next = moveWithinSection(sectionIds, sectionIds, probeId, direction)

      if (next !== null) {
        setMembers.mutate({ id: section.id, probeIds: next })
      }

      return
    }

    const next = moveWithinSection(
      allProbes.map((probe) => probe.id),
      sectionIds,
      probeId,
      direction,
    )

    if (next !== null) {
      reorder.mutate(next)
    }
  }

  return (
    <>
      <AdminPageHeader
        title="Probes"
        description="Listed the way the dashboard lays them out: each group in its own order, then every probe not in a group. A probe in two groups appears in both, and can sit at a different place in each."
        count={`${countOf(allProbes.length, 'probe')} · ${countOf(allGroups.length, 'group')}`}
      >
        <Link to="/admin/probe-groups" className={BUTTON_SECONDARY}>
          Manage groups
        </Link>
        {isEditingARow ? null : (
          <button type="button" onClick={jumpToAddForm} className={BUTTON_PRIMARY}>
            <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
            New probe
          </button>
        )}
      </AdminPageHeader>
      {sections.length === 0 ? (
        <p className={EMPTY_STATE}>No probes yet. Add one below.</p>
      ) : (
        sections.map((section) => (
          <ProbeSectionPanel
            key={section.id}
            section={section}
            editingKey={editingKey}
            confirmingDeleteKey={confirmingDeleteKey}
            groups={allGroups}
            onMove={(probeId, direction) => move(section, probeId, direction)}
            onEdit={(key) => {
              setConfirmingDeleteKey(null)
              setEditingKey((current) => (current === key ? null : key))
            }}
            onDoneEditing={() => setEditingKey(null)}
            onAskDelete={(key) => {
              setEditingKey(null)
              setConfirmingDeleteKey((current) => (current === key ? null : key))
            }}
            onCancelDelete={() => setConfirmingDeleteKey(null)}
          />
        ))
      )}
      {/*
        Not rendered while a row is being edited: ProbeForm's inputs carry fixed ids, so two at
        once would hand both "Name" labels to whichever input came first, and an add form below
        an open editor is a second thing to type into that nobody asked for.
      */}
      {isEditingARow ? null : <ProbeForm key="new-probe" probe={null} groups={allGroups} variant="card" onDoneEditing={() => undefined} />}
    </>
  )
}

/** "New probe" in the header: the add form is at the bottom of what can be a long page. */
function jumpToAddForm() {
  const name = document.getElementById('probe-name')
  name?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  name?.focus({ preventScroll: true })
}

function rowKey(section: ProbeSection, row: ProbeSectionRow): string {
  return `${section.id}:${row.probe.id}`
}

function countOf(count: number, noun: string): string {
  return `${String(count)} ${noun}${count === 1 ? '' : 's'}`
}

function ProbeSectionPanel({
  section,
  editingKey,
  confirmingDeleteKey,
  groups,
  onMove,
  onEdit,
  onDoneEditing,
  onAskDelete,
  onCancelDelete,
}: {
  section: ProbeSection
  editingKey: string | null
  confirmingDeleteKey: string | null
  groups: ProbeGroup[]
  onMove: (probeId: string, direction: -1 | 1) => void
  onEdit: (key: string) => void
  onDoneEditing: () => void
  onAskDelete: (key: string) => void
  onCancelDelete: () => void
}) {
  return (
    <AdminSection
      id={`probe-section-${section.id}`}
      heading={section.heading}
      meta={section.kind === 'group' ? countOf(section.rows.length, 'probe') : 'Not in any group'}
    >
      {section.rows.length === 0 ? (
        <p className={EMPTY_STATE}>
          No probes in this group yet, so the dashboard leaves it out. Tick it under Groups when you add or edit a
          probe.
        </p>
      ) : (
        <div className={PANEL}>
          <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
            <span className="text-right">#</span>
            <span>Status</span>
            <span>Probe</span>
            <span>Kind</span>
            <span>Target</span>
            <span className="text-right">Every</span>
            <span className="text-right">Actions</span>
          </div>
          <ol className="flex list-none flex-col">
            {section.rows.map((row, index) => {
              const key = rowKey(section, row)

              return (
                <ProbeRow
                  key={key}
                  row={row}
                  position={index + 1}
                  isFirst={index === 0}
                  isLast={index === section.rows.length - 1}
                  isEditing={editingKey === key}
                  isConfirmingDelete={confirmingDeleteKey === key}
                  groups={groups}
                  onMove={(direction) => onMove(row.probe.id, direction)}
                  onEdit={() => onEdit(key)}
                  onDoneEditing={onDoneEditing}
                  onAskDelete={() => onAskDelete(key)}
                  onCancelDelete={onCancelDelete}
                />
              )
            })}
          </ol>
        </div>
      )}
    </AdminSection>
  )
}

function ProbeRow({
  row,
  position,
  isFirst,
  isLast,
  isEditing,
  isConfirmingDelete,
  groups,
  onMove,
  onEdit,
  onDoneEditing,
  onAskDelete,
  onCancelDelete,
}: {
  row: ProbeSectionRow
  position: number
  isFirst: boolean
  isLast: boolean
  isEditing: boolean
  isConfirmingDelete: boolean
  groups: ProbeGroup[]
  onMove: (direction: -1 | 1) => void
  onEdit: () => void
  onDoneEditing: () => void
  onAskDelete: () => void
  onCancelDelete: () => void
}) {
  const { probe, alsoIn } = row
  const setPaused = useSetProbePaused()
  const deleteProbe = useDeleteProbe()
  const state = probe.isPaused ? 'paused' : probe.status
  const editorId = `probe-editor-${probe.id}`
  const confirmId = `probe-confirm-delete-${probe.id}`

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${isEditing ? 'bg-bg' : (ROW_TINT[state] ?? '')}`}>
        <span className="mono col-start-1 row-start-1 text-right text-[13px] text-muted lg:col-start-auto lg:row-start-auto">
          {String(position).padStart(2, '0')}
        </span>
        <span className="col-start-3 row-start-1 flex lg:col-start-auto lg:row-start-auto">
          <StatusChip state={state} />
        </span>
        <span className="col-start-2 row-start-1 flex min-w-0 items-baseline gap-2 lg:col-start-auto lg:row-start-auto">
          <Link
            to={`/probes/${probe.id}`}
            className={`truncate text-[15px] font-semibold no-underline hover:underline ${state === 'paused' ? 'text-muted' : 'text-text'}`}
          >
            {probe.name}
          </Link>
          {alsoIn.length > 0 ? (
            <span className="shrink-0 rounded-full border border-line-strong px-2 text-[12px] text-muted">
              also in {alsoIn.join(', ')}
            </span>
          ) : null}
        </span>
        {/* One line under the name below lg; three cells of the grid at lg (`contents`). */}
        <span className="col-span-2 col-start-2 flex min-w-0 gap-1.5 text-[13px] text-muted lg:contents">
          <span>{PROBE_KIND_SHORT[probe.kind]}</span>
          <span aria-hidden="true" className="lg:hidden">
            ·
          </span>
          <span className="mono min-w-0 truncate" title={probeTarget(probe)}>
            {probeTarget(probe)}
          </span>
          <span aria-hidden="true" className="lg:hidden">
            ·
          </span>
          <span className="mono shrink-0 lg:text-right">{formatDuration(probe.pollIntervalSeconds)}</span>
        </span>
        <span className="col-span-3 flex items-center justify-end lg:col-span-1">
          <MoveButtons
            name={probe.name}
            isFirst={isFirst}
            isLast={isLast}
            onMove={onMove}
          />
          <IconButton
            icon={Pencil}
            label={`Edit ${probe.name}`}
            title="Edit"
            onClick={onEdit}
            aria-expanded={isEditing}
            aria-controls={isEditing ? editorId : undefined}
          />
          <IconButton
            icon={probe.isPaused ? Play : Pause}
            label={probe.isPaused ? `Unpause ${probe.name}` : `Pause ${probe.name}`}
            title={probe.isPaused ? 'Unpause' : 'Pause'}
            onClick={() => setPaused.mutate({ id: probe.id, isPaused: !probe.isPaused })}
            disabled={setPaused.isPending}
          />
          <IconButton
            icon={Trash2}
            tone="danger"
            label={`Delete ${probe.name}`}
            title="Delete"
            onClick={onAskDelete}
            aria-expanded={isConfirmingDelete}
            aria-controls={isConfirmingDelete ? confirmId : undefined}
          />
        </span>
      </div>
      {/*
        A strip under the row rather than buttons swapped into the action cell: the cell is a fixed
        216px so the columns line up, and the question needs room to say that a probe in several
        groups leaves all of them — deleting from one section is not "remove from this group".
      */}
      {isConfirmingDelete ? (
        <ConfirmStrip
          id={confirmId}
          question={
            alsoIn.length > 0
              ? `Delete ${probe.name}? It also leaves ${alsoIn.join(', ')}.`
              : `Delete ${probe.name}?`
          }
          confirmLabel={`Confirm delete ${probe.name}`}
          cancelLabel={`Cancel delete ${probe.name}`}
          onConfirm={() => deleteProbe.mutate(probe.id, { onSuccess: onCancelDelete })}
          onCancel={onCancelDelete}
          isPending={deleteProbe.isPending}
        />
      ) : null}
      {isEditing ? (
        <div id={editorId}>
          <ProbeForm probe={probe} groups={groups} variant="inline" onDoneEditing={onDoneEditing} />
        </div>
      ) : null}
    </li>
  )
}


/**
 * Adds a probe (`variant="card"`, the panel at the bottom of the page) or edits one
 * (`variant="inline"`, opened inside that probe's row). Same fields either way; the inline one
 * drops the panel chrome and takes focus, since the button that opened it is right above.
 */
function ProbeForm({
  probe,
  groups,
  variant,
  onDoneEditing,
}: {
  probe: Probe | null
  groups: ProbeGroup[]
  variant: 'card' | 'inline'
  onDoneEditing: () => void
}) {
  const createProbe = useCreateProbe()
  const updateProbe = useUpdateProbe()
  const isEditing = probe !== null
  const isInline = variant === 'inline'
  const Heading = isInline ? 'h3' : 'h2'
  const nameRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (isInline) {
      nameRef.current?.focus()
    }
  }, [isInline])

  const [name, setName] = useState(probe?.name ?? '')
  const [host, setHost] = useState(probe?.host ?? '')
  const [pollIntervalSeconds, setPollIntervalSeconds] = useState(probe?.pollIntervalSeconds ?? 60)
  const [failureThreshold, setFailureThreshold] = useState(probe?.failureThreshold ?? 2)
  const [groupIds, setGroupIds] = useState<string[]>(probe?.groupIds ?? [])

  // Kind is only ever chosen on the create form — a probe's kind cannot change after
  // creation (ProbeEndpoints ignores it on PUT), so the edit form renders it as text below
  // instead of this state. Unused while editing.
  const [kind, setKind] = useState<'ping' | 'http' | 'message'>(
    probe?.kind === 'http' || probe?.kind === 'message' ? probe.kind : 'ping',
  )

  // HTTP fieldset state — seeded from the probe's own options when editing an HTTP probe,
  // otherwise the domain's own defaults (HttpProbeOptions.cs). Harmless to hold even when
  // the probe is not HTTP; buildHttpPayload() below only reads it when isHttp is true.
  const [method, setMethod] = useState<'head' | 'get'>(probe?.http?.method ?? 'get')
  const [path, setPath] = useState(probe?.http?.path ?? '')
  const [useHttps, setUseHttps] = useState(probe?.http?.useHttps ?? false)
  const [ignoreCertificateErrors, setIgnoreCertificateErrors] = useState(
    probe?.http?.ignoreCertificateErrors ?? false,
  )
  const [timeoutSeconds, setTimeoutSeconds] = useState(probe?.http?.timeoutSeconds ?? 10)
  const [expectedStatusCode, setExpectedStatusCode] = useState(
    probe?.http?.expectedStatusCode !== null && probe?.http?.expectedStatusCode !== undefined
      ? String(probe.http.expectedStatusCode)
      : '',
  )
  const [expectedStatusCodeNegate, setExpectedStatusCodeNegate] = useState(
    probe?.http?.expectedStatusCodeNegate ?? false,
  )
  const [expectedBodyText, setExpectedBodyText] = useState(probe?.http?.expectedBodyText ?? '')
  const [expectedBodyTextNegate, setExpectedBodyTextNegate] = useState(
    probe?.http?.expectedBodyTextNegate ?? false,
  )
  const [credentialType, setCredentialType] = useState<'none' | 'bearer' | 'basic'>(
    probe?.http?.credential.type ?? 'none',
  )
  const [credentialUsername, setCredentialUsername] = useState(probe?.http?.credential.username ?? '')
  const [secret, setSecret] = useState('')
  const hasStoredSecret = probe?.http?.credential.hasSecret ?? false
  // Reveal the secret input immediately unless there is something to replace — the "Replace
  // credential" affordance plan 003's Step 6 asks for (never pre-fill a stored secret).
  const [replaceCredential, setReplaceCredential] = useState(!hasStoredSecret)

  const mutation = isEditing ? updateProbe : createProbe
  const isHttp = isEditing ? probe.kind === 'http' : kind === 'http'
  const isMessage = isEditing ? probe.kind === 'message' : kind === 'message'

  // Only fetched while the form is actually offering reporters: src/test/fetch.ts throws on an
  // undeclared path, so an unconditional query here would force every existing probe-page test
  // to declare /api/v1/reporters. See useReporters' own comment.
  const reporters = useReporters({ enabled: isMessage })
  const showSecretInput = credentialType !== 'none' && (!hasStoredSecret || replaceCredential)

  function toggleGroup(id: string) {
    setGroupIds((current) => (current.includes(id) ? current.filter((g) => g !== id) : [...current, id]))
  }

  function buildHttpPayload(): HttpProbeOptionsInput | undefined {
    if (!isHttp) {
      return undefined
    }

    const trimmedPath = path.trim()
    const trimmedExpectedStatus = expectedStatusCode.trim()
    const trimmedExpectedBody = expectedBodyText.trim()

    return {
      method,
      path: trimmedPath,
      useHttps,
      ignoreCertificateErrors,
      timeoutSeconds,
      expectedStatusCode: trimmedExpectedStatus === '' ? null : Number(trimmedExpectedStatus),
      expectedStatusCodeNegate,
      // A HEAD response has no body — ProbeEndpoints 400s a HEAD probe with a body
      // expectation, so this never sends one regardless of leftover form state.
      expectedBodyText: method === 'head' || trimmedExpectedBody === '' ? null : trimmedExpectedBody,
      expectedBodyTextNegate,
      credential: {
        type: credentialType,
        username: credentialType === 'basic' ? credentialUsername.trim() : null,
        ...(showSecretInput ? { secret } : {}),
      },
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const trimmedHost = host.trim()
    const http = buildHttpPayload()

    if (isEditing && probe) {
      updateProbe.mutate(
        {
          id: probe.id,
          name: trimmedName,
          host: trimmedHost,
          pollIntervalSeconds,
          failureThreshold,
          groupIds,
          http,
        },
        { onSuccess: onDoneEditing },
      )
      return
    }

    createProbe.mutate(
      { name: trimmedName, host: trimmedHost, kind, pollIntervalSeconds, failureThreshold, groupIds, http },
      {
        onSuccess: () => {
          setName('')
          setHost('')
          setKind('ping')
          setPollIntervalSeconds(60)
          setFailureThreshold(2)
          setGroupIds([])
          setMethod('get')
          setPath('')
          setUseHttps(false)
          setIgnoreCertificateErrors(false)
          setTimeoutSeconds(10)
          setExpectedStatusCode('')
          setExpectedStatusCodeNegate(false)
          setExpectedBodyText('')
          setExpectedBodyTextNegate(false)
          setCredentialType('none')
          setCredentialUsername('')
          setSecret('')
          setReplaceCredential(true)
        },
      },
    )
  }

  return (
    <form
      onSubmit={onSubmit}
      aria-labelledby="probe-form-heading"
      className={isInline ? INLINE_FORM : CARD_FORM}
    >
      <Heading id="probe-form-heading" className={isInline ? 'text-[14px] font-semibold' : 'text-[15px] font-semibold'}>
        {isEditing ? `Edit ${probe.name}` : 'Add a probe'}
      </Heading>
      {isEditing ? (
        <p className="text-[14px] text-muted">Kind: {PROBE_KIND_LABEL[probe.kind]} — cannot be changed after creation.</p>
      ) : null}
      {/*
        As many 180px-or-wider columns as fit: the add form has five base fields and the editor
        four (kind is fixed once created), so a fixed column count leaves one of them stranded on
        a row of its own.
      */}
      <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,180px),1fr))] gap-4">
        <p className="flex flex-col gap-1">
          <label htmlFor="probe-name" className={FIELD_LABEL}>
            Name
          </label>
          <input
            ref={nameRef}
            id="probe-name"
            name="name"
            required
            value={name}
            onChange={(event) => setName(event.target.value)}
            className={FIELD_INPUT}
          />
        </p>
        {/*
          A message probe adds no per-kind fieldset, unlike 003/004/005 — it has no options at all,
          because what it watches is a reporter and when it next expects to hear from it is the
          reporter's own business. What it replaces instead is this control: `Probe.Host` holds the
          reporter's identifier (plan 021's Decision 3), which the API validates against an existing
          reporter, so a free-text field here would be a way to typo a probe into permanent Unknown.
          Any later kind whose host is a chosen thing rather than a typed one follows this shape.
        */}
        {isMessage ? (
          reporters.data?.length ? (
            <p className="flex flex-col gap-1">
              <label htmlFor="probe-host" className={FIELD_LABEL}>
                Reporter
              </label>
              <select
                id="probe-host"
                name="host"
                required
                value={host}
                onChange={(event) => setHost(event.target.value)}
                className={FIELD_INPUT}
              >
                <option value="">Choose a reporter</option>
                {reporters.data.map((reporter) => (
                  <option key={reporter.id} value={reporter.identifier}>
                    {reporter.name}
                  </option>
                ))}
              </select>
            </p>
          ) : (
            <p className="text-[14px] text-muted">
              No reporters yet.{' '}
              <Link
                to="/admin/reporters"
                className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
              >
                Add a reporter
              </Link>{' '}
              before adding a message probe.
            </p>
          )
        ) : (
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-host" className={FIELD_LABEL}>
              Host
            </label>
            <input
              id="probe-host"
              name="host"
              required
              value={host}
              onChange={(event) => setHost(event.target.value)}
              className={FIELD_INPUT}
            />
          </p>
        )}
        {isEditing ? null : (
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-kind" className={FIELD_LABEL}>
              Kind
            </label>
            <select
              id="probe-kind"
              name="kind"
              value={kind}
              onChange={(event) => setKind(event.target.value as 'ping' | 'http' | 'message')}
              className={FIELD_INPUT}
            >
              {CREATABLE_KINDS.map((option) => (
                <option key={option} value={option}>
                  {PROBE_KIND_LABEL[option]}
                </option>
              ))}
            </select>
          </p>
        )}
        <p className="flex flex-col gap-1">
          <label htmlFor="probe-poll-interval" className={FIELD_LABEL}>
            Poll interval (seconds)
          </label>
          <input
            id="probe-poll-interval"
            name="pollIntervalSeconds"
            type="number"
            // Probe.MinPollIntervalSeconds / MaxPollIntervalSeconds — the API still validates;
            // these only stop the browser submitting a value it would reject.
            min={15}
            max={86400}
            required
            value={pollIntervalSeconds}
            onChange={(event) => setPollIntervalSeconds(Number(event.target.value))}
            className={FIELD_INPUT}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="probe-failure-threshold" className={FIELD_LABEL}>
            Failure threshold
          </label>
          <input
            id="probe-failure-threshold"
            name="failureThreshold"
            type="number"
            // Probe.MinFailureThreshold / MaxFailureThreshold.
            min={1}
            max={10}
            required
            value={failureThreshold}
            onChange={(event) => setFailureThreshold(Number(event.target.value))}
            className={FIELD_INPUT}
          />
        </p>
      </div>
      {isHttp ? (
        // 004 (SMB) and 005 (SNMP) add a sibling fieldset here, keyed the same way, rather
        // than restructuring this block — see plan 003's Step 6 and its Maintenance notes.
        <fieldset id="probe-http-fieldset" className={FIELDSET}>
          <legend className={LEGEND}>HTTP</legend>
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-http-method" className={FIELD_LABEL}>
              Method
            </label>
            <select
              id="probe-http-method"
              value={method}
              onChange={(event) => setMethod(event.target.value as 'head' | 'get')}
              className={FIELD_INPUT}
            >
              <option value="get">GET</option>
              <option value="head">HEAD</option>
            </select>
          </p>
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-http-path" className={FIELD_LABEL}>
              Path
            </label>
            <input
              id="probe-http-path"
              required
              value={path}
              onChange={(event) => setPath(event.target.value)}
              className={FIELD_INPUT}
            />
          </p>
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-http-timeout" className={FIELD_LABEL}>
              Timeout (seconds)
            </label>
            <input
              id="probe-http-timeout"
              type="number"
              min={1}
              max={25}
              required
              value={timeoutSeconds}
              onChange={(event) => setTimeoutSeconds(Number(event.target.value))}
              className={FIELD_INPUT}
            />
          </p>
          <p>
            <label className="flex items-center gap-2 text-[14px] text-text">
              <input
                type="checkbox"
                checked={useHttps}
                onChange={(event) => setUseHttps(event.target.checked)}
                className="size-4 rounded border-line"
              />
              Use HTTPS
            </label>
          </p>
          <p>
            <label className="flex items-center gap-2 text-[14px] text-text">
              <input
                type="checkbox"
                checked={ignoreCertificateErrors}
                onChange={(event) => setIgnoreCertificateErrors(event.target.checked)}
                className="size-4 rounded border-line"
              />
              Skip certificate validation (self-signed certificates)
            </label>
          </p>
          <p className="flex flex-col gap-1">
            <label htmlFor="probe-http-expected-status" className={FIELD_LABEL}>
              Expected status code
            </label>
            <input
              id="probe-http-expected-status"
              type="number"
              value={expectedStatusCode}
              onChange={(event) => setExpectedStatusCode(event.target.value)}
              className={FIELD_INPUT}
            />
          </p>
          <p>
            <label className="flex items-center gap-2 text-[14px] text-text">
              <input
                type="checkbox"
                checked={expectedStatusCodeNegate}
                onChange={(event) => setExpectedStatusCodeNegate(event.target.checked)}
                className="size-4 rounded border-line"
              />
              Treat that status code as unexpected instead
            </label>
          </p>
          {method === 'get' ? (
            <>
              <p className="flex flex-col gap-1">
                <label htmlFor="probe-http-expected-body" className={FIELD_LABEL}>
                  Expected body text
                </label>
                <input
                  id="probe-http-expected-body"
                  value={expectedBodyText}
                  onChange={(event) => setExpectedBodyText(event.target.value)}
                  className={FIELD_INPUT}
                />
              </p>
              <p>
                <label className="flex items-center gap-2 text-[14px] text-text">
                  <input
                    type="checkbox"
                    checked={expectedBodyTextNegate}
                    onChange={(event) => setExpectedBodyTextNegate(event.target.checked)}
                    className="size-4 rounded border-line"
                  />
                  Treat that body text as unexpected instead
                </label>
              </p>
            </>
          ) : null}
          <fieldset className={FIELDSET}>
            <legend className={LEGEND}>Credential</legend>
            <p className="flex flex-col gap-1">
              <label htmlFor="probe-http-credential-type" className={FIELD_LABEL}>
                Credential type
              </label>
              <select
                id="probe-http-credential-type"
                value={credentialType}
                onChange={(event) => setCredentialType(event.target.value as 'none' | 'bearer' | 'basic')}
                className={FIELD_INPUT}
              >
                <option value="none">None</option>
                <option value="bearer">Bearer token</option>
                <option value="basic">Basic auth</option>
              </select>
            </p>
            {credentialType === 'basic' ? (
              <p className="flex flex-col gap-1">
                <label htmlFor="probe-http-credential-username" className={FIELD_LABEL}>
                  Username
                </label>
                <input
                  id="probe-http-credential-username"
                  required
                  value={credentialUsername}
                  onChange={(event) => setCredentialUsername(event.target.value)}
                  className={FIELD_INPUT}
                />
              </p>
            ) : null}
            {credentialType !== 'none' ? (
              showSecretInput ? (
                <p className="flex flex-col gap-1">
                  <label htmlFor="probe-http-credential-secret" className={FIELD_LABEL}>
                    {credentialType === 'bearer' ? 'Bearer token' : 'Password'}
                  </label>
                  <input
                    id="probe-http-credential-secret"
                    type="password"
                    value={secret}
                    onChange={(event) => setSecret(event.target.value)}
                    className={FIELD_INPUT}
                  />
                  {hasStoredSecret ? (
                    <button
                      type="button"
                      onClick={() => {
                        setReplaceCredential(false)
                        setSecret('')
                      }}
                      className={`${BUTTON_SECONDARY} w-fit`}
                    >
                      Keep the current secret
                    </button>
                  ) : null}
                </p>
              ) : (
                <p className="text-[14px] text-muted">
                  A secret is already set.{' '}
                  <button type="button" onClick={() => setReplaceCredential(true)} className={`${BUTTON_SECONDARY} ml-1`}>
                    Replace credential
                  </button>
                </p>
              )
            ) : null}
          </fieldset>
        </fieldset>
      ) : null}
      <fieldset className={FIELDSET}>
        <legend className={LEGEND}>Groups</legend>
        {groups.length === 0 ? (
          <p className="text-[14px] text-muted">
            No groups yet. <Link to="/admin/probe-groups" className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">Add one</Link>.
          </p>
        ) : (
          // Pills in a wrapping row rather than one checkbox per line: a household with six groups
          // otherwise spends six lines of every editor on them. The whole pill is the label, so it
          // is the 40px target, not the 16px box inside it.
          <p className="flex flex-wrap gap-2">
            {groups.map((group) => (
              <label
                key={group.id}
                className="inline-flex h-10 items-center gap-2 rounded-full border border-line px-3.5 text-[14px] text-text hover:border-line-strong has-checked:border-line-strong has-checked:bg-bg"
              >
                <input
                  type="checkbox"
                  checked={groupIds.includes(group.id)}
                  onChange={() => toggleGroup(group.id)}
                  className="size-4 rounded border-line accent-current"
                />
                {group.name}
              </label>
            ))}
          </p>
        )}
      </fieldset>
      {mutation.isError ? <p role="alert" className={ALERT}>{problemDetail(mutation.error) ?? 'Could not save the probe. Try again.'}</p> : null}
      <p className="flex gap-2">
        <button type="submit" disabled={mutation.isPending} className={BUTTON_PRIMARY}>
          {isEditing ? 'Save' : 'Add probe'}
        </button>
        {isEditing ? (
          <button type="button" onClick={onDoneEditing} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        ) : null}
      </p>
    </form>
  )
}
