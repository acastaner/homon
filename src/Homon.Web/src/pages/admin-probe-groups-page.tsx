import { useState, type ChangeEvent, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Minus, Pencil, Plus, Trash2 } from 'lucide-react'

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
} from '@/components/admin-classes'
import { AdminPageHeader, jumpToField } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton, MoveButtons } from '@/components/icon-button'
import { StatusChip } from '@/components/status-chip'
import { summariseGroups } from '@/lib/admin-summary'
import { problemDetail } from '@/lib/api'
import {
  useCreateProbeGroup,
  useDeleteProbeGroup,
  useProbeGroups,
  useRenameProbeGroup,
  useReorderProbeGroups,
  useSetProbeGroupMembers,
  type ProbeGroup,
} from '@/lib/probe-groups'
import { PROBE_KIND_SHORT, probeTarget } from '@/lib/probe-sections'
import { useProbes, type Probe } from '@/lib/probes'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/*
 * One grid per row, so every row and the column head share this template or the columns stop
 * lining up — hence the fixed 176px action column (four 40px buttons and a divider). Below `lg` a
 * row folds as the Probes page's does: position and name on the first line, count and members on
 * the second, actions on the third.
 */
const ROW_GRID =
  'grid grid-cols-[24px_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[28px_minmax(0,220px)_80px_minmax(0,1fr)_176px] lg:gap-x-3.5'

/**
 * The probe-group admin page: reorder groups, rename or delete one (its probes are kept), and
 * manage each group's membership and their order within it. Edit opens a panel inside the row.
 * Every action here sends the full new list immediately — there is no separate "save" step and no
 * unsaved state.
 */
export function AdminProbeGroupsPage() {
  useDocumentTitle(pageTitle('Probe groups', 'Admin'))

  const groups = useProbeGroups()
  const probes = useProbes()
  const reorderGroups = useReorderProbeGroups()

  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)

  const orderedGroups = groups.data ?? []
  const allProbes = probes.data ?? []
  // The dashboard's "Other" section, in the probes' own order.
  const ungrouped = allProbes.filter((probe) => probe.groupIds.length === 0)

  function moveGroup(id: string, direction: -1 | 1) {
    const ids = orderedGroups.map((group) => group.id)
    const index = ids.indexOf(id)
    const swapWith = index + direction

    if (swapWith < 0 || swapWith >= ids.length) {
      return
    }

    const next = [...ids]
    ;[next[index], next[swapWith]] = [next[swapWith], next[index]]
    reorderGroups.mutate(next)
  }

  return (
    <>
      <AdminPageHeader
        title="Probe groups"
        description="The dashboard's sections, top to bottom. Deleting a group keeps its probes; any that are in no other group move to Other."
        count={summariseGroups(orderedGroups, allProbes).text}
      >
        <Link to="/admin/probes" className={BUTTON_SECONDARY}>
          Probes
        </Link>
        <button type="button" onClick={() => jumpToField('new-group-name')} className={BUTTON_PRIMARY}>
          <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
          New group
        </button>
      </AdminPageHeader>
      <AdminSection id="groups" heading="Groups" meta="The order here is the dashboard's">
        {orderedGroups.length === 0 ? (
          <p className={EMPTY_STATE}>No groups yet. Add one below.</p>
        ) : (
          <div className={PANEL}>
            <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
              <span className="text-right">#</span>
              <span>Group</span>
              <span className="text-right">Probes</span>
              <span>In it, in order</span>
              <span className="text-right">Actions</span>
            </div>
            <ol aria-label="Probe groups" className="flex list-none flex-col">
              {orderedGroups.map((group, index) => (
                <GroupRow
                  key={group.id}
                  group={group}
                  allProbes={allProbes}
                  position={index + 1}
                  isFirst={index === 0}
                  isLast={index === orderedGroups.length - 1}
                  isEditing={editingId === group.id}
                  isConfirmingDelete={confirmingDeleteId === group.id}
                  onMove={(direction) => moveGroup(group.id, direction)}
                  onEdit={() => {
                    setConfirmingDeleteId(null)
                    setEditingId((current) => (current === group.id ? null : group.id))
                  }}
                  onDoneEditing={() => setEditingId(null)}
                  onAskDelete={() => {
                    setEditingId(null)
                    setConfirmingDeleteId((current) => (current === group.id ? null : group.id))
                  }}
                  onCancelDelete={() => setConfirmingDeleteId(null)}
                />
              ))}
            </ol>
          </div>
        )}
      </AdminSection>
      {ungrouped.length > 0 ? (
        <AdminSection id="other" heading="Other" meta="In no group · shown last on the dashboard">
          <ul aria-label="Probes in no group" className={`${PANEL} flex list-none flex-wrap gap-2 p-3.5`}>
            {ungrouped.map((probe) => (
              <li key={probe.id}>
                <Link
                  to={`/probes/${probe.id}`}
                  className="inline-flex h-10 items-center rounded-full border border-line px-3.5 text-[14px] text-text no-underline hover:border-line-strong hover:bg-bg"
                >
                  {probe.name}
                </Link>
              </li>
            ))}
          </ul>
        </AdminSection>
      ) : null}
      <CreateGroupForm />
    </>
  )
}

