import { useState, type ChangeEvent, type FormEvent } from 'react'

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
import { useProbes, type Probe } from '@/lib/probes'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

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

/**
 * The probe-group admin page: reorder groups, rename or delete one (its probes are kept),
 * and manage each group's membership and their order within it. Every action here sends the
 * full new list immediately — there is no separate "save" step and no unsaved state.
 */
export function AdminProbeGroupsPage() {
  useDocumentTitle(pageTitle('Probe groups', 'Admin'))

  const groups = useProbeGroups()
  const probes = useProbes()
  const reorderGroups = useReorderProbeGroups()

  const orderedGroups = groups.data ?? []
  const allProbes = probes.data ?? []

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
      <h1 className={PAGE_H1}>Probe groups</h1>
      {orderedGroups.length === 0 ? (
        <p className="rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted">
          No groups yet. Add one below.
        </p>
      ) : (
        <ol aria-label="Probe groups" className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4">
          {orderedGroups.map((group, index) => (
            <li key={group.id} className="py-3">
              <GroupCard
                group={group}
                allProbes={allProbes}
                canMoveUp={index > 0}
                canMoveDown={index < orderedGroups.length - 1}
                onMoveUp={() => moveGroup(group.id, -1)}
                onMoveDown={() => moveGroup(group.id, 1)}
              />
            </li>
          ))}
        </ol>
      )}
      <CreateGroupForm />
    </>
  )
}

function GroupCard({
  group,
  allProbes,
  canMoveUp,
  canMoveDown,
  onMoveUp,
  onMoveDown,
}: {
  group: ProbeGroup
  allProbes: Probe[]
  canMoveUp: boolean
  canMoveDown: boolean
  onMoveUp: () => void
  onMoveDown: () => void
}) {
  const renameGroup = useRenameProbeGroup()
  const deleteGroup = useDeleteProbeGroup()
  const setMembers = useSetProbeGroupMembers()

  const [isRenaming, setIsRenaming] = useState(false)
  const [newName, setNewName] = useState(group.name)
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false)

  const members = group.probeIds
    .map((id) => allProbes.find((probe) => probe.id === id))
    .filter((probe): probe is Probe => probe !== undefined)

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

    renameGroup.mutate({ id: group.id, name: trimmed }, { onSuccess: () => setIsRenaming(false) })
  }

  return (
    <div className="flex flex-col gap-3">
      {isRenaming ? (
        <form onSubmit={onRenameSubmit} aria-label={`Rename ${group.name}`} className="flex flex-col gap-2">
          <p className="flex flex-col gap-1">
            <label htmlFor={`rename-${group.id}`} className={FIELD_LABEL}>
              New name for {group.name}
            </label>
            <input
              id={`rename-${group.id}`}
              required
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              className={FIELD_INPUT}
            />
          </p>
          {renameGroup.isError ? (
            <p role="alert" className={ALERT}>
              {problemDetail(renameGroup.error) ?? 'Could not rename the group. Try again.'}
            </p>
          ) : null}
          <p className="flex gap-2">
            <button type="submit" disabled={renameGroup.isPending} className={BUTTON_PRIMARY}>
              Save name
            </button>
            <button type="button" onClick={() => setIsRenaming(false)} className={BUTTON_SECONDARY}>
              Cancel
            </button>
          </p>
        </form>
      ) : (
        <p className="text-[15px]">
          <strong className="font-semibold">{group.name}</strong>
        </p>
      )}
      <p className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={onMoveUp} disabled={!canMoveUp} className={BUTTON_SECONDARY}>
          Move {group.name} up
        </button>
        <button type="button" onClick={onMoveDown} disabled={!canMoveDown} className={BUTTON_SECONDARY}>
          Move {group.name} down
        </button>
        {!isRenaming ? (
          <button type="button" onClick={() => setIsRenaming(true)} className={BUTTON_SECONDARY}>
            Rename {group.name}
          </button>
        ) : null}
        {isConfirmingDelete ? (
          <>
            <button
              type="button"
              onClick={() => deleteGroup.mutate(group.id, { onSuccess: () => setIsConfirmingDelete(false) })}
              className={BUTTON_DANGER}
            >
              Confirm delete {group.name}
            </button>
            <button type="button" onClick={() => setIsConfirmingDelete(false)} className={BUTTON_SECONDARY}>
              Cancel delete {group.name}
            </button>
          </>
        ) : (
          <button type="button" onClick={() => setIsConfirmingDelete(true)} className={BUTTON_SECONDARY}>
            Delete {group.name}
          </button>
        )}
        {isConfirmingDelete ? <span className="text-[13px] text-muted">Its probes are kept.</span> : null}
      </p>
      {members.length === 0 ? (
        <p className="text-[13.5px] text-muted">No probes in {group.name} yet.</p>
      ) : (
        <ul aria-label={`Probes in ${group.name}`} className="flex list-none flex-col divide-y divide-line rounded-md border border-line px-3">
          {members.map((probe, index) => (
            <li key={probe.id} className="flex flex-wrap items-center gap-2 py-2">
              <span className="text-[14px]">{probe.name}</span>
              <span className="ml-auto flex flex-wrap gap-2">
                <button type="button" onClick={() => moveMember(probe.id, -1)} disabled={index === 0} className={BUTTON_SECONDARY}>
                  Move {probe.name} up
                </button>
                <button
                  type="button"
                  onClick={() => moveMember(probe.id, 1)}
                  disabled={index === members.length - 1}
                  className={BUTTON_SECONDARY}
                >
                  Move {probe.name} down
                </button>
                <button type="button" onClick={() => removeMember(probe.id)} className={BUTTON_SECONDARY}>
                  Remove {probe.name}
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
      <p className="flex flex-col gap-1">
        <label htmlFor={`add-probe-${group.id}`} className={FIELD_LABEL}>
          Add probe to {group.name}
        </label>
        <select id={`add-probe-${group.id}`} defaultValue="" onChange={addMember} className={FIELD_INPUT}>
          <option value="">Choose a probe…</option>
          {nonMembers.map((probe) => (
            <option key={probe.id} value={probe.id}>
              {probe.name}
            </option>
          ))}
        </select>
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
    <form onSubmit={onSubmit} aria-labelledby="create-group-heading" className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5">
      <h2 id="create-group-heading" className="text-[15px] font-semibold">
        Add a group
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
          className={FIELD_INPUT}
        />
      </p>
      {createGroup.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(createGroup.error) ?? 'Could not add the group. Try again.'}
        </p>
      ) : null}
      <p>
        <button type="submit" disabled={createGroup.isPending} className={BUTTON_PRIMARY}>
          Add group
        </button>
      </p>
    </form>
  )
}
