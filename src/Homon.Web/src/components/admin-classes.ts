/*
 * The class strings every admin page shares. They were copied, byte for byte, into each page
 * until plan 025; a later style change had to be made eight times and one of them was always
 * missed. They are plain strings rather than components because most of them land on elements
 * that differ page to page (a `<Link>` styled as a button, a `<label>` around a checkbox), and a
 * wrapper component per shape would have to forward every prop to stay usable.
 *
 * Hold new admin surfaces to these (plan 025's maintenance notes): a page that declares its own
 * `BUTTON_PRIMARY` is a page that will drift.
 */

export const FIELD_LABEL = 'text-[13px] font-medium text-text'
export const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
export const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center gap-2 rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
export const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center gap-2 rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
export const BUTTON_DANGER =
  'inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20'
export const FIELDSET = 'flex flex-col gap-4 rounded-md border border-line p-4'
export const LEGEND = 'px-1 text-[12px] font-semibold uppercase tracking-[0.1em] text-muted'
export const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'

/*
 * Row actions are icon-only, 40px square — the floor `e2e/layout.spec.ts`'s "admin row-action
 * buttons are tappable" holds every admin button to, which is why they are not the 32px the
 * design canvas drew. Quiet at rest (no border) so five of them per row read as one control
 * strip rather than five buttons; the row's name is in every aria-label, so a screen reader
 * (and every test that queries "Move NAS up") still hears which row a button acts on.
 *
 * `aria-pressed` gets the same treatment as `aria-expanded`: the page editor's toolbar toggles
 * (Bold, Italic…) are pressed rather than expanded, and one rule keeps the two looking alike.
 */
export const ICON_BUTTON =
  'inline-flex size-10 shrink-0 items-center justify-center rounded-md border border-transparent text-muted hover:border-line-strong hover:bg-bg hover:text-text aria-expanded:border-line-strong aria-expanded:bg-bg aria-expanded:text-text aria-pressed:border-line-strong aria-pressed:bg-bg aria-pressed:text-text disabled:pointer-events-none disabled:opacity-30'
export const ICON_BUTTON_DANGER = `${ICON_BUTTON} hover:border-down hover:bg-down-bg hover:text-down aria-expanded:border-down aria-expanded:bg-down-bg aria-expanded:text-down`

/** The "nothing here yet" line: dashed so it reads as a gap, not as a row. */
export const EMPTY_STATE =
  'rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted'

/** The table: a bordered surface holding a column head and an `<ol>` of rows. */
export const PANEL = 'rounded-md border border-line bg-surface'

/*
 * The column head. `aria-hidden` on the element that carries it, because the `<ol>` below is the
 * list a screen reader walks; this is only for sighted columns. Used together with the page's own
 * row grid, since each page has different columns. Hidden below `lg`, where rows fold instead of
 * lining up.
 */
export const COLUMN_HEAD =
  'hidden px-4 py-2 text-[11.5px] font-semibold uppercase tracking-[0.08em] text-muted lg:grid'

/** One `<li>`: a hairline between rows, none above the first. */
export const ROW = 'border-t border-line first:border-t-0'

/** The padding of the row's grid `<div>`, folded below `lg` (more air) and tight at `lg`. */
export const ROW_CELLS = 'px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1'

/** An editor opened inside its row; `lg:pl-[58px]` lines it up under the name column. */
export const INLINE_FORM =
  'flex flex-col gap-4 border-t border-line bg-bg px-3.5 pt-4 pb-5 lg:pr-4 lg:pl-[58px]'

/** The add form at the bottom of a page. */
export const CARD_FORM = 'flex flex-col gap-4 rounded-md border border-line bg-surface p-5'

/** Row backgrounds by status: `index.css` defines the three classes (the paused one is hatched). */
export const ROW_TINT: Record<string, string> = {
  down: 'row-tint-down',
  unstable: 'row-tint-unstable',
  paused: 'row-paused',
}
