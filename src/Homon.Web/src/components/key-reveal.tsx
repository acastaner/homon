import { BUTTON_SECONDARY, FIELD_INPUT, FIELD_LABEL } from '@/components/admin-classes'

/**
 * The one-time display of a freshly minted API key, shared by the Reporters page (registering or
 * replacing a reporter's key) and the API keys page (minting a script key). A `role="alert"` so a
 * screen reader announces it when it appears, which matters because it is the only time the token
 * exists in the clear: the caller keeps it in component state, never in the query cache, so a
 * refetch cannot resurface a secret the administrator was told they would see once.
 *
 * `inputId` is passed in rather than fixed because the two pages already carry different ids
 * (`revealed-key`, `revealed-api-key`) that their tests and e2e specs reach for.
 */
export function KeyReveal({
  inputId,
  name,
  token,
  onDone,
}: {
  inputId: string
  name: string
  token: string
  onDone: () => void
}) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-2 rounded-md border border-unstable/40 bg-unstable-bg px-3 py-2.5 text-[14px] text-text"
    >
      <p className="font-semibold">This key will not be shown again. Store it now.</p>
      <p className="flex flex-col gap-1">
        <label htmlFor={inputId} className={FIELD_LABEL}>
          API key for {name}
        </label>
        <input id={inputId} readOnly value={token} className={`${FIELD_INPUT} mono`} />
      </p>
      <p className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => void navigator.clipboard?.writeText(token)}
          className={BUTTON_SECONDARY}
        >
          Copy key
        </button>
        <button type="button" onClick={onDone} className={BUTTON_SECONDARY}>
          Done
        </button>
      </p>
    </div>
  )
}
