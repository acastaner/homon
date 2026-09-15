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
      <h1>Probe groups</h1>
      {orderedGroups.length === 0 ? (
        <p>No groups yet. Add one below.</p>
      ) : (
        <ol aria-label="Probe groups">
          {orderedGroups.map((group, index) => (
            <li key={group.id}>
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
    <>
      {isRenaming ? (
        <form onSubmit={onRenameSubmit} aria-label={`Rename ${group.name}`}>
          <p>
            <label htmlFor={`rename-${group.id}`}>New name for {group.name}</label>
            <input
              id={`rename-${group.id}`}
              required
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
            />
          </p>
          {renameGroup.isError ? (
            <p role="alert">{problemDetail(renameGroup.error) ?? 'Could not rename the group. Try again.'}</p>
          ) : null}
          <p>
            <button type="submit" disabled={renameGroup.isPending}>
              Save name
            </button>{' '}
            <button type="button" onClick={() => setIsRenaming(false)}>
              Cancel
            </button>
          </p>
        </form>
      ) : (
        <p>
          <strong>{group.name}</strong>
        </p>
      )}
      <p>
        <button type="button" onClick={onMoveUp} disabled={!canMoveUp}>
          Move {group.name} up
        </button>{' '}
        <button type="button" onClick={onMoveDown} disabled={!canMoveDown}>
          Move {group.name} down
        </button>{' '}
        {!isRenaming ? (
          <button type="button" onClick={() => setIsRenaming(true)}>
            Rename {group.name}
          </button>
        ) : null}{' '}
        {isConfirmingDelete ? (
          <>
            <button
              type="button"
              onClick={() => deleteGroup.mutate(group.id, { onSuccess: () => setIsConfirmingDelete(false) })}
            >
              Confirm delete {group.name}
            </button>{' '}
            <button type="button" onClick={() => setIsConfirmingDelete(false)}>
              Cancel delete {group.name}
            </button>
          </>
        ) : (
          <button type="button" onClick={() => setIsConfirmingDelete(true)}>
            Delete {group.name}
          </button>
        )}
        {isConfirmingDelete ? <span> Its probes are kept.</span> : null}
      </p>
      {members.length === 0 ? (
        <p>No probes in {group.name} yet.</p>
      ) : (
        <ul aria-label={`Probes in ${group.name}`}>
          {members.map((probe, index) => (
            <li key={probe.id}>
              {probe.name}{' '}
              <button type="button" onClick={() => moveMember(probe.id, -1)} disabled={index === 0}>
                Move {probe.name} up
              </button>{' '}
              <button
                type="button"
                onClick={() => moveMember(probe.id, 1)}
                disabled={index === members.length - 1}
              >
                Move {probe.name} down
              </button>{' '}
              <button type="button" onClick={() => removeMember(probe.id)}>
                Remove {probe.name}
              </button>
            </li>
          ))}
        </ul>
      )}
      <p>
        <label htmlFor={`add-probe-${group.id}`}>Add probe to {group.name}</label>
        <select id={`add-probe-${group.id}`} defaultValue="" onChange={addMember}>
          <option value="">Choose a probe…</option>
          {nonMembers.map((probe) => (
            <option key={probe.id} value={probe.id}>
              {probe.name}
            </option>
          ))}
        </select>
      </p>
    </>
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
    <form onSubmit={onSubmit} aria-labelledby="create-group-heading">
      <h2 id="create-group-heading">Add a group</h2>
      <p>
        <label htmlFor="new-group-name">Group name</label>
        <input
          id="new-group-name"
          required
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </p>
      {createGroup.isError ? (
        <p role="alert">{problemDetail(createGroup.error) ?? 'Could not add the group. Try again.'}</p>
      ) : null}
      <p>
        <button type="submit" disabled={createGroup.isPending}>
          Add group
        </button>
      </p>
    </form>
  )
}
