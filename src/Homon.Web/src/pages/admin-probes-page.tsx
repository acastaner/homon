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

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const FIELDSET = 'flex flex-col gap-4 rounded-md border border-line p-4'
const LEGEND = 'px-1 text-[12px] font-semibold uppercase tracking-[0.1em] text-muted'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'

/**
 * The probe admin page: an ordered list of existing probes with move/edit/pause/delete
 * actions, and a form beneath it that both adds a new probe and edits whichever one is
 * currently selected.
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
      <h1 className={PAGE_H1}>Probes</h1>
      {orderedProbes.length === 0 ? (
        <p className="rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted">
          No probes yet. Add one below.
        </p>
      ) : (
        <ol aria-label="Probes" className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4">
          {orderedProbes.map((probe, index) => (
            <li key={probe.id} className="flex flex-col gap-2 py-3">
              <p className="text-[15px]">
                <strong className="font-semibold">{probe.name}</strong>{' '}
                <span className="text-muted">
                  — {probe.status}
                  {probe.lastDetail ? ` (${probe.lastDetail})` : null}
                </span>
              </p>
              <p className="flex flex-wrap gap-2">
                <button type="button" onClick={() => move(probe.id, -1)} disabled={index === 0} className={BUTTON_SECONDARY}>
                  Move {probe.name} up
                </button>
                <button
                  type="button"
                  onClick={() => move(probe.id, 1)}
                  disabled={index === orderedProbes.length - 1}
                  className={BUTTON_SECONDARY}
                >
                  Move {probe.name} down
                </button>
                <button type="button" onClick={() => setEditingId(probe.id)} className={BUTTON_SECONDARY}>
                  Edit {probe.name}
                </button>
                <button
                  type="button"
                  onClick={() => setPaused.mutate({ id: probe.id, isPaused: !probe.isPaused })}
                  disabled={setPaused.isPending}
                  className={BUTTON_SECONDARY}
                >
                  {probe.isPaused ? `Unpause ${probe.name}` : `Pause ${probe.name}`}
                </button>
                {confirmingDeleteId === probe.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() =>
                        deleteProbe.mutate(probe.id, { onSuccess: () => setConfirmingDeleteId(null) })
                      }
                      className="inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20"
                    >
                      Confirm delete {probe.name}
                    </button>
                    <button type="button" onClick={() => setConfirmingDeleteId(null)} className={BUTTON_SECONDARY}>
                      Cancel delete {probe.name}
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => setConfirmingDeleteId(probe.id)} className={BUTTON_SECONDARY}>
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
    <form
      onSubmit={onSubmit}
      aria-labelledby="probe-form-heading"
      className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5"
    >
      <h2 id="probe-form-heading" className="text-[15px] font-semibold">
        {isEditing ? `Edit ${probe.name}` : 'Add a probe'}
      </h2>
      <p className="flex flex-col gap-1">
        <label htmlFor="probe-name" className={FIELD_LABEL}>
          Name
        </label>
        <input
          id="probe-name"
          name="name"
          required
          value={name}
          onChange={(event) => setName(event.target.value)}
          className={FIELD_INPUT}
        />
      </p>
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
      {isEditing ? (
        <p className="text-[14px] text-muted">Kind: {KIND_LABELS[probe.kind]} — cannot be changed after creation.</p>
      ) : (
        <p className="flex flex-col gap-1">
          <label htmlFor="probe-kind" className={FIELD_LABEL}>
            Kind
          </label>
          <select
            id="probe-kind"
            name="kind"
            value={kind}
            onChange={(event) => setKind(event.target.value as 'ping' | 'http')}
            className={FIELD_INPUT}
          >
            {CREATABLE_KINDS.map((option) => (
              <option key={option} value={option}>
                {KIND_LABELS[option]}
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
          required
          value={failureThreshold}
          onChange={(event) => setFailureThreshold(Number(event.target.value))}
          className={FIELD_INPUT}
        />
      </p>
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
          groups.map((group) => (
            <p key={group.id}>
              <label className="flex items-center gap-2 text-[14px] text-text">
                <input
                  type="checkbox"
                  checked={groupIds.includes(group.id)}
                  onChange={() => toggleGroup(group.id)}
                  className="size-4 rounded border-line"
                />
                {group.name}
              </label>
            </p>
          ))
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