function GroupRow({
  group,
  allProbes,
  position,
  isFirst,
  isLast,
  isEditing,
  isConfirmingDelete,
  onMove,
  onEdit,
  onDoneEditing,
  onAskDelete,
  onCancelDelete,
}: {
  group: ProbeGroup
  allProbes: Probe[]
  position: number
  isFirst: boolean
  isLast: boolean
  isEditing: boolean
  isConfirmingDelete: boolean
  onMove: (direction: -1 | 1) => void
  onEdit: () => void
  onDoneEditing: () => void
  onAskDelete: () => void
  onCancelDelete: () => void
}) {
  const deleteGroup = useDeleteProbeGroup()
  const editorId = `group-editor-${group.id}`
  const confirmId = `group-confirm-delete-${group.id}`

  const members = group.probeIds
    .map((id) => allProbes.find((probe) => probe.id === id))
    .filter((probe): probe is Probe => probe !== undefined)

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${isEditing ? 'bg-bg' : ''}`}>
        <span className="mono col-start-1 row-start-1 text-right text-[13px] text-muted lg:col-start-auto lg:row-start-auto">
          {String(position).padStart(2, '0')}
        </span>
        <span className="col-span-2 col-start-2 row-start-1 min-w-0 truncate text-[15px] font-semibold lg:col-span-1 lg:col-start-auto lg:row-start-auto">
          {group.name}
        </span>
        {/* One line under the name below lg; two cells of the grid at lg (`contents`). */}
        <span className="col-span-2 col-start-2 flex min-w-0 gap-1.5 text-[13px] text-muted lg:contents">
          <span className="mono shrink-0 lg:text-right">
            {group.probeIds.length}
            <span className="lg:hidden"> {group.probeIds.length === 1 ? 'probe' : 'probes'}</span>
          </span>
          <span aria-hidden="true" className="lg:hidden">
            ·
          </span>
          <span className="min-w-0 truncate" title={members.map((probe) => probe.name).join(' · ')}>
            {members.length > 0 ? members.map((probe) => probe.name).join(' · ') : 'No probes yet'}
          </span>
        </span>
        <span className="col-span-3 flex items-center justify-end lg:col-span-1">
          <MoveButtons name={group.name} isFirst={isFirst} isLast={isLast} onMove={onMove} />
          <IconButton
            icon={Pencil}
            label={`Edit ${group.name}`}
            title="Edit"
            onClick={onEdit}
            aria-expanded={isEditing}
            aria-controls={isEditing ? editorId : undefined}
          />
          <IconButton
            icon={Trash2}
            tone="danger"
            label={`Delete ${group.name}`}
            title="Delete"
            onClick={onAskDelete}
            aria-expanded={isConfirmingDelete}
            aria-controls={isConfirmingDelete ? confirmId : undefined}
          />
        </span>
      </div>
      {isConfirmingDelete ? (
        <ConfirmStrip
          id={confirmId}
          question={`Delete ${group.name}? Its probes are kept.`}
          confirmLabel={`Confirm delete ${group.name}`}
          cancelLabel={`Cancel delete ${group.name}`}
          onConfirm={() => deleteGroup.mutate(group.id, { onSuccess: onCancelDelete })}
          onCancel={onCancelDelete}
          isPending={deleteGroup.isPending}
        />
      ) : null}
      {isEditing ? (
        <GroupEditor group={group} members={members} allProbes={allProbes} editorId={editorId} onDone={onDoneEditing} />
      ) : null}
    </li>
  )
}

/**
 * What opens inside a group's row: the rename form, then the members in dashboard order, then
 * the select that adds one. A `<div>`, not a `<form>`: the rename is a form of its own, and a
 * form cannot nest in one. Membership edits save at once, so Done only closes the panel.
 */
function GroupEditor({
  group,
  members,
  allProbes,
  editorId,
  onDone,
}: {
  group: ProbeGroup
  members: Probe[]
  allProbes: Probe[]
  editorId: string
  onDone: () => void
}) {
  const renameGroup = useRenameProbeGroup()
  const setMembers = useSetProbeGroupMembers()
  const [newName, setNewName] = useState(group.name)

  const nonMembers = allProbes.filter((probe) => !group.probeIds.includes(probe.id))

  function moveMember(id: string, direction: -1 | 1) {
    const ids = [...group.probeIds]
    const index = ids.indexOf(id)
    const swapWith = index + direction

    if (swapWith < 0 || swapWith >= ids.length) {
      return
    }

    ;[ids[index], ids[swapWith]] = [ids[swapWith], ids[index]]
    setMembers.mutate({ id: group.id, probeIds: ids })
  }

  function removeMember(id: string) {
    setMembers.mutate({ id: group.id, probeIds: group.probeIds.filter((probeId) => probeId !== id) })
  }

  function addMember(event: ChangeEvent<HTMLSelectElement>) {
    const probeId = event.target.value

    if (probeId.length === 0) {
      return
    }

    setMembers.mutate({ id: group.id, probeIds: [...group.probeIds, probeId] })
    event.target.value = ''
  }

  function onRenameSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmed = newName.trim()

    if (trimmed.length === 0) {
      return
    }

    renameGroup.mutate({ id: group.id, name: trimmed })
  }

  return (
    <div id={editorId} className={INLINE_FORM}>
      <form onSubmit={onRenameSubmit} aria-label={`Rename ${group.name}`} className="flex flex-col gap-2">
        <p className="flex flex-col gap-1">
          <label htmlFor={`group-name-${group.id}`} className={FIELD_LABEL}>
            Name
          </label>
          <span className="flex flex-wrap gap-2">
            <input
              id={`group-name-${group.id}`}
              required
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              className={`${FIELD_INPUT} max-w-[320px] flex-1`}
            />
            <button type="submit" disabled={renameGroup.isPending} className={BUTTON_PRIMARY}>
              Save name
            </button>
          </span>
        </p>
        {renameGroup.isError ? (
          <p role="alert" className={ALERT}>
            {problemDetail(renameGroup.error) ?? 'Could not rename the group. Try again.'}
          </p>
        ) : null}
      </form>
      <div className="flex flex-col gap-2">
        <h3 className="text-[14px] font-semibold">Probes in {group.name}, in dashboard order</h3>
        {members.length === 0 ? (
          <p className="text-[13.5px] text-muted">No probes in {group.name} yet.</p>
        ) : (
          <ol aria-label={`Probes in ${group.name}`} className="flex list-none flex-col rounded-md border border-line bg-surface">
            {members.map((probe, index) => (
              <li
                key={probe.id}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-line px-3 py-1.5 first:border-t-0"
              >
                <StatusChip state={probe.isPaused ? 'paused' : probe.status} />
                <span className="min-w-0 text-[14px] font-semibold">{probe.name}</span>
                <span className="mono min-w-0 flex-1 truncate text-[13px] text-muted" title={probeTarget(probe)}>
                  {PROBE_KIND_SHORT[probe.kind]} · {probeTarget(probe)}
                </span>
                <span className="ml-auto flex items-center">
                  <MoveButtons
                    name={probe.name}
                    labelSuffix={` in ${group.name}`}
                    isFirst={index === 0}
                    isLast={index === members.length - 1}
                    onMove={(direction) => moveMember(probe.id, direction)}
                  />
                  <IconButton
                    icon={Minus}
                    tone="danger"
                    label={`Remove ${probe.name} from ${group.name}`}
                    onClick={() => removeMember(probe.id)}
                  />
                </span>
              </li>
            ))}
          </ol>
        )}
      </div>
      <p className="flex flex-col gap-1">
        <label htmlFor={`add-probe-${group.id}`} className={FIELD_LABEL}>
          Add probe to {group.name}
        </label>
        <select id={`add-probe-${group.id}`} defaultValue="" onChange={addMember} className={`${FIELD_INPUT} max-w-[320px]`}>
          <option value="">Choose a probe…</option>
          {nonMembers.map((probe) => (
            <option key={probe.id} value={probe.id}>
              {probe.name}
            </option>
          ))}
        </select>
        <span className="text-[13px] text-muted">Adding, removing and reordering probes saves at once.</span>
      </p>
      <p>
        <button type="button" onClick={onDone} className={BUTTON_SECONDARY}>
          Done
        </button>
      </p>
    </div>
  )
}

function CreateGroupForm() {
  const createGroup = useCreateProbeGroup()
  const [name, setName] = useState('')

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmed = name.trim()

    if (trimmed.length === 0) {
      return
    }

    createGroup.mutate(trimmed, { onSuccess: () => setName('') })
  }

  return (
    <form onSubmit={onSubmit} aria-labelledby="create-group-heading" className={CARD_FORM}>
      <h2 id="create-group-heading" className="text-[15px] font-semibold">
        New group
      </h2>
      <p className="flex flex-col gap-1">
        <label htmlFor="new-group-name" className={FIELD_LABEL}>
          Group name
        </label>
        <input
          id="new-group-name"
          required
          value={name}
          onChange={(event) => setName(event.target.value)}
          className={`${FIELD_INPUT} max-w-[420px]`}
        />
      </p>
      {createGroup.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(createGroup.error) ?? 'Could not add the group. Try again.'}
        </p>
      ) : null}
      <p className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={createGroup.isPending} className={BUTTON_PRIMARY}>
          Add group
        </button>
        <span className="text-[13px] text-muted">Goes to the bottom of the dashboard, above Other.</span>
      </p>
    </form>
  )
}
