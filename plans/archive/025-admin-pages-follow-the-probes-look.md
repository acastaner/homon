# Plan 025: Every admin page follows the Probes page's look

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If a STOP condition occurs, stop and report; do
> not improvise. When done, set this plan's row in `plans/README.md` to `IN PROGRESS — awaiting manual review` and leave the
> branch checked out. The maintainer reviews by running the app in this checkout, then merges.
>
> **Drift check (run first)**: `git diff --stat fd3f598..HEAD -- src/Homon.Web/src src/Homon.Web/e2e docs/ARCHITECTURE.md`
> If any in-scope file changed, compare it against the "Current state" excerpts before going on;
> a mismatch is a STOP condition.

## Status

- **Priority**: P2
- **Effort**: L. Eight pages, one refactor, roughly 25 files.
- **Risk**: MED. It is all front end, but it changes markup that about 40 existing assertions query.
- **Depends on**: none. It builds on 012 (tokens), 021 (reporters, keys) and 024 (the Probes page).
- **Category**: direction (design consistency) + tech-debt (the class constants copied into 8 files)
- **Planned at**: commit `fd3f598`, 2026-10-07
- **Outcome**: DONE — merged to `main` as `1202c67` (2026-10-07) after an independent review; `web` 239, `e2e` 133

## Why this matters

Plan 024 rebuilt `/admin/probes` with the following:

- a header with a count and a primary action;
- uppercase section headings over table panels;
- 40px icon row actions;
- a delete confirmation in a strip under the row;
- an editor that opens inside the row;
- the add form as a card at the bottom.

The other seven admin pages still use the pre-024 pattern: flat `<ol>` lists where every action is
a text button ("Move NAS up", "Edit NAS", "Confirm delete NAS"), raw ISO timestamps such as
`2026-10-01T04:30:00+00:00`, and one shared form at the bottom that both adds and edits. The admin
side therefore looks like two applications. Each page also re-declares the same eight Tailwind class
constants (`PAGE_H1`, `FIELD_INPUT`, `BUTTON_PRIMARY`…), so any later style change has to be made
eight times.

After this plan, every admin page uses one set of shared primitives, extracted from the Probes page
and with Probes itself moved onto them. Each page follows the target design on the Claude Design
canvas "Homon Admin pages", <https://claude.ai/artifact/8Rw21YgG8M2exz7wtSPv9T> (open it; every page
has an artboard, plus Links and Reporters at 412px).

## Decisions (made in planning; do not reopen them)

Where the canvas and this list disagree, **this list wins**. The canvas was drawn before the test
constraints were surveyed.

- **D1: Shared primitives, extracted from `admin-probes-page.tsx`, not copied.** They are listed in
  Step 1. `admin-probes-page.tsx` is refactored onto them, and its 22 unit tests must pass
  **unchanged**: that is the proof the extraction is faithful.
- **D2: Icon buttons are 40px (`size-10`), not the canvas's 32px.** `e2e/layout.spec.ts:41-45`
  measures every `<button>` on six admin pages against a 40px floor, disabled ones included. The
  same applies to confirm-strip buttons and to member-list buttons inside the group editor.
- **D3: Confirmations are a strip under the row, inside the same `<li>`.** This is 024's D5, not
  the canvas's in-cell swap. The strip has visible text "Delete" or "Cancel", with
  `aria-label="Confirm delete X"` / `"Cancel delete X"`. `e2e/reporters.spec.ts:136-137` scopes both
  buttons to the row's `listitem`.
- **D4: Accessible names that tests query are kept exactly.** The full list is under "Assertions
  that must keep passing". Icon buttons carry the old text as their `aria-label`. Where a test must
  change, this plan names the line and the replacement.
- **D5: Admin home summaries are computed in the browser from the existing list endpoints; there is
  no new API endpoint.** Every number is already in a response the admin pages fetch: `GET /probes`
  carries each probe's `status` (`'paused'` when paused), and `GET /probe-groups`, `/reporters`,
  `/links`, `/admin/pages`, `/weather/settings` and `/api-keys` carry the rest. The queries share
  cache keys with the section pages, so opening a section after the home page is instant. A
  `/admin/summary` endpoint would duplicate seven derivations in C# for one page. The derivations
  are pure functions in `src/lib/admin-summary.ts`, unit-tested. While a query is loading, its
  summary renders nothing. On error it renders nothing too: the summary is a convenience, and the
  link still works.
