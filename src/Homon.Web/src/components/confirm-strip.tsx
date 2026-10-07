import type { ReactNode } from 'react'

import { BUTTON_DANGER, BUTTON_SECONDARY } from '@/components/admin-classes'

/**
 * The strip under a row that asks before something is deleted, replaced or revoked (plan 024's
 * D5, kept by plan 025's D3). It is a strip rather than buttons swapped into the action cell
 * because that cell has a fixed width so the columns line up, and the question needs room to say
 * what else is lost. It stays inside the row's `<li>`, which is how a test scopes both buttons to
 * the row they belong to.
 *
 * The buttons read "Delete"/"Cancel" but are named by `confirmLabel` / `cancelLabel` ("Confirm
 * delete NAS"), so the name says which row they act on.
 *
 * `tone="neutral"` without `onConfirm` is the other use: an explanation with only a Close button,
 * for an action that cannot be taken (D6, a reporter a probe watches). Red would say "danger,
 * confirm me"; there is nothing to confirm.
 */
export function ConfirmStrip({
  id,
  question,
  confirmText = 'Delete',
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  isPending = false,
  tone = 'danger',
}: {
  id: string
  question: ReactNode
  confirmText?: string
  confirmLabel: string
  cancelLabel: string
  onConfirm?: () => void
  onCancel: () => void
  isPending?: boolean
  tone?: 'danger' | 'neutral'
}) {
  const isDanger = tone === 'danger'

  return (
    <div
      id={id}
      className={`flex flex-wrap items-center justify-end gap-2 border-t px-4 py-2.5 ${
        isDanger ? 'border-down/40 bg-down-bg' : 'border-line bg-bg'
      }`}
    >
      <p className={`mr-auto text-[13.5px] ${isDanger ? 'font-medium text-down' : 'text-text'}`}>{question}</p>
      {onConfirm ? (
        <>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isPending}
            aria-label={confirmLabel}
            className={BUTTON_DANGER}
          >
            {confirmText}
          </button>
          <button type="button" onClick={onCancel} aria-label={cancelLabel} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        </>
      ) : (
        <button type="button" onClick={onCancel} aria-label={cancelLabel} className={BUTTON_SECONDARY}>
          Close
        </button>
      )}
    </div>
  )
}
