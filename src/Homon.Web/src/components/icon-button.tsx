import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { ChevronDown, ChevronUp, type LucideIcon } from 'lucide-react'

import { ICON_BUTTON, ICON_BUTTON_DANGER } from '@/components/admin-classes'

type IconButtonProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'title' | 'className' | 'type'> & {
  /** Either a Lucide icon... */
  icon?: LucideIcon
  /** ...or children, for the page editor's "H2" text buttons. */
  children?: ReactNode
  /** The accessible name. It carries the row's name ("Move NAS up"), so tests and screen readers know what it acts on. */
  label: string
  /** The hover text; defaults to `label`. Rows pass the short form ("Move up") because the row is right there. */
  title?: string
  tone?: 'default' | 'danger'
  iconSize?: number
}

/**
 * A 40px icon-only button whose accessible name is `label`. Passes `onClick`, `disabled`,
 * `aria-expanded`, `aria-controls` and `aria-pressed` straight through. Always `type="button"`:
 * the page editor wraps its toolbar in the page's `<form>`, where the default would submit it.
 */
export function IconButton({
  icon: Icon,
  label,
  title,
  tone = 'default',
  iconSize = 16,
  children,
  ...rest
}: IconButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={title ?? label}
      className={tone === 'danger' ? ICON_BUTTON_DANGER : ICON_BUTTON}
      {...rest}
    >
      {Icon ? <Icon aria-hidden="true" size={iconSize} strokeWidth={2} /> : children}
    </button>
  )
}

/**
 * The up and down pair, then the divider that separates them from the row's other actions.
 * `labelSuffix` is for rows that sit inside another row's editor (" in Storage"), where "Move NAS
 * up" alone would be ambiguous with the same probe's row on the page behind it.
 */
export function MoveButtons({
  name,
  isFirst,
  isLast,
  onMove,
  labelSuffix = '',
}: {
  name: string
  isFirst: boolean
  isLast: boolean
  onMove: (direction: -1 | 1) => void
  labelSuffix?: string
}) {
  return (
    <>
      <IconButton
        icon={ChevronUp}
        iconSize={18}
        label={`Move ${name} up${labelSuffix}`}
        title="Move up"
        disabled={isFirst}
        onClick={() => onMove(-1)}
      />
      <IconButton
        icon={ChevronDown}
        iconSize={18}
        label={`Move ${name} down${labelSuffix}`}
        title="Move down"
        disabled={isLast}
        onClick={() => onMove(1)}
      />
      <span aria-hidden="true" className="mx-1.5 h-5 w-px bg-line" />
    </>
  )
}
