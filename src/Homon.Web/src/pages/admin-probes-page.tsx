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
  type HttpProbeOptionsInput,
  type Probe,
} from '@/lib/probes'
import type { ProbeKind } from '@/lib/status'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/** Every kind the create form offers today — `smb`/`snmp` arrive with plans 004/005. */
const CREATABLE_KINDS: readonly ('ping' | 'http')[] = ['ping', 'http']

const KIND_LABELS: Record<ProbeKind, string> = {
  ping: 'Ping (ICMP)',
  http: 'HTTP/HTTPS',
  smb: 'SMB/CIFS',
  snmp: 'SNMP',
}

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

  // Kind is only ever chosen on the create form — a probe's kind cannot change after
  // creation (ProbeEndpoints ignores it on PUT), so the edit form renders it as text below
  // instead of this state. Unused while editing.
  const [kind, setKind] = useState<'ping' | 'http'>(probe?.kind === 'http' ? 'http' : 'ping')

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
      {isEditing ? (
        <p>Kind: {KIND_LABELS[probe.kind]} — cannot be changed after creation.</p>
      ) : (
        <p>
          <label htmlFor="probe-kind">Kind</label>
          <select
            id="probe-kind"
            name="kind"
            value={kind}
            onChange={(event) => setKind(event.target.value as 'ping' | 'http')}
          >
            {CREATABLE_KINDS.map((option) => (
              <option key={option} value={option}>
                {KIND_LABELS[option]}
              </option>
            ))}
          </select>
        </p>
      )}
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
      {isHttp ? (
        // 004 (SMB) and 005 (SNMP) add a sibling fieldset here, keyed the same way, rather
        // than restructuring this block — see plan 003's Step 6 and its Maintenance notes.
        <fieldset id="probe-http-fieldset">
          <legend>HTTP</legend>
          <p>
            <label htmlFor="probe-http-method">Method</label>
            <select
              id="probe-http-method"
              value={method}
              onChange={(event) => setMethod(event.target.value as 'head' | 'get')}
            >
              <option value="get">GET</option>
              <option value="head">HEAD</option>
            </select>
          </p>
          <p>
            <label htmlFor="probe-http-path">Path</label>
            <input
              id="probe-http-path"
              required
              value={path}
              onChange={(event) => setPath(event.target.value)}
            />
          </p>
          <p>
            <label htmlFor="probe-http-timeout">Timeout (seconds)</label>
            <input
              id="probe-http-timeout"
              type="number"
              min={1}
              max={25}
              required
              value={timeoutSeconds}
              onChange={(event) => setTimeoutSeconds(Number(event.target.value))}
            />
          </p>
          <p>
            <label>
              <input
                type="checkbox"
                checked={useHttps}
                onChange={(event) => setUseHttps(event.target.checked)}
              />{' '}
              Use HTTPS
            </label>
          </p>
          <p>
            <label>
              <input
                type="checkbox"
                checked={ignoreCertificateErrors}
                onChange={(event) => setIgnoreCertificateErrors(event.target.checked)}
              />{' '}
              Skip certificate validation (self-signed certificates)
            </label>
          </p>
          <p>
            <label htmlFor="probe-http-expected-status">Expected status code</label>
            <input
              id="probe-http-expected-status"
              type="number"
              value={expectedStatusCode}
              onChange={(event) => setExpectedStatusCode(event.target.value)}
            />
          </p>
          <p>
            <label>
              <input
                type="checkbox"
                checked={expectedStatusCodeNegate}
                onChange={(event) => setExpectedStatusCodeNegate(event.target.checked)}
              />{' '}
              Treat that status code as unexpected instead
            </label>
          </p>
          {method === 'get' ? (
            <>
              <p>
                <label htmlFor="probe-http-expected-body">Expected body text</label>
                <input
                  id="probe-http-expected-body"
                  value={expectedBodyText}
                  onChange={(event) => setExpectedBodyText(event.target.value)}
                />
              </p>
              <p>
                <label>
                  <input
                    type="checkbox"
                    checked={expectedBodyTextNegate}
                    onChange={(event) => setExpectedBodyTextNegate(event.target.checked)}
                  />{' '}
                  Treat that body text as unexpected instead
                </label>
              </p>
            </>
          ) : null}
          <fieldset>
            <legend>Credential</legend>
            <p>
              <label htmlFor="probe-http-credential-type">Credential type</label>
              <select
                id="probe-http-credential-type"
                value={credentialType}
                onChange={(event) => setCredentialType(event.target.value as 'none' | 'bearer' | 'basic')}
              >
                <option value="none">None</option>
                <option value="bearer">Bearer token</option>
                <option value="basic">Basic auth</option>
              </select>
            </p>
            {credentialType === 'basic' ? (
              <p>
                <label htmlFor="probe-http-credential-username">Username</label>
                <input
                  id="probe-http-credential-username"
                  required
                  value={credentialUsername}
                  onChange={(event) => setCredentialUsername(event.target.value)}
                />
              </p>
            ) : null}
            {credentialType !== 'none' ? (
              showSecretInput ? (
                <p>
                  <label htmlFor="probe-http-credential-secret">
                    {credentialType === 'bearer' ? 'Bearer token' : 'Password'}
                  </label>
                  <input
                    id="probe-http-credential-secret"
                    type="password"
                    value={secret}
                    onChange={(event) => setSecret(event.target.value)}
                  />
                  {hasStoredSecret ? (
                    <>
                      {' '}
                      <button
                        type="button"
                        onClick={() => {
                          setReplaceCredential(false)
                          setSecret('')
                        }}
                      >
                        Keep the current secret
                      </button>
                    </>
                  ) : null}
                </p>
              ) : (
                <p>
                  A secret is already set.{' '}
                  <button type="button" onClick={() => setReplaceCredential(true)}>
                    Replace credential
                  </button>
                </p>
              )
            ) : null}
          </fieldset>
        </fieldset>
      ) : null}
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