- **D6: A watched reporter's Delete is shown as unavailable, without a doomed request.** The button
  stays a real, enabled `<button>` named `Delete X`, so it remains reachable by keyboard and
  Playwright. It is styled muted with no danger hover, and its `title` is "A probe watches this
  reporter". Clicking it opens a **neutral** strip, not a red one, reading: `{probe} watches
  {reporter}, so it cannot be deleted. Delete that probe or point it at another reporter first.`
  `{probe}` is a link to `/probes/{id}`, and the strip has a "Close" button. It does **not** use
  `disabled` (a disabled button cannot say why) or `aria-disabled` (Playwright treats it as
  not-enabled and its `.click()` would time out). The page finds the watching probe from `useProbes()`:
  `kind === 'message' && host === reporter.identifier`, mirroring the server's check at
  `src/Homon.Api/Endpoints/ReporterEndpoints.cs:234-243`. The server's 400 refusal remains the
  backstop, and its `role="alert"` rendering stays.
- **D7: Timestamps use one helper, `formatStamp`, in the reader's locale and time zone.** It follows
  the existing `formatObservedAt` (`src/lib/probe-detail.ts:135-143`, which uses `toLocaleString(undefined, …)`).
  - It adds the year only when the year differs from now's.
  - `{ time: false }` gives a date only, used for created and expiry dates.
  - Every rendered stamp is `<time dateTime={iso} title={iso}>`.
  - Tests never assert its exact text; the existing comment on `formatObservedAt` says why.
- **D8: The page-editor toolbar becomes Lucide icons**, plus monospace "H2", "H3", "H4" text
  buttons. The 14 `aria-label`s stay byte-identical (`admin-page-editor-page.test.tsx:9-24` pins
  them). Each button gains a `title` equal to its label, and `aria-pressed` for the toggles
  (Bold, Italic, Strikethrough, Code, the three headings, both lists, Blockquote, Link). The pressed
  state is read with Tiptap 3's `useEditorState` (installed `@tiptap/react` ^3.31.3). Undo, Redo and
  Horizontal rule get no `aria-pressed`.
- **D9: Deleting a link asks first**, with the same strip as D3. No test clicks link delete today,
  so no test changes.
- **D10: "Remove location" on the weather page asks first too**, for consistency, which changes
  one unit test (Step 9 names it).
- **D11: Weather keeps its form always visible.** The canvas drew the form behind an Edit button.
  But `e2e/weather.spec.ts:56-59` fills Latitude on a fresh install, and `:74-76` reads the values
  back after a reload, so the form must be on screen at load in both states. The layout is:
  - a one-row "Location" table when a location is set, with only a `Remove location` icon action;
  - the form below it at all times;
  - the dashed empty-state line instead of the row when nothing is set.
- **D12: API keys keep Revoke on every key, reporter-paired keys included.** The canvas replaced it
  with a link, but `admin-api-keys-page.test.tsx:68-82` revokes a paired key, and a styling pass
  must not remove a capability. Paired keys show "Paired with the reporter X" under the token id,
  where X links to `/admin/reporters`.
  - The page splits into two sections. "Script keys" has `reporterName === null` and an `<ol>`
    with `aria-label="Script keys"`. "Reporter keys" has a reporter name and an `<ol>` with
    `aria-label="Reporter keys"`.
  - The State chip reads `Active`, `Expired` or `Revoked {date}`. A revoked key reads Revoked even
    when it has also expired. The revocation date sits inside the chip, so the page has exactly one
    element whose text matches `/Revoked/` (the unit test at `:57` uses a single-match `getByText`).
- **D13: `StatusChip` gains an optional `glyph?: LucideIcon` prop**, additive and defaulting to
  today's glyph. Non-probe states use it:
  - Published: `Eye`, `up` colour.
  - Draft: `FilePen`, `paused` colour.
  - Revoked: `Ban`, `paused` colour.
  - Expired: `Clock`, `unknown` colour.
  - A reporter's Overdue: `Clock`, `down` colour.
- **D14: The page editor keeps `h1 = "Edit {title}"`** (the canvas shows the bare title) because
  `e2e/pages.spec.ts:48` asserts it. The "Edit X" action on `/admin/pages` stays a **link**
  (`e2e/pages.spec.ts:70-72`).
- **D15: Labels keep their exact text.** Do not add "(optional)" to `Description` on Links or
  Reporters: `getByLabelText('Description')` is exact. `Place (optional)` already has it.
- **D16: Below `lg`, rows fold** the way Probes' do (024 D6): a first line with name and status, a
  second line of muted metadata, then the action strip. The canvas's `LinksPhone` and
  `ReportersPhone` artboards draw this.

## Current state

All paths are relative to `src/Homon.Web/` unless written from the repo root.

**Files and roles**
- `src/pages/admin-probes-page.tsx` (994 lines) is the reference look, and holds the primitives to
  extract (excerpts below).
- `src/pages/admin-home-page.tsx` is a plain `<nav aria-label="Admin sections">` list of 7 links.
- `src/pages/admin-probe-groups-page.tsx` holds `GroupCard` with text buttons "Move X up/down",
  "Rename X", "Delete X" / "Confirm delete X", members "Move P up/down", "Remove P", the select
  "Add probe to X", and the card `CreateGroupForm` ("Group name", "Add group").
- `src/pages/admin-links-page.tsx` has text buttons and one `LinkForm` at the bottom that both adds
  and edits ("Add a link" / "Edit link"). Delete fires immediately (`:111`).
- `src/pages/admin-pages-page.tsx`: each row reads "Title — Published/Draft", with a link
  "Edit X" and text buttons for delete and confirm. "New page" is a link at the bottom.
- `src/pages/admin-page-editor-page.tsx` is one card form with a toolbar of 14 text buttons
  (`:232-323`, each already `aria-label`led).
- `src/pages/admin-reporters-page.tsx`:
  - one row block per reporter, with text buttons for Messages, Edit, Replace key (+confirm) and
    Delete (+confirm), and history as a table;
  - a reveal `role="alert"`, plus one `ReporterForm` that both adds and edits;
  - `chipState`/`chipWord`/`isOverdue`/`describe` at the end of the file. `isOverdue` is page-private.
- `src/pages/admin-api-keys-page.tsx` is one `<ol aria-label="API keys">` with a text Revoke
  (+confirm), a reveal, and the "Mint a key" card ("Name", "Scope", "Expires", "Create key").
- `src/pages/admin-weather-page.tsx` is one card form (Latitude, Longitude, Place (optional),
  Units, "Save location") plus a separate "Remove location" button that deletes on one click.
- `src/components/status-chip.tsx` renders `StatusChip({ state, word? })` with glyphs `Check`,
  `TriangleAlert`, `X`, `CircleHelp`, `Pause`.
- `src/index.css:112-123` defines the classes `row-tint-unstable`, `row-tint-down` and
  `row-paused` (the hatched background).
- `src/lib/*` holds the hooks, all already present:
  - `useProbes` (`lib/probes.ts:111`, key `['probes']`, `Probe.status`, `Probe.isPaused`, `Probe.groupIds`);
  - `useProbeGroups` (`lib/probe-groups.ts:21`);
  - `useReporters` (`lib/reporters.ts:67`);
  - `useLinks` (`lib/links.ts:35`);
  - `useAdminPages` (`lib/pages.ts:61`, `AdminPageSummary { id, slug, title, isPublished, updatedAt }`);
  - `useWeatherSettings` (`lib/weather.ts:350`, `null` when unset);
  - `useDeleteWeatherSettings` (`lib/weather.ts:381`);
  - `useApiKeys` (`lib/api-keys.ts:40`).
- `src/test/fetch.ts`'s `stubFetch` **throws on any path a test did not declare**. A page that
  starts calling a new endpoint (Reporters calling `/api/v1/probes`) must have that path added to
  the stubs of its existing tests.

**The Probes page's primitives (to extract)**: `src/pages/admin-probes-page.tsx`

```tsx
// :34-45 — class constants (identical copies live in every other admin page)
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT = 'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY = 'inline-flex h-10 items-center justify-center gap-2 rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY = 'inline-flex h-10 items-center justify-center gap-2 rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const BUTTON_DANGER = 'inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'
// :54-56 — 40px icon buttons
const ICON_BUTTON = 'inline-flex size-10 shrink-0 items-center justify-center rounded-md border border-transparent text-muted hover:border-line-strong hover:bg-bg hover:text-text aria-expanded:border-line-strong aria-expanded:bg-bg aria-expanded:text-text disabled:pointer-events-none disabled:opacity-30'
const ICON_BUTTON_DANGER = `${ICON_BUTTON} hover:border-down hover:bg-down-bg hover:text-down aria-expanded:border-down aria-expanded:bg-down-bg aria-expanded:text-down`
```

The rest of the file supplies these shapes:

- **Page header** (`:120-142`): `<div className="flex flex-col gap-4 border-b border-line-strong pb-4 lg:flex-row lg:items-end lg:justify-between">`.
  - The left side holds `<h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">`
    and `<p className="max-w-[680px] text-[14px] text-muted">`.
  - The right side holds `<span className="mono text-[13px] text-muted">` (count), then the
    secondary action, then the primary action with `<Plus size={16} strokeWidth={2.25}/>`.
- **Section** (`:217-226`): `<section aria-labelledby={headingId} className="flex flex-col gap-2.5">`.
  Inside it, `<h2 className="text-[12px] font-semibold uppercase tracking-[0.12em] text-muted">`
  and a meta `<span className="text-[12.5px] text-muted">`.
- **Empty state** (`:228`): `rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted`.
- **Panel and column head** (`:233-245`): `rounded-md border border-line bg-surface`, with a header
  row `aria-hidden="true"` and classes `${ROW_GRID} hidden px-4 py-2 text-[11.5px] font-semibold uppercase tracking-[0.08em] text-muted lg:grid`.
- **Row** (`:316-317`): `<li className="border-t border-line first:border-t-0">`, then
  `<div className={`${ROW_GRID} px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1 ${isEditing ? 'bg-bg' : tint}`}>`.
- **Move buttons and divider** (`:352-372`): `ChevronUp`/`ChevronDown` at 18px with `aria-label`
  "Move X up/down" and `title`, then `<span aria-hidden="true" className="mx-1.5 h-5 w-px bg-line" />`.
- **Confirm strip** (`:416-438`): `<div id={confirmId} className="flex flex-wrap items-center justify-end gap-2 border-t border-down/40 bg-down-bg px-4 py-2.5">`.
  It holds `<p className="mr-auto text-[13.5px] font-medium text-down">`, then BUTTON_DANGER
  "Delete" with `aria-label="Confirm delete X"`, then BUTTON_SECONDARY "Cancel" with
  `aria-label="Cancel delete X"`.
- **Inline editor form classes** (`:621-625`): inline is
  `flex flex-col gap-4 border-t border-line bg-bg px-3.5 pt-4 pb-5 lg:pr-4 lg:pl-[58px]`; the card
  is `flex flex-col gap-4 rounded-md border border-line bg-surface p-5`.
- **"New X" jump** (`:180-184`): `jumpToAddForm()` scrolls to and focuses the add form's first input.
- **Add card hidden while a row edits** (`:169-174`): the inputs share fixed ids, so only one form
  is ever rendered.

**Conventions** (from `CLAUDE.md`):

- kebab-case files, named exports (`App.tsx` is the only default), the `@/` alias;
- comments carry the reasoning (match the density of the comments in `admin-probes-page.tsx`);
- no MSW: stub with `src/test/fetch.ts`, and render with `src/test/render.tsx`'s `renderWithProviders`;
- `npm ci`, never `npm install`;
- `docs/ARCHITECTURE.md`: grep the highest `### 3.N` and take the next. It is §3.30 at planning time.

## Assertions that must keep passing

Unless Step 9 or Step 11 names a change, these names must survive exactly.

- **`App.test.tsx:65-67` and `e2e/admin.spec.ts:13-20`:** `/admin` has h1 `Admin` and a
  `navigation` named `Admin sections`. That nav contains links whose accessible names are exactly
  `Probes`, `Probe groups`, `Links`, `Pages`, `Reporters` and `API keys` (put descriptions and
  summaries **outside** the `<a>`). Each destination's h1 has the same name.
- **Links unit test:** `Move NAS down`; labels `Title`, `URL`, `Description`; `Add link`.
- **Groups unit test:** `Move Storage up`; label `Group name`; `Add group`.
- **Editor unit test and `e2e/pages.spec.ts`:**
  - the 14 toolbar names (`Bold`, `Italic`, `Strikethrough`, `Code`, `Heading 2`, `Heading 3`,
    `Heading 4`, `Bullet list`, `Numbered list`, `Blockquote`, `Link`, `Horizontal rule`, `Undo`,
    `Redo`);
  - textbox `Body`, labels `Title`, `Slug` (with auto-follow), checkbox `Published`;
  - `Create page`, `Save changes`, h1 `Edit {title}`;
  - on `/admin/pages`, a `listitem` containing the **link** `Edit {title}`.
- **Reporters unit test and `e2e/reporters.spec.ts`:**
  - `getByText` exact on the reporter name and on the identifier (each alone in its own element),
    and on the status word (`Succeeded`);
  - text `/No probe watches this reporter/`, `/can never be overdue/`, `Watched by a probe`,
    `No report received yet` (inside the reporter's `listitem`);
  - labels `Name`, `Description`, `Message visibility`; `Add reporter`;
  - the `role="alert"` reveal "This key will not be shown again. Store it now.", the label
    `API key for X`, `Done`;
  - `Delete X` → `Confirm delete X`; `Messages from X` (button **and** table);
    `Replace the key for X` → `Confirm replace the key for X`.
- **API keys unit test:**
  - exact `getByText` on key names, and `Read and write` inside the scope list (see Step 8 for the
    list-name change);
  - `/Paired with the reporter X/`, `/Never used/`, `/Revoked/` (single match);
  - `Revoke X (tokenId)` disabled for a revoked key, enabled otherwise, then `Confirm revoke X (tokenId)`;
  - labels `Name`, `Create key`, `role="alert"`, `API key for X`.
- **Weather unit test and e2e:** labels `Latitude`, `Longitude`, `Place (optional)`, `Units`;
  `Save location`; `Remove location` present only when set (and see D10).
- **`e2e/layout.spec.ts`:** no horizontal overflow on every admin route at Pixel 7 and 1440×900,
  and every `<button>` at least 40px on the six list pages.

## Commands you will need

Run from the repo root unless noted. This machine runs the gate **one suite at a time**. A memory
kill or `Internal CLR error (0x80131506)` is environmental: retry once.

| Purpose | Command | Expected on success |
|---|---|---|
| Unit, lint, build | `./ci/run-ci.sh web` | ends `PASS — web`, exit 0 |
| One unit file | `cd src/Homon.Web && npx vitest run src/pages/admin-links-page.test.tsx` | all pass |
| Typecheck and build only | `cd src/Homon.Web && npm run build` | exit 0 |
| Lint only | `cd src/Homon.Web && npm run lint` | no new warnings vs. `main` (`oxlint` was never silent on this tree) |
| e2e (needs Docker) | `./ci/run-ci.sh e2e` | ends `PASS — e2e`, both viewport projects |
| API | not needed: no C# changes. If you change C#, STOP. | — |

Check the exit code, not the banner (`echo $?`). Do not pipe the gate into `tail`.

## Suggested executor toolkit

- `vercel-react-best-practices`, while writing `useEditorState` and the Admin home's seven queries.
- Open the canvas URL above, and keep each artboard beside you while you build its page.

## Scope

**In scope** (create or modify only these):
- `src/Homon.Web/src/components/admin-classes.ts` (create)
- `src/Homon.Web/src/components/admin-page-header.tsx` (create)
- `src/Homon.Web/src/components/admin-section.tsx` (create)
- `src/Homon.Web/src/components/icon-button.tsx` (create)
- `src/Homon.Web/src/components/confirm-strip.tsx` (create)
- `src/Homon.Web/src/components/key-reveal.tsx` (create)
- `src/Homon.Web/src/components/admin-primitives.test.tsx` (create)
- `src/Homon.Web/src/components/status-chip.tsx`, `status-chip.test.tsx`
- `src/Homon.Web/src/lib/format-stamp.ts`, `format-stamp.test.ts`, `src/Homon.Web/src/components/stamp.tsx` (create)
- `src/Homon.Web/src/lib/admin-summary.ts`, `admin-summary.test.ts` (create)
- `src/Homon.Web/src/lib/reporters.ts` (only to export `isOverdue` moved from the page)
- `src/Homon.Web/src/pages/admin-*-page.tsx`, all nine (Probes is refactor-only)
- `src/Homon.Web/src/pages/admin-*-page.test.tsx`, only the lines this plan names, plus new tests
- `src/Homon.Web/src/pages/admin-home-page.test.tsx` and `admin-pages-page.test.tsx` (create)
- `src/Homon.Web/src/App.test.tsx`, only if its `/admin` test needs the seven new stub paths
- `src/Homon.Web/e2e/reporters.spec.ts` (`:136-140` only) and `src/Homon.Web/e2e/contrast.spec.ts` (Step 11)
- `docs/ARCHITECTURE.md` (one new §3.N), `plans/README.md` (this row)

**Out of scope** (do NOT touch):
- Anything under `src/Homon.Api`, `src/Homon.Domain`, `src/Homon.Infrastructure` or `tests/`: no
  API change is needed (D5, D6).
- `src/components/app-shell.tsx`. Its `<main>` lacks a side gutter at 640–1240px (024's "Not
  done"), but that affects every page and is a separate change.
- `src/components/ui/*` (shadcn, unused). Do not adopt it in this plan.
- The probe form inside `admin-probes-page.tsx` beyond swapping in the shared class constants:
  its fields, ids and behaviour stay as they are.
- `e2e/helpers.ts`, `e2e/layout.spec.ts`, `e2e/admin.spec.ts`, `e2e/pages.spec.ts`,
  `e2e/weather.spec.ts`. They are the net and must stay byte-identical.

## Git workflow

- Branch: `plan/025-admin-pages-restyle`, cut from `main` in **this checkout**. Never create a
  worktree, never switch back to `main`, never merge, push, delete branches or `git stash`
  (`CLAUDE.md`, "Where the work happens").
- One commit per step, in the house style:
  `Web: the links admin page follows the probes look (plan 025)`, compare with
  `9adce5f Web: the probe admin page lists probes in the dashboard's sections and order (plan 024)`.
- End every commit message with the attribution lines your environment specifies.

## Steps

### Step 1: Extract the shared primitives and move Probes onto them

Create the following. Each file opens with a short comment explaining why it exists, in
`admin-probes-page.tsx`'s comment style.

1. **`components/admin-classes.ts`** exports the constants from the excerpt above under the same
   names: `FIELD_LABEL`, `FIELD_INPUT`, `BUTTON_PRIMARY`, `BUTTON_SECONDARY`, `BUTTON_DANGER`,
   `ICON_BUTTON`, `ICON_BUTTON_DANGER`, `ALERT`, `FIELDSET`, `LEGEND`. It also exports:
   - `EMPTY_STATE` (the dashed class);
   - `PANEL` (`rounded-md border border-line bg-surface`);
   - `COLUMN_HEAD` (`hidden px-4 py-2 text-[11.5px] font-semibold uppercase tracking-[0.08em] text-muted lg:grid`, used together with a page's own row grid);
   - `ROW` (`border-t border-line first:border-t-0`);
   - `ROW_CELLS` (`px-3.5 py-3 lg:min-h-12 lg:px-4 lg:py-1`);
   - `INLINE_FORM` and `CARD_FORM` (the two form classes above);
   - `ROW_TINT: Record<string, string>` (`{ down: 'row-tint-down', unstable: 'row-tint-unstable', paused: 'row-paused' }`).
2. **`components/admin-page-header.tsx`** exports
   `AdminPageHeader({ title, description, count, back, children })`.
   - `title` is the h1 text.
   - `description?: ReactNode`.
   - `count?: string` renders as the mono span.
   - `back?: { to: string; label: string }` renders a small muted `Link` with `ChevronLeft` above
     the h1 (used only by the page editor).
   - `children` holds the actions on the right.
   - The markup is exactly the Probes header (`:120-142`).
3. **`components/admin-section.tsx`** exports
   `AdminSection({ id, heading, meta, children })`, which renders the `<section aria-labelledby>`
   plus h2 plus meta row from `:217-226`. `id` builds `${id}-heading`.
4. **`components/icon-button.tsx`** exports two things.
   - `IconButton({ icon: Icon, label, title, tone = 'default', iconSize = 16, ...rest })`:
     - It renders `<button type="button" aria-label={label} title={title ?? label} className={tone === 'danger' ? ICON_BUTTON_DANGER : ICON_BUTTON} {...rest}>`
       around `<Icon aria-hidden="true" size={iconSize} strokeWidth={2} />`.
     - `rest` passes `onClick`, `disabled`, `aria-expanded`, `aria-controls` and `aria-pressed`.
     - It also accepts `children` instead of `icon`, for the "H2" text buttons.
   - `MoveButtons({ name, isFirst, isLast, onMove, labelSuffix = '' })`, which renders the up and
     down `IconButton`s (`ChevronUp`/`ChevronDown`, `iconSize={18}`), labelled
     `` `Move ${name} up${labelSuffix}` `` and `` `Move ${name} down${labelSuffix}` ``, followed by
     the divider span.
5. **`components/confirm-strip.tsx`** exports `ConfirmStrip(props)` with these props:
   - `id`, `question: ReactNode`, `confirmText`, `confirmLabel`, `cancelLabel`;
   - `onConfirm`, `onCancel`, `isPending`;
   - `tone: 'danger' | 'neutral' = 'danger'`.
   - The danger tone is the `:416-438` markup.
   - The neutral tone uses `border-line bg-bg` with `text-text`, and renders **only** a Close
     button (`BUTTON_SECONDARY`, text "Close", `aria-label={cancelLabel}`) when `onConfirm` is
     omitted. This is the shape D6 uses.
6. **`components/key-reveal.tsx`** exports `KeyReveal({ inputId, name, token, onDone })`.
   - It is a `role="alert"` box with the classes
     `flex flex-col gap-2 rounded-md border border-unstable/40 bg-unstable-bg px-3 py-2.5 text-[14px] text-text`.
   - Its heading text is exactly `This key will not be shown again. Store it now.`.
   - It has a label `API key for {name}` bound to `inputId`, and a read-only mono input.
   - It has a "Copy key" button that calls `navigator.clipboard?.writeText(token)`, and a "Done" button.
   - It is lifted verbatim from `admin-reporters-page.tsx`'s `REVEAL` block. Keep the input ids
     `revealed-key` (Reporters) and `revealed-api-key` (API keys) by passing `inputId`.

Then edit `admin-probes-page.tsx` to import every one of these in place of its local copies:

- delete its local constants;
- use `AdminPageHeader` for `:120-142`;
- use `AdminSection` for the section wrapper;
- use `MoveButtons` and `IconButton` for the row actions;
- use `ConfirmStrip` for the delete strip.

Change no text, id, label or order.

Write `components/admin-primitives.test.tsx` with these tests:

- `IconButton` exposes `label` as its accessible name and keeps `aria-expanded`.
- `MoveButtons` disables up on the first row and down on the last.
- `ConfirmStrip` (danger) shows the question and calls `onConfirm` from the button named by
  `confirmLabel`.
- `ConfirmStrip` (neutral, no `onConfirm`) renders a Close button and no confirm button.
- `KeyReveal` renders `role="alert"` and the label `API key for X`.

**Verify**:
- `cd src/Homon.Web && npx vitest run src/pages/admin-probes-page.test.tsx src/components/admin-primitives.test.tsx` → all pass. The probes file is **unmodified** (`git diff --stat -- src/Homon.Web/src/pages/admin-probes-page.test.tsx` is empty).
- `npm run build` → exit 0.
- `grep -c "const ICON_BUTTON\|const BUTTON_PRIMARY" src/pages/admin-probes-page.tsx` → `0`.

### Step 2: Shared helpers (`formatStamp`, `StatusChip` glyph, summaries)

1. **`lib/format-stamp.ts`** exports `formatStamp(iso: string, options?: { time?: boolean; now?: Date }): string`.
   - It calls `new Date(iso).toLocaleString(undefined, { day: 'numeric', month: 'short', ...(time ? { hour: '2-digit', minute: '2-digit' } : {}), ...(differentYear ? { year: 'numeric' } : {}) })`.
   - `time` defaults to `true`, and `now` to `new Date()`.
   - Add a doc comment that cross-references `formatObservedAt`.
   - Also create the component `Stamp({ iso, time })` in `components/stamp.tsx`, which renders `<time dateTime={iso} title={iso}>{formatStamp(iso, { time })}</time>`.
   - Tests in `format-stamp.test.ts` (the suite runs with `TZ=UTC`):
     - a same-year stamp does not contain the year;
     - a different-year stamp contains `2025`;
     - `{ time: false }` output does not contain `:`;
     - it never returns `'Invalid Date'` for `'2026-10-01T04:30:00+00:00'`.
     - Do not assert the exact text: the locale is the reader's.
2. **`components/status-chip.tsx`**: add `glyph?: LucideIcon` to the props, with
   `const Glyph = glyph ?? GLYPH[state]`. Extend the doc comment with D13's mapping. Add one test to
   `status-chip.test.tsx`: a passed glyph replaces the default (query the rendered `svg`'s
   `lucide-…` class, e.g. `lucide-eye`).
3. **`lib/reporters.ts`**: move `isOverdue(reporter, now = Date.now())` here from the page and
   export it. The page then imports it.
4. **`lib/admin-summary.ts`** contains pure functions with no hooks. Each returns
   `{ text: string; down?: number; unstable?: number }`, and the `text` strings are exactly these:
   - `summariseProbes(probes)` → `"{n} probes"` plus `" · {p} paused"` when p > 0, with `down`
     and `unstable` counts from `status`.
   - `summariseGroups(groups, probes)` → `"{g} groups · {u} probes in none"`. `u` counts probes
     with `groupIds.length === 0`. Drop the second part when u = 0.
   - `summariseReporters(reporters, now)` → `"{n} reporters"`, with `down` = the overdue count.
   - `summariseLinks(links)` → `"{n} links"`.
   - `summarisePages(pages)` → `"{n} pages"` plus `" · {d} draft(s)"`.
   - `summariseWeather(settings)` → `null` gives `"Not set"`; otherwise
     `"{lat.toFixed(2)}, {lon.toFixed(2)} · {units}"`, prefixed `"{place} · "` when `place` is set.
   - `summariseApiKeys(keys)` → `"{a} active · {r} revoked · {e} expired"`, with zero parts
     dropped. Revoked takes precedence over expired (D12).
   - Singulars use the `countOf` rule from `admin-probes-page.tsx:190-192`; move `countOf` into
     this file and export it.
   - Write `admin-summary.test.ts` with at least one test per function, covering a zero case and a
     plural/singular case.

**Verify**: `npx vitest run src/lib src/components` → all pass; `npm run build` → exit 0.

### Step 3: Links

Follow the canvas's `Links` artboard, plus D3, D9 and D16.

- **Header**: `AdminPageHeader` with title `Links`, description "The link list on the dashboard,
  in the order shown here.", count `countOf(n,'link')`, and a primary "New link" (`Plus`) that
  jumps to `#link-title`. Hide it while a row is editing.
- **Section**: one `AdminSection` with heading "On the dashboard" and meta "Each opens in a new tab".
- **Table**: a `PANEL` holding an `<ol aria-label="Links">`. Row grid at `lg`:
  `lg:grid-cols-[28px_minmax(0,0.9fr)_minmax(0,1.1fr)_minmax(0,1.2fr)_176px]`, with columns
  `# · Link · Address · Description · Actions`.
- **Row contents**: the title is an external `<a target="_blank" rel="noopener noreferrer">` with
  a 13px muted `ExternalLink` icon. The URL is mono and truncated with `title`. The description
  reads `—` when null.
- **Actions**: `MoveButtons`, Edit (`Pencil`, `aria-expanded`), Delete (danger, `Trash2`).
  Delete opens a `ConfirmStrip` with "Delete {title}?".
- **Editing**: the inline form uses `INLINE_FORM`, with Title, URL and Description in an auto-fit
  grid and Save/Cancel. While editing, the bottom card is not rendered.
- **Add card**: `CARD_FORM`, h2 "New link", the same three fields, "Add link", and the hint "Goes
  to the bottom of the list."
- Split `LinkForm` into `variant: 'card' | 'inline'` exactly as `ProbeForm` does
  (`admin-probes-page.tsx:451-470`).
- **Empty state**: `EMPTY_STATE` "No links yet. Add one below."

Add two tests to `admin-links-page.test.tsx`:
- (a) `Delete NAS` sends nothing; `Confirm delete NAS` then sends `DELETE /api/v1/links/link-1`.
- (b) `Edit NAS` opens `form` named `Edit NAS` inside the row; only one form exists; `Cancel`
  brings back `form` named `New link`.

**Verify**: `npx vitest run src/pages/admin-links-page.test.tsx` → all pass (2 existing + 2 new).

### Step 4: Probe groups

Follow the `Groups` artboard.

- **Header**:
  - title `Probe groups`;
  - description "The dashboard's sections, top to bottom. Deleting a group keeps its probes; any
    that are in no other group move to Other.";
  - count from `summariseGroups`;
  - a secondary link "Probes" → `/admin/probes`, and a primary "New group" jumping to `#new-group-name`.
- **Section "Groups"**: meta "The order here is the dashboard's". The `<ol aria-label="Probe groups">`
  uses the grid `lg:grid-cols-[28px_minmax(0,220px)_80px_minmax(0,1fr)_176px]`, with columns
  `# · Group · Probes (count, right-aligned mono) · In it, in order (member names joined " · ", truncated) · Actions`.
- **Actions**: `MoveButtons` (keeping `Move Storage up`), Edit (`aria-label="Edit {name}"`), and
  Delete. Delete opens a strip reading "Delete {name}? Its probes are kept." with `Confirm delete {name}`.
- **Edit opens inside the row** with three parts:
  - (1) a rename form with label `Name`, input id `group-name-{id}`, and "Save name";
  - (2) "Probes in {name}, in dashboard order", an `<ol aria-label="Probes in {name}">` where each
    member row shows `StatusChip` (state = `isPaused ? 'paused' : status`), name, mono
    kind · target (reuse `PROBE_KIND_SHORT` and `probeTarget` from `lib/probe-sections.ts`),
    `MoveButtons` with `labelSuffix=" in {group}"`, and a danger `IconButton` (`Minus`) named
    `Remove {probe} from {group}`;
  - (3) the existing select with label `Add probe to {name}` (keep its add-on-change behaviour)
    and the hint "Adding, removing and reordering probes saves at once."
  - Finish with a "Done" button.
- **Section "Other"**: meta "In no group · shown last on the dashboard". It is a `PANEL` of
  pill links to `/probes/{id}` for each probe with no group. Omit the section when there are none.
- **Add card**: "New group", label `Group name`, "Add group" (unchanged names).

Add tests:
- (a) `Edit Storage` shows `Probes in Storage` and `Remove … from Storage` sends `PUT …/members`
  without that id;
- (b) `Delete Storage` shows "Its probes are kept." and `Confirm delete Storage` sends DELETE.

Add `/api/v1/probes` fields as needed. The existing stub already serves probes; check that it
includes `status`, `isPaused` and `kind`, and extend the fixture if not.

**Verify**: `npx vitest run src/pages/admin-probe-groups-page.test.tsx` → all pass.

### Step 5: Pages list

Follow the `Pages` artboard and D13/D14.

- **Header**: title `Pages`, description "Free-form pages readers open from the dashboard. A
  draft is visible only here until it is published.", count from `summarisePages`, and a primary
  **link** "New page" → `/admin/pages/new`.
- **Sections**: two `AdminSection`s, "Published" (meta "Linked from the dashboard") and "Drafts"
  (meta "Only administrators see these"). Omit a section that would be empty. When both are empty,
  show `EMPTY_STATE` "No pages yet."
- **Grid**: `lg:grid-cols-[132px_minmax(0,1fr)_minmax(0,1fr)_160px_176px]`, with columns
  `State · Page · Address · Last saved · Actions`.
- **Row contents**:
  - State is a `StatusChip` with `up`/`Eye`/"Published" or `paused`/`FilePen`/"Draft".
  - Draft rows get `ROW_TINT.paused`.
  - Page is a `Link` to `/admin/pages/{id}`.
  - Address is mono `/pages/{slug}`.
  - Last saved is `<Stamp iso={updatedAt} />`.
- **Actions**:
  - for published pages, an external `<a>` styled as `ICON_BUTTON` with aria-label
    `Open {title} as readers see it` → `/pages/{slug}`;
  - a divider;
  - a `Link` styled as `ICON_BUTTON` with **aria-label `Edit {title}`** (it must stay a link);
  - Delete (danger) → strip "Delete {title}?" / `Confirm delete {title}`.
- Create `admin-pages-page.test.tsx` with these tests:
  - (a) drafts and published are in separate regions;
  - (b) `getByRole('link', { name: 'Edit X' })` has href `/admin/pages/{id}`;
  - (c) Delete → Confirm sends `DELETE /api/v1/pages/{id}`. Check the exact path in `lib/pages.ts`'s
    delete hook before asserting it.

**Verify**: `npx vitest run src/pages/admin-pages-page.test.tsx` → all pass.

### Step 6: Page editor

Follow the `PageEditor` artboard and D8/D14.

- Wrap the whole page in the existing `<form onSubmit>` so that header buttons can submit. Keep
  `aria-labelledby="page-form-heading"`, and move that id onto the h2 "Body".
- **Header**: `AdminPageHeader` with `back={{ to: '/admin/pages', label: 'Pages' }}` (this
  replaces the "Back to pages" link) and title `New page` or `Edit {title}`.
  - The description is `Published at /pages/{slug}` or `Draft` when editing, and nothing when new.
  - The actions are the "View page" external link (when editing a published page) and the submit
    button (`Create page` / `Save changes`, unchanged names).
- **Body**: `flex flex-wrap gap-6`.
  - The **main** column (`flex-[999_1_560px] min-w-0`) is an `AdminSection` "Body" holding a
    `PANEL`: the toolbar row (`role="toolbar" aria-label="Formatting"`, `flex flex-wrap gap-0.5 border-b border-line p-1.5`), then `<EditorContent>`.
  - Drop the editor's own border classes in `editorProps.attributes.class`, keeping `prose min-h-[420px] px-6 py-5 outline-none`.
  - The **aside** (`flex-[1_1_300px]`) is an `AdminSection` "Settings" holding a `PANEL p-4`:
    - Title, unchanged label;
    - Slug, rendered as an inline-flex box with a muted mono `/pages/` prefix before the input,
      label `Slug` unchanged, plus the hint "Lower-case letters, digits and hyphens. Changing it
      breaks old bookmarks.";
    - the `Published` checkbox with the hint "Readers can open it from the dashboard.";
    - when editing, a `<dl>` with Created and Last saved, using `Stamp`.
- **Toolbar**: `IconButton` for each action, in this order, with dividers between the groups:
  - `Bold`, `Italic`, `Strikethrough`, `Code`;
  - text buttons `H2`/`H3`/`H4` (`aria-label` `Heading 2/3/4`);
  - `List`, `ListOrdered`, `TextQuote` (labelled `Bullet list`, `Numbered list`, `Blockquote`);
  - `Link`, `Minus` (`Horizontal rule`);
  - a spacer, then `Undo2`, `Redo2`.
  - Pressed state: `const active = useEditorState({ editor, selector: ({ editor: e }) => ({ bold: e?.isActive('bold') ?? false, … }) })` → `aria-pressed={active.bold}` on toggles only.
  - Add `aria-pressed:border-line-strong aria-pressed:bg-bg aria-pressed:text-text` to
    `ICON_BUTTON` in `admin-classes.ts` (harmless elsewhere).
- Add one test to `admin-page-editor-page.test.tsx`: `Bold` has `aria-pressed="false"` before any
  typing. Leave the 14-name test as it is.

**Verify**: `npx vitest run src/pages/admin-page-editor-page.test.tsx` → all pass. STOP if
`useEditorState` is not exported by the installed `@tiptap/react`.

### Step 7: Reporters

Follow the `Reporters` artboard, D6, D7 and D13.

- **Header**: title `Reporters`, the existing description text, count from `summariseReporters`
  (e.g. `3 reporters · 1 overdue`), and a primary "New reporter" → `#reporter-name`.
- **Reveal**: `<KeyReveal inputId="revealed-key" …>` between the header and the section, with
  unchanged behaviour.
- **Section "Registered"**: meta "Status is each reporter's own last word".
  `<ol aria-label="Reporters">`, grid
  `lg:grid-cols-[124px_minmax(0,1.3fr)_128px_minmax(0,1fr)_56px_minmax(0,1fr)_minmax(176px,auto)]`,
  with columns `Status · Reporter · Last report · Next expected · Kept · Watched · Actions`.
- **Status cell**: `StatusChip` from the existing `chipState`/`chipWord`; Overdue uses `glyph={Clock}`.
  Tint the row `ROW_TINT[down]` when overdue or failed, and `ROW_TINT[unstable]` on warning.
- **Reporter cell**: name in `<span className="text-[15px] font-semibold">` and identifier in its
  own mono `<span>`, as two separate elements (exact `getByText`).
- **Last report**: `<Stamp>` or `No report received yet`.
- **Next expected**:
  - `<Stamp>` prefixed "by ", in `text-down` when overdue;
  - when there is a latest message but no `nextExpectedAt`: `Not declared — can never be overdue`;
  - when there is no message at all: `—`.
- **Kept**: mono count.
- **Watched**: a link reading exactly `Watched by a probe` to the watching probe's
  `/probes/{id}`, or muted `No probe watches this reporter`.
- **Revoked key**: when `keyRevokedAt` is set, add a line under the identifier: "Its key is
  revoked: replace it" (`text-down`).
- **Actions**, all `IconButton`:
  - `Messages from X` (`History`, `aria-expanded`) toggles the history drawer;
  - divider;
  - `Edit X`;
  - `Replace the key for X` (`KeyRound`) opens a `ConfirmStrip` "Replace the key for X? The old
    one stops working at once." with `confirmLabel="Confirm replace the key for X"`;
  - `Delete X` opens either the danger strip with `Confirm delete X` (not watched), or the
    **neutral** D6 strip (watched).
- **Watcher lookup**: call `useProbes()` and find the watcher with
  `probes.find((p) => p.kind === 'message' && p.host === reporter.identifier)`.
- **History drawer**: `INLINE_FORM`-coloured `div` (bg-bg). Keep the existing table, with
  `aria-label="Messages from X"`, `Stamp` for Received, `StatusChip` with `MESSAGE_STATUS_WORD`
  for Status, and unchanged body rendering.
- **Edit and add**: split `ReporterForm` into inline and card variants, like `LinkForm`. The card's
  h2 is "New reporter", the labels are unchanged, and the hint is "Adding one mints its key and
  shows it once, at the top of this page."
- **Tests**:
  - Add `'/api/v1/probes': { body: [] }` to **every** existing stub in `admin-reporters-page.test.tsx`
    (`stubFetch` throws on undeclared paths).
  - New test (a): a watched reporter (`isWatched: true` plus a message probe whose `host` is the
    identifier). Clicking `Delete X` shows the probe's name and **no** `Confirm delete X` button,
    and sends no DELETE.
  - New test (b): an overdue reporter's chip reads `Overdue`.

**Verify**: `npx vitest run src/pages/admin-reporters-page.test.tsx` → all pass.

### Step 8: API keys

Follow the `ApiKeys` artboard and D12.

- **Header**: title `API keys`, the existing description with its `create-api-key` `<code>`, count
  from `summariseApiKeys`, and a primary "New key" → `#api-key-name`.
- **Reveal**: `<KeyReveal inputId="revealed-api-key" …>`.
- **Sections**:
  - "Script keys", meta "Minted and revoked here", `<ol aria-label="Script keys">`;
  - "Reporter keys", meta "One per reporter · replace it on the Reporters page",
    `<ol aria-label="Reporter keys">`.
  - Omit an empty section; show `EMPTY_STATE` when both are empty.
- **Grid**: `lg:grid-cols-[156px_minmax(0,1.2fr)_124px_112px_128px_128px_64px]`, with columns
  `State · Key · Scope · Created · Last used · Expires · Actions`.
- **State**: `Active` (`up`), `Expired` (`unknown`, `Clock`), or `Revoked {formatStamp(revokedAt,{time:false})}`
  (`paused`, `Ban`). The revoked row gets `ROW_TINT.paused`.
- **Key**: name in its own span, mono `tokenId` beneath it, and for paired keys a third line
  `Paired with the reporter <Link to="/admin/reporters">{reporterName}</Link>`. Keep the sentence
  in **one** element so the regex test matches.
- **Scope**: `Read` / `Read and write`.
- **Dates**: Created is `<Stamp time={false}>`. Last used is `<Stamp>` or `Never used`. Expires is
  `<Stamp time={false}>` or `Never`; when it is past, prefix it with `Expired `. **Never** write
  the word "Revoked" outside the chip.
- **Actions**: one danger `IconButton` (`Ban`), labelled `Revoke {name} ({tokenId})` and
  `disabled` when revoked, which opens a `ConfirmStrip` "Revoke {name}? It stops working at once."
  with `confirmLabel="Confirm revoke {name} ({tokenId})"`.
- **Card**: h2 "New key", the three fields in one auto-fit row, "Create key" (unchanged), and the
  hint "Leave Expires empty for a key that does not expire. The key is shown once, at the top of
  this page." Keep the `Expires` label text exactly, without "(optional)", because the date
  input's hint already says so.
- **Test change**: `admin-api-keys-page.test.tsx:53` currently reads
  `within(screen.getByRole('list', { name: 'API keys' }))`. The fixture's readWrite key
  (`clockmaster restic`) is reporter-paired, so it now sits in `Reporter keys`. Change only
  `'API keys'` → `'Reporter keys'` on that line, and update its comment. The test's intent (scope
  away from the select) is unchanged.

**Verify**: `npx vitest run src/pages/admin-api-keys-page.test.tsx` → all pass.

### Step 9: Weather

Follow the `Weather` artboard as amended by D10 and D11.

- **Header**: title `Weather`, description "One location for the whole household. Paste its
  coordinates; there is no place search.", and a secondary link "Weather page" → `/weather`.
- **Section "Location"**: meta "Feeds the dashboard widget and the weather page".
  - **When set**, a `PANEL` with a column head and one row, grid
    `lg:grid-cols-[116px_minmax(0,1fr)_minmax(0,1fr)_160px_64px]`:
    - `StatusChip up` "Set";
    - place, or `—`;
    - mono `lat.toFixed(4), lon.toFixed(4)`;
    - units word (`Metric · °C, km/h` / `Imperial · °F, mph`);
    - a danger `IconButton` `Remove location` (`Trash2`) → `ConfirmStrip` "Remove the location?
      The dashboard shows no weather until one is set." with `confirmLabel="Confirm remove location"`.
  - **When unset**, an `EMPTY_STATE` reading "No location yet, so the dashboard shows no weather."
- Below the panel, always render the form with `CARD_FORM`, h2 "Set the location" (unset) or
  "Change the location" (set). It has four fields in a `repeat(auto-fit,minmax(180px,1fr))` grid,
  with labels unchanged, the hint "Decimal degrees: latitude −90 to 90, longitude −180 to 180.",
  and "Save location".
- **Test change**: in `admin-weather-page.test.tsx`, the test starting at `:45` gets one line
  added after `await user.click(removeButton)`:
  `await user.click(screen.getByRole('button', { name: 'Confirm remove location' }))`. Rename the
  test to "…asks first, then issues DELETE with an empty body". Nothing else changes.
  `e2e/weather.spec.ts` only checks visibility and stays untouched.

**Verify**: `npx vitest run src/pages/admin-weather-page.test.tsx` → all pass.

### Step 10: Admin home

Follow the `Main` artboard and D5.

- `AdminPageHeader`: title `Admin`, description "Everything the dashboard shows is set up here.
  Each section opens its own page." No count.
- One `<nav aria-label="Admin sections">` wraps three `AdminSection`s:
  - "Monitoring": Probes, Probe groups, Reporters;
  - "Content": Links, Pages, Weather;
  - "Access": API keys.
- Each section is a `PANEL` with grid `lg:grid-cols-[200px_minmax(0,1fr)_minmax(0,300px)_24px]`,
  with columns `Section · What it sets up · Now · chevron`.
- **The `<Link>` contains only the label** (`Probes`…), so the accessible names stay exact. The
  description and summary are sibling spans, and the chevron is `aria-hidden`.
- **Now**: down and unstable chips when their counts are > 0 (`StatusChip` with words such as
  `1 down`, `1 overdue`), then the summary's mono `text`. Tint the row `ROW_TINT.down` when `down`
  is > 0, else `ROW_TINT.unstable` when `unstable` is > 0.
- Call the seven hooks at the top of the page.
- Create `admin-home-page.test.tsx`:
  - (a) with all seven endpoints stubbed, the nav has the 7 links with exact names and
    `Probes`' row shows `1 down`;
  - (b) with every endpoint stubbed to `500`, the 7 links still render.
- If `App.test.tsx`'s `/admin` test now fails on an undeclared fetch, add the seven paths with
  empty bodies to its stub.

**Verify**: `npx vitest run src/pages/admin-home-page.test.tsx src/App.test.tsx` → all pass.

### Step 11: e2e: the watched-reporter case and contrast

1. In `e2e/reporters.spec.ts`, replace `:136-140` (the Confirm-delete click and the refusal-alert
   expectation) with the code below. Update the comment above it to say Delete explains instead of
   attempting.

   ```ts
   await row.getByRole('button', { name: `Delete ${reporterName}` }).click()
   // A watched reporter cannot be deleted; the row says which probe to change instead (plan 025 D6).
   await expect(row).toContainText(probeName)
   await expect(row.getByRole('button', { name: `Confirm delete ${reporterName}` })).toHaveCount(0)
   ```

2. In `e2e/contrast.spec.ts`, add `/admin` and `/admin/api-keys` to the axe `color-contrast`
   checks in both schemes. Copy the existing dashboard test pair's structure (`:26-41`), including
   how it switches scheme.

**Verify**: `./ci/run-ci.sh e2e` → `PASS — e2e`, exit 0. This also re-runs `layout.spec.ts`'s
overflow and 40px checks on every admin route at both viewports.

### Step 12: Render with realistic data, record, gate

1. **Render check (temporary, never committed).**
   - Create `e2e/zz-render-check.spec.ts`. Seed data through `request.post`, exactly as
     `e2e/reporters.spec.ts:115-128` does:
     - 6 links;
     - 3 groups, one empty;
     - 4 pages, 1 a draft (the payload is in `e2e/pages.spec.ts`);
     - 3 reporters, one watched by a message probe;
     - 3 API keys, revoking one through `DELETE /api/v1/api-keys/{id}`;
     - a weather location.
   - Then visit every admin route and save `page.screenshot({ fullPage: true, path: 'test-results/render-check/<route>-<project>.png' })`.
   - Run `./ci/run-ci.sh e2e`, and list the screenshot paths in your report.
   - Compare each against its canvas artboard and fix what differs, D1–D16 excepted.
   - **Delete the spec** before committing (`git status` must not show it).
2. **`docs/ARCHITECTURE.md`**: add `### 3.N Admin pages share one set of primitives`, where N is
   the highest existing number + 1 (30 at planning time). In 5–10 lines, record:
   - the primitives and their files;
   - D5 (client-side summaries, no endpoint);
   - D6 (watched reporters explain rather than attempt);
   - D7 (`formatStamp`);
   - D12 (revoke stays on paired keys).
3. **`plans/README.md`**: set row 025 to `IN PROGRESS — awaiting manual review`.
4. **Gate, one suite at a time**: `./ci/run-ci.sh web`, then `./ci/run-ci.sh e2e`. Report each
   test count against today's (`web` 203, `e2e` 125 per project).

**Verify**: both suites → `PASS`. `git status` shows only in-scope files, and
`git diff --stat main -- src/Homon.Api src/Homon.Domain src/Homon.Infrastructure tests` is empty.

## Test plan

- **New unit tests**:
  - `components/admin-primitives.test.tsx` (5);
  - `lib/format-stamp.test.ts` (4);
  - `lib/admin-summary.test.ts` (≥ 7);
  - `status-chip.test.tsx` (+1);
  - Links (+2), Groups (+2), Pages (new file, 3), Editor (+1), Reporters (+2), Home (new file, 2).
- **Pattern to copy**: `src/pages/admin-probes-page.test.tsx:388-411` for inline edit and the
  row-scoped confirm (`within(region)`, then `getByRole('form', { name })`).
- **Changed existing assertions** (exactly three):
  - `admin-api-keys-page.test.tsx:53` (list name);
  - `admin-weather-page.test.tsx` "Remove location" (one added click);
  - `e2e/reporters.spec.ts:136-140` (explain, not refuse).
- **Expected totals**: `web` ≈ 203 + 33, and `e2e` 125 + 2 contrast tests per project. Report the
  real numbers.

## Done criteria

- [ ] `./ci/run-ci.sh web` exits 0, and `./ci/run-ci.sh e2e` exits 0, run one at a time.
- [ ] `git diff --stat main -- src/Homon.Web/src/pages/admin-probes-page.test.tsx src/Homon.Web/e2e/layout.spec.ts src/Homon.Web/e2e/admin.spec.ts src/Homon.Web/e2e/pages.spec.ts src/Homon.Web/e2e/weather.spec.ts src/Homon.Web/e2e/helpers.ts` is empty.
- [ ] `grep -rln "const BUTTON_PRIMARY\|const ICON_BUTTON\|const FIELD_INPUT" src/Homon.Web/src/pages` prints nothing.
- [ ] `grep -rn "Move .* up\b" src/Homon.Web/src/pages/admin-links-page.tsx` finds no visible
  button text: the move buttons come from `MoveButtons`.
- [ ] `grep -rn "createdAt}\|receivedAt}\|lastUsedAt}" src/Homon.Web/src/pages/admin-*.tsx` prints
  nothing: every timestamp goes through `Stamp` or `formatStamp`.
- [ ] No changes under `src/Homon.Api`, `src/Homon.Domain`, `src/Homon.Infrastructure` or `tests/`.
- [ ] `e2e/zz-render-check.spec.ts` does not exist, and the screenshot paths are listed in the report.
- [ ] `docs/ARCHITECTURE.md` has the new §3.N, and row 025 reads `IN PROGRESS — awaiting manual review`.
- [ ] The branch `plan/025-admin-pages-restyle` is checked out in this checkout.

## STOP conditions

- An excerpt under "Current state" does not match the live file.
- Step 1's probes tests fail and the cause is not a missed import, i.e. the extraction changed
  behaviour.
- A step would require editing a test beyond the three named changes, or editing any file in the
  "net" list (`layout`, `admin`, `pages`, `weather` specs, `helpers.ts`, `admin-probes-page.test.tsx`).
- Any change to C# would be needed (e.g. you find a summary truly cannot be computed from the
  existing responses).
- `useEditorState` is not exported by the installed `@tiptap/react`.
- `e2e/layout.spec.ts` reports a button under 40px or a horizontal overflow that two fix attempts
  do not resolve.
- The render check shows a page whose layout differs from its artboard in a way D1–D16 do not
  explain, and the fix is not obvious.

## Maintenance notes

- New admin pages, including 004/005's SMB and SNMP fieldsets and 011's calendar settings, must
  build from `components/admin-*` and `icon-button.tsx`, not from local class strings.
- `ICON_BUTTON` is held to 40px by `e2e/layout.spec.ts`. Shrinking it to the canvas's 32px means
  changing that spec deliberately.
- The Admin home issues seven requests. If an eighth section arrives, consider a summary endpoint
  then, not before. D5's reasoning lives in `docs/ARCHITECTURE.md`.
- D6 duplicates the server's watcher rule in the browser
  (`kind === 'message' && host === identifier`). If `ReporterEndpoints.cs:234-243` changes, change
  the page with it.
- `formatStamp` follows the reader's locale and zone, so tests must never pin its text.
- **For reviewers**:
  - diff the three changed assertions against their stated intent;
  - confirm `admin-probes-page.tsx` changed only by imports and substitutions (`git diff -w`);
  - open each page beside its artboard on the branch.
- **Deferred**:
  - the 640–1240px gutter in `app-shell.tsx`;
  - adopting `components/ui/*`;
  - adding contrast coverage for the other admin routes beyond the two in Step 11.
