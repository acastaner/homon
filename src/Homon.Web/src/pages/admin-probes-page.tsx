import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'

import { problemDetail } from '@/lib/api'
import { type ProbeGroup, useProbeGroups } from '@/lib/probe-groups'
import {
  useCreateProbe,
  useDeleteProbe,
  useProbes,
  useReorderProbes,
  useSetProbePaused,
  useUpdateProbe,
  type Probe,
} from '@/lib/probes'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/**
 * The probe admin page: an ordered list of existing probes with move/edit/pause/delete
 * actions, and a form beneath it that both adds a new probe and edits whichever one is
 * currently selected. Unstyled — no `className` anywhere; plan 012 owns the look.
 */
export function AdminProbesPage() {
  useDocumentTitle(pageTitle('Probes', 'Admin'))

  const probes = useProbes()
  const groups = useProbeGroups()
  const reorder = useReorderProbes()
  const setPaused = useSetProbePaused()
  const deleteProbe = useDeleteProbe()

  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)

  const orderedProbes = probes.data ?? []
  const editingProbe = orderedProbes.find((probe) => probe.id === editingId) ?? null

  function move(id: string, direction: -1 | 1) {
    const ids = orderedProbes.map((probe) => probe.id)
    const index = ids.indexOf(id)
    const swapWith = index + direction

    if (swapWith < 0 || swapWith >= ids.length) {
      return
    }

    const next = [...ids]
    ;[next[index], next[swapWith]] = [next[swapWith], next[index]]
    reorder.mutate(next)
  }

  return (
    <>
      <h1>Probes</h1>
      {orderedProbes.length === 0 ? (
        <p>No probes yet. Add one below.</p>
      ) : (
        <ol aria-label="Probes">
          {orderedProbes.map((probe, index) => (
            <li key={probe.id}>
              <p>
                <strong>{probe.name}</strong> — {probe.status}
                {probe.lastDetail ? ` (${probe.lastDetail})` : null}
              </p>
              <p>
                <button type="button" onClick={() => move(probe.id, -1)} disabled={index === 0}>
                  Move {probe.name} up
                </button>{' '}
                <button
                  type="button"
                  onClick={() => move(probe.id, 1)}
                  disabled={index === orderedProbes.length - 1}
                >
                  Move {probe.name} down
                </button>{' '}
                <button type="button" onClick={() => setEditingId(probe.id)}>
                  Edit {probe.name}
                </button>{' '}
                <button
                  type="button"
                  onClick={() => setPaused.mutate({ id: probe.id, isPaused: !probe.isPaused })}
                  disabled={setPaused.isPending}
                >
                  {probe.isPaused ? `Unpause ${probe.name}` : `Pause ${probe.name}`}
                </button>{' '}
                {confirmingDeleteId === probe.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() =>
                        deleteProbe.mutate(probe.id, { onSuccess: () => setConfirmingDeleteId(null) })
                      }
                    >
                      Confirm delete {probe.name}
                    </button>{' '}
                    <button type="button" onClick={() => setConfirmingDeleteId(null)}>
                      Cancel delete {probe.name}
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => setConfirmingDeleteId(probe.id)}>
                    Delete {probe.name}
                  </button>
                )}
              </p>
            </li>
          ))}
        </ol>
      )}
      <ProbeForm
        key={editingId ?? 'new-probe'}
        probe={editingProbe}
        groups={groups.data ?? []}
        onDoneEditing={() => setEditingId(null)}
      />
    </>
  )
}

function ProbeForm({
  probe,
  groups,
  onDoneEditing,
}: {
  probe: Probe | null
  groups: ProbeGroup[]
  onDoneEditing: () => void
}) {
  const createProbe = useCreateProbe()
  const updateProbe = useUpdateProbe()
  const isEditing = probe !== null

  const [name, setName] = useState(probe?.name ?? '')
  const [host, setHost] = useState(probe?.host ?? '')
  const [pollIntervalSeconds, setPollIntervalSeconds] = useState(probe?.pollIntervalSeconds ?? 60)
  const [failureThreshold, setFailureThreshold] = useState(probe?.failureThreshold ?? 2)
  const [groupIds, setGroupIds] = useState<string[]>(probe?.groupIds ?? [])

  const mutation = isEditing ? updateProbe : createProbe

  function toggleGroup(id: string) {
    setGroupIds((current) => (current.includes(id) ? current.filter((g) => g !== id) : [...current, id]))
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const trimmedHost = host.trim()

    if (isEditing && probe) {
      updateProbe.mutate(
        { id: probe.id, name: trimmedName, host: trimmedHost, pollIntervalSeconds, failureThreshold, groupIds },
        { onSuccess: onDoneEditing },
      )
      return
    }

    createProbe.mutate(
      { name: trimmedName, host: trimmedHost, kind: 'ping', pollIntervalSeconds, failureThreshold, groupIds },
      {
        onSuccess: () => {
          setName('')
          setHost('')
          setPollIntervalSeconds(60)
          setFailureThreshold(2)
          setGroupIds([])
        },
      },
    )
  }

  return (
    <form onSubmit={onSubmit} aria-labelledby="probe-form-heading">
      <h2 id="probe-form-heading">{isEditing ? `Edit ${probe.name}` : 'Add a probe'}</h2>
      <p>
        <label htmlFor="probe-name">Name</label>
        <input
          id="probe-name"
          name="name"
          required
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </p>
      <p>
        <label htmlFor="probe-host">Host</label>
        <input
          id="probe-host"
          name="host"
          required
          value={host}
          onChange={(event) => setHost(event.target.value)}
        />
      </p>
      <p>
        <label htmlFor="probe-kind">Kind</label>
        {/* One option today — 003 adds "http" beside it. A real, bound select rather than a
            hidden/fixed field, so that addition is a new <option>, not new structure. */}
        <select id="probe-kind" name="kind" value="ping" onChange={() => {}}>
          <option value="ping">Ping (ICMP)</option>
        </select>
      </p>
      <p>
        <label htmlFor="probe-poll-interval">Poll interval (seconds)</label>
        <input
          id="probe-poll-interval"
          name="pollIntervalSeconds"
          type="number"
          required
          value={pollIntervalSeconds}
          onChange={(event) => setPollIntervalSeconds(Number(event.target.value))}
        />
      </p>
      <p>
        <label htmlFor="probe-failure-threshold">Failure threshold</label>
        <input
          id="probe-failure-threshold"
          name="failureThreshold"
          type="number"
          required
          value={failureThreshold}
          onChange={(event) => setFailureThreshold(Number(event.target.value))}
        />
      </p>
      <fieldset>
        <legend>Groups</legend>
        {groups.length === 0 ? (
          <p>
            No groups yet. <Link to="/admin/probe-groups">Add one</Link>.
          </p>
        ) : (
          groups.map((group) => (
            <p key={group.id}>
              <label>
                <input
                  type="checkbox"
                  checked={groupIds.includes(group.id)}
                  onChange={() => toggleGroup(group.id)}
                />{' '}
                {group.name}
              </label>
            </p>
          ))
        )}
      </fieldset>
      {mutation.isError ? (
        <p role="alert">{problemDetail(mutation.error) ?? 'Could not save the probe. Try again.'}</p>
      ) : null}
      <p>
        <button type="submit" disabled={mutation.isPending}>
          {isEditing ? 'Save' : 'Add probe'}
        </button>{' '}
        {isEditing ? (
          <button type="button" onClick={onDoneEditing}>
            Cancel
          </button>
        ) : null}
      </p>
    </form>
  )
}
