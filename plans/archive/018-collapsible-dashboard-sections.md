# Plan 018: Collapsible dashboard sections, remembered per browser in local storage

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If anything under "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> (Step 8).
>
> **Drift check (run first)**:
> ```bash
> git diff --stat 9d70023..HEAD -- \
>   src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/pages/dashboard-page.test.tsx \
>   src/Homon.Web/src/lib/status.ts src/Homon.Web/src/lib/status.test.ts \
>   src/Homon.Web/src/lib/theme.ts src/Homon.Web/src/index.css \
>   src/Homon.Web/e2e/dashboard-groups.spec.ts src/Homon.Web/e2e/refresh.spec.ts \
>   src/Homon.Web/e2e/layout.spec.ts src/Homon.Web/e2e/helpers.ts docs/ARCHITECTURE.md
> ```
> Empty output means no drift. If any of those files changed, compare the "Current state"
> excerpts below against the live code before proceeding; on a mismatch, treat it as a STOP
> condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW-MEDIUM (SPA only — no API, no database, no migration; the risk is entirely in
  four existing test suites that assert the dashboard's headings, rows and tap targets)
- **Depends on**: none (002, 006, 007, 010, 012 and 014 are all DONE; this builds on what they left)
- **Category**: feature
- **Planned at**: commit `9d70023`, 2026-09-19
- **Reviewed**: 2026-09-19 (`review-plan`, plus a cold read by a fresh-context agent against
  `9d70023`). Fixed: an unscoped Playwright `getByText('1 paused')` in Step 6 that would have
  collided with the dashboard's stat strip and failed on strict mode; two done-criteria greps
  (`sr-only`, `aria-live|role="status"`) that matched the very comments Step 3 requires and so
  reported failure on a correct implementation; a garbled test list in Step 6; every `file:line`
  citation in "Excerpts" (they were 1–3 lines off — the quoted code itself was verbatim-correct);
  a "six new buttons on `/`" claim that is four on the bare route `refresh.spec.ts` loads. Added:
  the imports Steps 4 and 5 need, the rule that `knownSectionIds` must follow the `sections`
  computation, a ban on widening `DashboardSection` (two whole-object `toEqual` assertions in
  `status.test.ts` would fail), and a verification gate on Step 7.
- **Requested by**: the maintainer, verbatim — *"I want to be able to collapse the sections
  (probe groups) individually. The state of the sections (collapsed/expanded) should be
  remembered browser by browser and saved as a cookie."* Three answers shaped the plan from
  there: **every** dashboard section collapses (not only the probe groups); a collapsed probe
  section keeps a small state summary in its header; and, once the trade-off was laid out on
  2026-09-19, **`localStorage` replaced the cookie** — identical per-browser behaviour, no
  `Secure` attribute to get wrong on a plain-HTTP LAN (D1). The quote above is kept verbatim as
  the record of the original request; it is not the instruction to follow.

## Why this matters

The dashboard is one long single column: one section per probe group, then the ungrouped rest,
then Links, then (when any exist) Pages, then Weather. Nothing folds. A household with six
groups and a dozen links gets a page that has to be scrolled past to reach the part any one
reader actually cares about, and there is no way to say "I never look at Storage" — every
visit renders everything at full height.

After this plan each section's heading is a disclosure control: click it and the section's body
folds away, click it again and it comes back. The choice is written to `localStorage`, so the same
browser on the same device opens the dashboard the way that reader left it, and a different
browser (the kitchen tablet, a phone) keeps its own arrangement. Nothing is stored server-side
and no API changes — this is a per-browser display preference, the same class of thing as the
theme toggle, and it is deliberately *not* household-wide state.

A collapsed section must not hide bad news, so a collapsed **probe** section keeps a short
`1 down · 4 up` line beside its heading. The stat strip at the top of the page already counts
every probe regardless — the per-section line just says which section the trouble is in.

## Current state

### The files that matter

- `src/Homon.Web/src/pages/dashboard-page.tsx` — the whole page. Four kinds of `<section>`: the
  mapped probe sections (line 139), Links (line 200), the conditional Pages (line 233) and
  Weather (line 251). All four share `SECTION_LABEL` / `SECTION_GAP` / `PANEL` (lines 98–100).
- `src/Homon.Web/src/lib/status.ts` — `dashboardSections()` builds the probe sections and
  assigns their ids and `headingId`s; `PHONE_SEVERITY_ORDER` already orders the five states
  worst-first.
- `src/Homon.Web/src/lib/theme.ts` + `src/Homon.Web/src/lib/theme.test.ts` — **the exemplar for
  this plan's persistence module**: an exported storage-key constant, a `get`/`set` pair each
  wrapped in `try`/`catch`, and a `useX()` hook whose toggle writes storage and then calls
  `setState`. Copy that shape almost verbatim — this plan's module differs from it only in the
  key it reads and in holding a JSON array rather than a single word.
- `src/Homon.Web/src/components/theme-toggle.tsx` + `theme-toggle.test.tsx` — the exemplar for a
  small button component and its test (`userEvent.setup()` inside each test that clicks).
- `src/Homon.Web/src/components/status-chip.tsx` — the exemplar for a presentational component
  with `Record<…>` lookup tables and a doc comment naming the brief rule it implements.
- `src/Homon.Web/src/test/render.tsx` (`renderWithProviders`) and `src/Homon.Web/src/test/fetch.ts`
  (`stubFetch`) — the only test helpers this repo has. There is no MSW.
- `src/Homon.Web/e2e/dashboard-groups.spec.ts` — the exemplar for an e2e spec that seeds a probe
  group and a **paused** probe through the authenticated API and cleans both up in `afterEach`.
- `src/Homon.Web/e2e/helpers.ts` — `expectTappable`, `expectNoHorizontalOverflow`.

### Excerpts, as they exist at `9d70023`

`src/Homon.Web/src/pages/dashboard-page.tsx:97-100` — the three shared class strings:

```tsx
/** `docs/design-brief.md`'s Section label rule: 12px/600/0.12em/uppercase, 10px above its panel. */
const SECTION_LABEL = 'text-[12px] font-semibold uppercase tracking-[0.12em] text-muted'
const SECTION_GAP = 'flex flex-col gap-[10px]'
const PANEL = 'rounded-md border border-line bg-surface'
```

`src/Homon.Web/src/pages/dashboard-page.tsx:139-143` — the probe sections' opening, the shape
every one of the four sections repeats:

```tsx
      {sections.map((section) => (
        <section key={section.id} aria-labelledby={section.headingId} className={SECTION_GAP}>
          <h2 id={section.headingId} className={SECTION_LABEL}>
            {section.heading}
          </h2>
```

`src/Homon.Web/src/lib/status.ts:77-85` — the section type and where its `id` comes from:

```ts
/** One rendered section of the dashboard's Services area: a group, or the ungrouped rest. */
export interface DashboardSection {
  /** Stable identity for a `key` prop — `group.id`, or `'ungrouped'`. */
  id: string
  /** The `<h2 id>` — `probe-group-{id}-heading`, never derived from the group's name, or `services-heading`. */
  headingId: string
  heading: string
  probes: StatusProbe[]
}
```

`src/Homon.Web/src/lib/status.ts:87-93` — the severity order to reuse for the collapsed summary:

```ts
const PHONE_SEVERITY_ORDER: Record<ProbeState, number> = {
  down: 0,
  unstable: 1,
  unknown: 2,
  up: 3,
  paused: 4,
}
```

`src/Homon.Web/src/lib/theme.ts:19-45` — the persistence shape to copy:

```ts
/** `null` when nothing is stored — dark renders by default, the bootstrap script's own rule. */
export function getStoredTheme(): Theme | null {
  try {
    return window.localStorage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : null
  } catch {
    return null
  }
}

/**
 * Writes the choice and flips the DOM attribute immediately, so the toggle is instant rather
 * than waiting on a re-render. `try/catch` around the write only — the DOM update must still
 * happen even where storage is unavailable (private browsing, disabled storage).
 */
export function setTheme(theme: Theme): void {
  …
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // Unavailable storage — the DOM attribute above still took effect for this page life.
  }
}
```

`src/Homon.Web/src/lib/theme.ts:51-65` — the hook shape to copy (write, then `setState`; the
callback depends on the current value rather than using a state updater):

```ts
export function useTheme(): { theme: Theme; toggleTheme: () => void } {
  const [theme, setThemeState] = useState<Theme>(() => readDomTheme())
  …
  const toggleTheme = useCallback(() => {
    const next: Theme = theme === 'light' ? 'dark' : 'light'
    setTheme(next)
    setThemeState(next)
  }, [theme])

  return { theme, toggleTheme }
}
```

### The four existing assertions that constrain the markup

**Read this section twice. Every one of these is already green and must stay green, and three
of them will break on the obvious implementation.** They are quoted here because you have not
read these files.

`src/Homon.Web/e2e/dashboard-groups.spec.ts:66-74` — **every `<h2>`'s text is asserted exactly**:

```ts
    const headings = page.getByRole('heading', { level: 2 })
    …
    await expect(headings).toHaveText(['Hosts', 'Storage', 'Other', 'Links', 'Pages', 'Weather'])
```

Playwright's `toHaveText` with an array is an exact, whitespace-normalised match on each
element's `textContent`. So the heading's rendered text must remain **exactly** the section
name: no count, no "(collapsed)", no `sr-only` span, no visible chevron character inside the
`<h2>`. An `<svg>` icon contributes no text and is therefore allowed inside it.

`src/Homon.Web/e2e/refresh.spec.ts:20-24` — **every `<button>` on `/` must clear 40×40**:

```ts
  test('the Refresh button is there and is tappable', async ({ page }) => {
    await page.goto('/')

    await expectTappable(page, 'button')
  })
```

`expectTappable` (`e2e/helpers.ts`) fails any element matching the selector whose rendered box
is narrower **or** shorter than 40px. The section headings are 12px text; a button wrapped
naïvely around one is about 16px tall and fails this. On the bare `/` that `refresh.spec.ts`
itself loads there are **four** new buttons (Services, Links, Pages — `e2e/auth.setup.ts` seeds
one published page for the whole run — and Weather); with another spec's groups seeded there are
six. `expectTappable` checks every `<button>` on the page regardless of the count, so the number
does not matter — only that each one clears 40×40.

`src/Homon.Web/src/pages/dashboard-page.test.tsx:60-76` — the regions are queried by their
accessible name, which comes from the `<h2>` through `aria-labelledby`:

```tsx
    expect(await screen.findByRole('region', { name: 'Hosts' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Storage' })).toBeInTheDocument()
    …
    expect(within(hosts).getByText('Shared device')).toBeInTheDocument()
```

`src/Homon.Web/e2e/layout.spec.ts:28-33` — `Services` and `Links` must stay visible `<h2>`s on `/`.

### The repo conventions that apply

From `CLAUDE.md`:

> **Comments carry the reasoning.** Config files, scripts and Dockerfiles explain the
> alternative that was rejected and why. Keep that up; a bare setting is a setting somebody
> will "simplify" back.

> **TypeScript**: kebab-case files, named exports (`App.tsx` is the only default), `@/` alias,
> no MSW — stub `fetch` with `src/test/fetch.ts`. `npm ci`, never `npm install`, in the gate.

From `docs/design-brief.md:206,215`:

> | Section label `h2` | 12px / 600 / 0.12em / uppercase | same |
>
> - Section label sits 10px above its panel.

## Decisions — implement these exactly; the reasoning is the point

**D1. `localStorage`, not a cookie — and this overrides the original request.** The maintainer
asked for a cookie first ("saved as a cookie", quoted under Status) and chose `localStorage` on
2026-09-19 once the trade-off was laid out. Put both the choice and the reasons in the module's
doc comment, because "make it a cookie" is exactly the instruction someone re-issues after
reading the Status section alone:

- **A cookie here has a silent failure mode.** The SPA and the API are **one origin** in every
  deployment (`nginx.conf` proxies `/api/`; `vite.config.ts` does the same in dev and e2e), and
  the production host serves the LAN over **plain HTTP** on port 8102 (plan 015,
  `docs/ARCHITECTURE.md` §3.21). Add `Secure` to that cookie — which is what every cookie
  checklist says to do — and the browser drops it on an `http://` origin: the feature then passes
  every test and every HTTPS deployment and remembers nothing on the one dashboard the household
  actually uses. `localStorage` has no attribute to get wrong.
- **A cookie rides on every request, for nothing.** Same origin means the value is attached to
  `/status` every 30 s plus links, pages and weather — a few hundred bytes of GUIDs, roughly
  700 KB a day. Negligible on a LAN, but bought for nothing: no server-side code reads it. The
  API reads only `homon.sid` (`src/Homon.Api/Program.cs:297`).
- **The app already does exactly this.** `src/Homon.Web/src/lib/theme.ts` persists a per-browser
  display preference in `localStorage`. One mechanism, one mental model, one exemplar.

The one thing a cookie would have bought — a value the **server** can read, for a future
server-rendered dashboard — does not apply: `src/Homon.Web` is a Vite SPA and nginx serves a
static bundle. If that ever changes, this module is the only thing to swap.

**D2. One key, holding a JSON array of ids.** `homon-collapsed-sections`, e.g.
`["group-hosts","links"]`. Parse **defensively**: `JSON.parse` inside a `try`/`catch`, and
discard anything that is not an array of non-empty strings. This read runs inside a `useState`
initialiser, where a throw blanks the entire dashboard — a value left by an older version of
this code, by a newer one, or by a curious reader with dev tools open must degrade to
"everything expanded", never to an exception.

**D3. No expiry to manage, and no durability promise either.** `localStorage` persists until the
reader clears site data, which is what "remembered" should mean for a wall tablet opened once a
month. Say in the comment that this is *not* a guarantee: Safari's ITP evicts script-writable
storage after seven days without interaction and treats a JS-set cookie identically, so the
cookie would not have bought longevity. Nothing in this feature may assume the value survives —
losing it means every section is expanded, which is the default anyway.

**D4. Store the collapsed ids only; absence means expanded.** Expanded is the default and the
status quo, so a first-ever visit, cleared site data, a private window and a brand-new probe
group all behave identically without a single special case.

**D5. Validate on read, cap at 50 on write.** Keep only non-empty strings, and keep at most the
last 50 — a household collapses a handful of sections, and the cap exists so that a long-lived
browser cannot accumulate the ids of a thousand deleted probe groups, not because 50 means
anything. **No character-class rule and no separator escaping**: JSON handles every string, which
is half the reason for D2.

**D6. Writing an empty set removes the key** (`removeItem`), rather than storing `[]`. "Never
used" and "used, then everything re-expanded" are the same state and should look the same in dev
tools — and it makes the key's presence a truthful signal, which Step 6's third e2e test asserts
on directly.

**D7. Prune unknown ids on write, but only once the status data has arrived.** Deleting a probe
group would otherwise leave its GUID in storage forever. Pruning on each write, against the
list of sections currently on the page, garbage-collects it for free. The guard matters: before
`/status` resolves, `dashboardSections(undefined, …)` returns a single `ungrouped` section, so
pruning at that moment (if the reader managed to collapse *Links* while the status request was
still in flight, or had failed) would wipe every group's remembered state. Pass the known-id
list as `string[] | null` and skip pruning when it is `null`.

**D8. The toggle is a `<button>` *inside* the `<h2>`, with the heading text as its only text.**
This is the one arrangement that satisfies all four existing assertions at once: the `<h2>`'s
`textContent` stays exactly the section name (`dashboard-groups.spec.ts`), the region's
accessible name through `aria-labelledby` stays the section name
(`dashboard-page.test.tsx`), the heading stays visible (`layout.spec.ts`), and the control is a
real button with real keyboard behaviour. The chevron is an `aria-hidden` lucide icon — an
`<svg>` contributes nothing to `textContent` or to the accessible name. **Do not** add an
`sr-only` span, a title attribute with different wording, or a visible count inside the `<h2>`.

**D9. The button carries `min-h-10 min-w-10` (40px), and that outranks the brief's 10px gap.**
`refresh.spec.ts` runs `expectTappable(page, 'button')` on `/` over *every* button on the page,
and these six are buttons on `/`. A 12px label inside a 40px-tall button makes the header row
taller than the brief's "section label sits 10px above its panel" — accept that. The tap-target
floor is enforced by the gate; the 10px is enforced by nobody. Record the deviation in the
`ARCHITECTURE.md` section (Step 7), the way plan 014's D2 recorded its own.

**D10. Collapse with the `hidden` attribute on a wrapper element, not by not rendering.**
`aria-controls` must point at an element that exists, and `hidden` is the canonical disclosure
pattern: the panel leaves the accessibility tree and `toBeVisible()` / Playwright visibility
both report it hidden. **Do not put a `display` utility (`flex`, `grid`, `block`, …) on that
wrapper** — a Tailwind `display` class beats `[hidden]`'s `display: none` and the section would
stay visible while announcing itself collapsed. The wrapper takes no classes at all.

**D11. The collapsed summary lives beside the `<h2>`, never inside it, and only for probe
sections.** `1 down · 4 up`, mono/12.5px/`muted` — the stat strip's own separator and register.
States in worst-first order (down, unstable, unknown, up, paused, reusing the order
`PHONE_SEVERITY_ORDER` already encodes), zero counts omitted, empty string for a section with no
probes. Links, Pages and Weather get no summary: "3 links" tells a reader nothing they wanted.

**D12. Not a live region.** No `role="status"`, no `aria-live` anywhere in this feature. The
button's `aria-expanded` is the entire announcement, and it is the correct one. `role="status"`
in this app belongs to the session check (`components/require-administrator.tsx`).

**D13. No bootstrap script, unlike the theme.** `public/theme-bootstrap.js` exists because the
theme must be right *before first paint* or the page flashes white. Sections are different: they
render only after `/status`, `/links` etc. resolve, and the stored value is read synchronously in a
`useState` initialiser, which runs before the first paint that could show a section at all.
Adding a second bootstrap script here would be cargo cult.

**D14. No API change, no database, no server-side state.** `git diff --name-only main` must list
nothing under `src/Homon.Api/`, `src/Homon.Domain/`, `src/Homon.Infrastructure/` or
`tests/Homon.Api.Tests/`.

## How to read every command in this plan

- **Working directory.** Every command is run from the **repository root** unless it begins with
  an explicit `cd`. `./ci/run-ci.sh` is repo-root only.
- **`grep` exits 1 when it matches nothing.** Several checks below succeed *by finding nothing*.
  Each is wrapped so that it prints `OK` or `FAIL` and exits 0 either way. Judge the printed
  word, never the exit code.

## Commands you will need

`ci/run-ci.sh` is the gate. **Run one suite at a time on this machine** — the full run is
memory-hungry here, and an out-of-memory kill or a `0x80131506` abort is environmental, not a
failure of your change; retry the suite once before treating it as real.

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Install | `cd src/Homon.Web && npm ci` | exit 0 (never `npm install`) |
| Lint | `cd src/Homon.Web && npm run lint` | exit 0 (`oxlint`) |
| Typecheck + build | `cd src/Homon.Web && npm run build` | exit 0 |
| Unit tests | `cd src/Homon.Web && npm test` | exit 0, 0 failures |
| One unit file | `cd src/Homon.Web && npx vitest run src/lib/collapsed-sections.test.ts` | exit 0 |
| Web gate | `./ci/run-ci.sh web` | `PASS — web` |
| E2E gate | `./ci/run-ci.sh e2e` | `PASS — e2e` |
| Full gate (last) | `./ci/run-ci.sh` | `PASS — web api e2e` |

The API and database are untouched (D14), so `./ci/run-ci.sh api` must be unchanged; run it once
at the end to confirm you did not stray.

## Scope

**In scope** (the only files you may modify or create):

- `src/Homon.Web/src/lib/collapsed-sections.ts` (create)
- `src/Homon.Web/src/lib/collapsed-sections.test.ts` (create)
- `src/Homon.Web/src/components/collapsible-section.tsx` (create)
- `src/Homon.Web/src/components/collapsible-section.test.tsx` (create)
- `src/Homon.Web/src/lib/status.ts` (one new exported function; nothing else changes)
- `src/Homon.Web/src/lib/status.test.ts` (new tests appended)
- `src/Homon.Web/src/pages/dashboard-page.tsx`
- `src/Homon.Web/src/pages/dashboard-page.test.tsx` (new tests + storage cleanup; existing tests unchanged)
- `src/Homon.Web/e2e/dashboard-collapse.spec.ts` (create)
- `docs/ARCHITECTURE.md` (append one new `### 3.N` section — Step 7)
- `plans/README.md` (status row — Step 8)

**Out of scope — do NOT touch, even though they look related:**

- `src/Homon.Web/src/lib/theme.ts`, `theme-toggle.tsx`, `public/theme-bootstrap.js`. Read
  `theme.ts` as the exemplar; change nothing in it, and **do not refactor the two into a shared
  storage helper**. They hold different shapes (a single word against a JSON array) and have
  different first-paint requirements (D13); the duplication is two `try`/`catch` blocks and is
  cheaper than the abstraction that would hide them.
- `src/Homon.Web/e2e/dashboard-groups.spec.ts`, `e2e/refresh.spec.ts`, `e2e/layout.spec.ts`,
  `e2e/contrast.spec.ts`, `e2e/helpers.ts`. These are the net. **If one of them fails, the fix is
  in your markup, never in the spec.** See STOP conditions.
- `src/Homon.Web/src/test/render.tsx` and `src/test/fetch.ts`. Its comment warns that the
  provider nesting is load-bearing.
- **The `DashboardSection` interface in `src/Homon.Web/src/lib/status.ts`.** Add no field to it —
  not `summary`, not `collapsed`, however natural that looks. `src/Homon.Web/src/lib/status.test.ts`
  carries two assertions of the form
  `expect(sections).toEqual([{ id: 'ungrouped', headingId: 'services-heading', heading: 'Services', probes: [] }])`,
  which compare the **whole object**; one extra field fails both, and they are not yours to edit.
  The summary is computed by the caller (Step 4) and the collapsed flag lives in the hook. That is
  deliberate, not an oversight.
- `src/Homon.Web/src/components/app-shell.tsx`, `refresh-indicator.tsx`. No banner change.
- Anything under `src/Homon.Api/`, `src/Homon.Domain/`, `src/Homon.Infrastructure/`,
  `tests/Homon.Api.Tests/` (D14). There is no server-side preference here.
- `src/Homon.Web/src/components/ui/*` (shadcn primitives — committed but unused by every page;
  adopting them, including any `Collapsible`/`Accordion`, is its own plan). **Do not add a
  dependency for this.** A disclosure is a button and a `hidden` attribute.
- `src/Homon.Web/src/pages/admin-*.tsx`. Admin lists are not sections and do not collapse.
- `docs/design-brief.md`. It is the spec; D9 documents the deviation in `ARCHITECTURE.md`
  instead, which is where this repo records decisions.

## Git workflow

- Make the worktree yourself from **local `main`**, not `origin/main`:
  `git worktree add ../homon-018 -b plan/018-collapsible-sections main`
- Commit per step or per logical unit. Message style, from `git log`:
  `Dashboard: show validation problems' field messages instead of the generic fallback`
  — an area prefix, a colon, an imperative sentence, the plan number in parentheses where it
  clarifies. Use `Dashboard:` as the prefix here.
- Do **not** push or open a PR unless the operator asks.

## Steps

### Step 1: The persistence module

Create `src/Homon.Web/src/lib/collapsed-sections.ts`. Modelled on `src/Homon.Web/src/lib/theme.ts`
— an exported key constant, a pure parse helper, a `read`/`write` pair each wrapped in
`try`/`catch`, and a hook at the bottom. The shape below is the target; match it, comments
included — they carry D1, D2 and D6, which are the parts a later reader will otherwise
"simplify" away.

```ts
import { useCallback, useState } from 'react'

/**
 * Which dashboard sections this browser has folded away.
 *
 * `localStorage`, although the feature was first requested as a cookie (plan 018, D1). Three
 * reasons, in the order they matter:
 *
 * 1. A cookie here has a silent failure mode. The SPA and the API are ONE origin in every
 *    deployment, and the production host serves the LAN over plain HTTP (docs/ARCHITECTURE.md
 *    §3.21) — so a `Secure` attribute, which every cookie checklist tells you to add, would make
 *    this work in every test and on any HTTPS deployment and remember nothing on the household's
 *    actual dashboard. There is no equivalent mistake available here.
 * 2. A cookie would ride on every /status poll (every 30 s) and on links, pages and weather, for
 *    nothing: no server-side code reads it. The API reads only its own `homon.sid`.
 * 3. lib/theme.ts already persists a per-browser display preference exactly this way.
 *
 * The one thing a cookie buys — a value the SERVER can read — has no use while Homon is a Vite
 * SPA behind nginx. If that changes, this module is the only thing to swap.
 */
export const COLLAPSED_SECTIONS_STORAGE_KEY = 'homon-collapsed-sections'

/**
 * The ceiling on how many ids are kept, newest last. A household collapses a handful of
 * sections; this exists so that a long-lived browser cannot accumulate the ids of a thousand
 * deleted probe groups, not because 50 is a meaningful number.
 */
const MAX_IDS = 50

/**
 * Anything that is not an array of non-empty strings is discarded, and so is a value that will
 * not parse at all.
 *
 * This runs inside a `useState` initialiser, where a throw blanks the whole dashboard — so a
 * value written by an older version of this code, by a newer one, or by a curious reader with
 * dev tools open must all degrade to "everything expanded" rather than to an exception.
 */
export function parseCollapsedSections(raw: string | null): string[] {
  if (raw === null) {
    return []
  }

  let parsed: unknown

  try {
    parsed = JSON.parse(raw)
  } catch {
    return []
  }

  if (!Array.isArray(parsed)) {
    return []
  }

  const items = parsed as unknown[]

  return [...new Set(items.filter((id): id is string => typeof id === 'string' && id !== ''))].slice(-MAX_IDS)
}

export function readCollapsedSections(): Set<string> {
  try {
    return new Set(parseCollapsedSections(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)))
  } catch {
    // localStorage throws in a sandboxed frame and in some private-browsing modes. Everything
    // expanded is the right fallback — it is exactly what a first-ever visitor sees.
    return new Set()
  }
}

export function writeCollapsedSections(ids: Iterable<string>): void {
  const kept = [...new Set([...ids].filter((id) => id !== ''))].slice(-MAX_IDS)

  try {
    if (kept.length === 0) {
      // REMOVE, not setItem('[]'): "never used" and "used, then everything re-expanded" are the
      // same state and should look the same in dev tools — and the key's presence is what
      // e2e/dashboard-collapse.spec.ts asserts on.
      window.localStorage.removeItem(COLLAPSED_SECTIONS_STORAGE_KEY)
    } else {
      window.localStorage.setItem(COLLAPSED_SECTIONS_STORAGE_KEY, JSON.stringify(kept))
    }
  } catch {
    // Unavailable storage — the React state below still took effect for this page life, exactly
    // as lib/theme.ts's setTheme still flips the DOM attribute when its own write fails.
  }
}

/**
 * Reads storage once, synchronously, in the state initialiser — early enough that the first
 * paint which could show a section already knows the answer, which is why this needs no
 * equivalent of `public/theme-bootstrap.js` (the theme has to be right before ANY paint; a
 * section cannot render before its data has loaded anyway).
 *
 * `knownSectionIds` prunes ids whose section no longer exists — a deleted probe group would
 * otherwise sit in storage forever. Pass `null` while the section list is not yet known: before
 * `/status` resolves the page believes it has exactly one section, and pruning against that
 * would wipe every group's remembered state.
 */
export function useCollapsedSections(): {
  collapsed: ReadonlySet<string>
  toggle: (id: string, knownSectionIds: readonly string[] | null) => void
} {
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => readCollapsedSections())

  const toggle = useCallback(
    (id: string, knownSectionIds: readonly string[] | null) => {
      const known = knownSectionIds === null ? null : new Set(knownSectionIds)
      const next = new Set(known === null ? collapsed : [...collapsed].filter((candidate) => known.has(candidate)))

      if (collapsed.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }

      writeCollapsedSections(next)
      setCollapsed(next)
    },
    [collapsed],
  )

  return { collapsed, toggle }
}
```

Now create `src/Homon.Web/src/lib/collapsed-sections.test.ts`, modelled on
`src/Homon.Web/src/lib/theme.test.ts` (`import { afterEach, beforeEach, describe, expect, it } from 'vitest'`,
`@/` alias import, no providers). **Open it with the same `beforeEach`/`afterEach` pair that file
uses** — `window.localStorage.clear()` in both — because jsdom keeps one storage area for the
whole file and a leaked key makes a later test pass or fail for reasons that have nothing to do
with it.

Six `it()` blocks, grouping cases the way `theme.test.ts` groups its assertions:

1. `parseCollapsedSections` — `null` → `[]`; `'[]'` → `[]`; `'["a","b"]'` → `['a','b']`;
   `'["a","a","b"]'` → `['a','b']`.
2. `parseCollapsedSections` survives garbage — `'not json'` → `[]`; `'{"a":1}'` → `[]`;
   `'["a",1,null,""]'` → `['a']`. This is the test that protects D2; without it a later
   "simplification" to a bare `JSON.parse` would pass everything else and blank the dashboard
   for whoever had the old value.
3. `readCollapsedSections` returns an empty set when nothing is stored.
4. `writeCollapsedSections` then `readCollapsedSections` round-trips a two-id set, and
   `window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)` is `'["a","b"]'`.
5. Writing an empty set removes the key (D6) —
   `expect(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)).toBeNull()` and
   `readCollapsedSections()` has size `0`.
6. The cap holds — write 60 generated ids (`id-0` … `id-59`), read back a set of size 50 that
   contains `id-59` and not `id-0`.

**Do not unit-test the `try`/`catch` fallbacks.** Making `window.localStorage` throw in jsdom
means stubbing the global, at which point the test asserts on the stub rather than on the
module. They exist for the same private-browsing and sandboxed-frame cases `lib/theme.ts`'s do,
and they are verified by reading the diff.

**Verify**: `cd src/Homon.Web && npx vitest run src/lib/collapsed-sections.test.ts` → exit 0, `6 passed`.

### Step 2: The collapsed-state summary

Add **one** exported function to `src/Homon.Web/src/lib/status.ts`, immediately after
`dashboardSections`. Change nothing else in that file.

```ts
/**
 * `1 down · 4 up` — what a collapsed probe section keeps in its header so that folding a group
 * away cannot hide a service that is down. Worst state first (the order `PHONE_SEVERITY_ORDER`
 * already encodes), zero counts omitted, `''` for a section with no probes. The `·` separator
 * and the mono/muted treatment match the stat strip at the top of the dashboard.
 */
export function summariseProbeStates(probes: readonly StatusProbe[]): string {
  const counts = new Map<ProbeState, number>()

  for (const probe of probes) {
    counts.set(probe.state, (counts.get(probe.state) ?? 0) + 1)
  }

  return [...counts.entries()]
    .sort(([a], [b]) => PHONE_SEVERITY_ORDER[a] - PHONE_SEVERITY_ORDER[b])
    .map(([state, count]) => `${String(count)} ${state}`)
    .join(' · ')
}
```

Append three `it()` blocks to `src/Homon.Web/src/lib/status.test.ts`, inside a new
`describe('summariseProbeStates', …)` placed at the **end** of the file, after
`describe('formatCheckedAt', …)`. Build fixtures with that file's existing
`makeProbe({ id, ... })` factory — it defaults `state: 'up'`, so pass `state` explicitly in every
case here. Do **not** add a fixture helper of your own:

- empty input → `''`;
- one down and four up, given in "up first" order → `'1 down · 4 up'` (this is the one that
  proves the ordering, so pass the probes in the *wrong* order deliberately);
- every state once → `'1 down · 1 unstable · 1 unknown · 1 up · 1 paused'`.

**Verify**: `cd src/Homon.Web && npx vitest run src/lib/status.test.ts` → exit 0, all tests pass.

### Step 3: The `CollapsibleSection` component

Create `src/Homon.Web/src/components/collapsible-section.tsx`. Every class on the button is
load-bearing and the reason is in the comments — keep them.

```tsx
import { ChevronDown, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'

/** `docs/design-brief.md`'s Section label rule: 12px/600/0.12em/uppercase. */
const SECTION_LABEL = 'text-[12px] font-semibold uppercase tracking-[0.12em] text-muted'

/**
 * One foldable section of the dashboard, and the only disclosure pattern in this app.
 *
 * The button lives INSIDE the <h2> and its text is the heading's text and nothing else. That is
 * not a style choice — it is the one arrangement that satisfies four existing assertions at
 * once: `e2e/dashboard-groups.spec.ts` matches every level-2 heading's textContent EXACTLY
 * against the section names, `dashboard-page.test.tsx` finds each region by the accessible name
 * this heading supplies through aria-labelledby, `e2e/layout.spec.ts` needs the headings
 * visible, and `e2e/refresh.spec.ts` runs expectTappable over every <button> on `/`. Adding an
 * sr-only span, a count or a text chevron inside the <h2> breaks the first two; shrinking the
 * button breaks the last. The lucide icon is safe there because an <svg> contributes nothing to
 * textContent or to the accessible name.
 *
 * `aria-expanded` on the button is the whole announcement — deliberately no role="status" and no
 * aria-live anywhere in this component. role="status" in this app belongs to the session check
 * (components/require-administrator.tsx).
 */
export function CollapsibleSection({
  headingId,
  heading,
  collapsed,
  onToggle,
  summary,
  children,
}: {
  headingId: string
  heading: string
  collapsed: boolean
  onToggle: () => void
  /** Shown beside the heading while collapsed, for probe sections only. */
  summary?: string
  children: ReactNode
}) {
  const panelId = `${headingId}-panel`
  const Chevron = collapsed ? ChevronRight : ChevronDown

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-[10px]">
      {/* flex-wrap, not a fixed row: a long group name and its summary must wrap rather than push
          the document sideways — e2e/layout.spec.ts asserts no horizontal overflow at a Pixel 7. */}
      <div className="flex flex-wrap items-center gap-x-3">
        <h2 id={headingId} className={SECTION_LABEL}>
          <button
            type="button"
            onClick={onToggle}
            aria-expanded={!collapsed}
            aria-controls={panelId}
            // min-h-10/min-w-10 is the 40px tap-target floor e2e/helpers.ts's expectTappable
            // enforces over EVERY button on `/`, and a 12px label does not reach it on its own.
            // The taller header row costs the design brief's "section label sits 10px above its
            // panel"; the floor is enforced by the gate and the 10px is enforced by nobody.
            // -mx-2 puts the padded hit area back in optical alignment with the panel below.
            className="-mx-2 inline-flex min-h-10 min-w-10 items-center gap-2 rounded-md px-2 text-left hover:text-text"
          >
            <Chevron aria-hidden="true" size={14} className="shrink-0" />
            {heading}
          </button>
        </h2>
        {collapsed && summary !== undefined && summary !== '' ? (
          <span className="mono text-[12.5px] text-muted">{summary}</span>
        ) : null}
      </div>
      {/* `hidden`, not a conditional render: aria-controls must point at an element that exists.
          This wrapper takes NO className — a Tailwind display utility (flex/grid/block) beats
          [hidden]'s display:none and would leave the panel on screen while the button announced
          it collapsed. */}
      <div id={panelId} hidden={collapsed}>
        {children}
      </div>
    </section>
  )
}
```

Create `src/Homon.Web/src/components/collapsible-section.test.tsx`, modelled on
`theme-toggle.test.tsx` (same imports, `userEvent.setup()` inside the test that clicks,
`renderWithProviders` even though this component needs no providers — it is the house style).
Five tests:

1. **It is a named region with its content showing.** Render expanded with a child
   `<p>Body</p>`; `expect(screen.getByRole('region', { name: 'Hosts' })).toBeInTheDocument()` and
   `expect(screen.getByText('Body')).toBeVisible()`.
2. **The heading's text is exactly the heading.**
   `expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent(/^Hosts$/)`. This is the
   unit-level guard for the e2e `toHaveText` assertion; it is the cheapest place to catch a
   stray `sr-only` span.
3. **`aria-expanded` follows `collapsed`.** Render expanded, assert the button named `Hosts` has
   `aria-expanded="true"`, then use the `rerender` that `renderWithProviders` returns (it
   re-renders through the same wrapper) with `collapsed` set, and assert `"false"`. Do **not**
   call `renderWithProviders` a second time in the same test: two copies of the same heading and
   button would then be in the document, and every `getByRole` throws on strict mode.
4. **Collapsed hides the body and shows the summary.** Render collapsed with
   `summary="1 down · 4 up"`; `expect(screen.getByText('Body')).not.toBeVisible()` and
   `expect(screen.getByText('1 down · 4 up')).toBeVisible()`. Then assert the summary is **not**
   inside the heading: `expect(screen.getByRole('heading', { level: 2 })).not.toHaveTextContent('down')`.
5. **Clicking calls `onToggle` once.** `const onToggle = vi.fn()`, click the button, expect one call.

**Verify**: `cd src/Homon.Web && npx vitest run src/components/collapsible-section.test.tsx` → exit 0, `5 passed`.

### Step 4: Wire the dashboard

Edit `src/Homon.Web/src/pages/dashboard-page.tsx`. Four sections become four
`<CollapsibleSection>` uses. The `SECTION_LABEL` constant moves into the component (Step 3), so
**delete it from this file** — `SECTION_GAP` and `PANEL` also lose their remaining users for the
section wrappers; delete `SECTION_GAP` too, and keep `PANEL`, which the panels themselves still
use. Do not leave an unused constant behind: `.oxlintrc.json` sets the `correctness` category to
`error` and unused variables are in it, so `npm run lint` fails on one. Move `SECTION_LABEL`'s doc
comment (`docs/design-brief.md`'s Section label rule) into the component along with the constant
rather than dropping it.

Add three imports at the top of the file — `CollapsibleSection` from
`@/components/collapsible-section`, `useCollapsedSections` from `@/lib/collapsed-sections`, and
`summariseProbeStates` added to the **existing** `@/lib/status` import (the file already imports
`dashboardSections`, `formatCheckedAt`, `useStatus` and the `ProbeState` type from there).

Then add the two lines below. `useCollapsedSections()` may sit with the other hook calls, but
`knownSectionIds` must come **after** the existing `const sections = dashboardSections(...)` line —
it reads `sections`, and placing it above is a compile error:

```tsx
  const { collapsed, toggle } = useCollapsedSections()

  // Everything that can be a section, not only what is on screen right now: `pages` disappears
  // entirely when no page is published, and pruning (lib/collapsed-sections.ts) must not forget
  // a reader's choice just because the Pages section is temporarily absent. `null` until the
  // status query has answered — see that module's comment for why pruning early is destructive.
  const knownSectionIds = status.data === undefined ? null : [...sections.map((section) => section.id), 'links', 'pages', 'weather']
```

The probe sections become:

```tsx
      {sections.map((section) => (
        <CollapsibleSection
          key={section.id}
          headingId={section.headingId}
          heading={section.heading}
          collapsed={collapsed.has(section.id)}
          onToggle={() => toggle(section.id, knownSectionIds)}
          summary={summariseProbeStates(section.probes)}
        >
          {section.probes.length === 0 ? (
            … the existing empty-state <div>, unchanged …
          ) : (
            … the existing <div className={`${PANEL} overflow-x-auto`}> table, unchanged …
          )}
        </CollapsibleSection>
      ))}
```

Links, Pages and Weather follow exactly the same shape, with these values and **no `summary`
prop** (D11):

| Section | `headingId` | `heading` | id passed to `toggle` |
| --- | --- | --- | --- |
| Links | `links-heading` | `Links` | `links` |
| Pages | `pages-heading` | `Pages` | `pages` |
| Weather | `weather-heading` | `` `Weather${weather?.place ? ` · ${weather.place}` : ''}` `` | `weather` |

Two things that must not change while you do this:

- **The Weather heading's text stays exactly what it is today**, place suffix included. The
  section *id* is the constant `weather`; the heading is the display string. Do not derive one
  from the other.
- **The Pages section stays conditional** on `pages.length > 0`. It gains a toggle; it does not
  gain a permanent slot.

**Verify**: `cd src/Homon.Web && npm run build && npm run lint` → both exit 0.
**Verify**: `cd src/Homon.Web && npx vitest run src/pages/dashboard-page.test.tsx` → exit 0, **all
existing tests still pass, unmodified**. If one fails here, re-read D8 before changing anything —
the likely cause is markup, not the test.

### Step 5: The dashboard's own tests

Edit `src/Homon.Web/src/pages/dashboard-page.test.tsx`.

**First, add storage cleanup.** The file has an `afterEach` calling `vi.unstubAllGlobals()`; add
`window.localStorage.clear()` to it, and add a matching `beforeEach` — the same pair
`src/Homon.Web/src/components/theme-toggle.test.tsx` already opens with. This is not optional:
jsdom keeps one storage area for the whole file, so without it the first test that collapses
something changes the starting state of every test after it — and because `getByText` finds
hidden elements too, the existing `within(hosts).getByText('Shared device')` assertions would
keep passing while actually rendering a collapsed section. The leak would be invisible.

The file currently imports only `describe/expect/it/vi/afterEach` from `vitest` and
`screen/within` from `@testing-library/react`, so add three imports it does not yet have:
`userEvent` (default import from `@testing-library/user-event`, as
`src/Homon.Web/src/components/theme-toggle.test.tsx` does), and `writeCollapsedSections` plus
`COLLAPSED_SECTIONS_STORAGE_KEY` from `@/lib/collapsed-sections`.

Then add three tests to the existing `describe('DashboardPage', …)`, using the file's existing
`statusWithASharedProbe` stub table:

1. **Sections start expanded and say so.** After `findByRole('region', { name: 'Hosts' })`,
   assert `screen.getByRole('button', { name: 'Hosts' })` has `aria-expanded="true"` and that
   `within(hosts).getByText('Shared device')` is **visible** (`toBeVisible`, not
   `toBeInTheDocument` — visibility is the whole point here).
2. **Clicking a heading collapses it and writes the choice.** `userEvent.setup()`, click the
   `Hosts` button, then: the probe row is `not.toBeVisible()`, the button reads
   `aria-expanded="false"`,
   `expect(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)).toBe('["group-hosts"]')`,
   and the `Storage` section — which holds the same probe — is still visible. That last
   assertion is the one that proves the state is per-section rather than per-probe.
3. **A stored id collapses a section on first paint, with its summary.** Call
   `writeCollapsedSections(['group-hosts'])` *before* rendering, render, then await the `Storage`
   region, and assert the `Hosts` button reads `aria-expanded="false"` and that
   `within(hosts).getByText('1 up')` is visible. The fixture's single probe is `state: 'up'`, so
   the summary is exactly `1 up`.

**Verify**: `cd src/Homon.Web && npx vitest run src/pages/dashboard-page.test.tsx` → exit 0, every
test passes — the three new ones and all the pre-existing ones.
**Verify**: `./ci/run-ci.sh web` → `PASS — web`.

### Step 6: The end-to-end spec

Create `src/Homon.Web/e2e/dashboard-collapse.spec.ts`. **Copy the seeding shape from
`e2e/dashboard-groups.spec.ts` exactly** — a `beforeEach` that POSTs a probe group and a probe
through the already-authenticated `request` fixture and then **pauses the probe**
(`PUT /api/v1/probes/{id}/pause` with `{ isPaused: true }`), and an `afterEach` that deletes
both. The scheduler is live in e2e (plan 002's Decision 3), so an unpaused probe means real ICMP
traffic and a nondeterministic status word — and this spec asserts on the summary text, which is
derived from that word. A paused probe gives a deterministic `1 paused`.

Name the group `Collapse` and the probe `Collapse device` — names no other spec uses, so the
section and its row can be addressed by name regardless of what else the shared e2e database
holds at the time. (`dashboard-groups.spec.ts` names its own probes `Shared device` and
`Ungrouped device`; do not reuse those.) Capture the group's id from the `beforeEach` POST
response into a variable named `collapseGroupId` — test 2 asserts on it. Clear the key in `afterEach` as well
(`await page.evaluate(() => { window.localStorage.removeItem('homon-collapsed-sections') })`),
following `e2e/contrast.spec.ts`'s reset of the theme key — each test gets a fresh context from
the stored auth state, so this is insurance rather than a known leak, and it is what the house
already does.

Four tests, each its own `test()`:

1. **Collapsing folds the section away and keeps its heading.** `page.goto('/')`, assert the row
   named `/Collapse device/` is visible, click `page.getByRole('button', { name: 'Collapse' })`,
   then: the row is `toBeHidden()`, `page.getByRole('heading', { level: 2, name: 'Collapse' })`
   is still `toBeVisible()`, and the collapsed summary is visible — **scoped to the region**:
   ```ts
   await expect(page.getByRole('region', { name: 'Collapse' }).getByText('1 paused')).toBeVisible()
   ```
   A bare `page.getByText('1 paused')` **fails**, and not because of your markup. Playwright's
   `getByText` matches a *substring* unless `{ exact: true }` is passed (`exact` defaults to
   false — unlike testing-library's `getByText`, which is exact by default), and the dashboard's
   stat strip at the top of the page reads `… · 1 paused · …` whenever exactly one probe is
   paused, which is precisely what this spec seeds. The unscoped locator therefore resolves to
   two elements and dies on strict mode. Every `getByText` in this repo's e2e suite is scoped to
   a container for this reason — see `e2e/weather.spec.ts` and `e2e/pages.spec.ts`.
2. **The choice survives a reload** — the test the whole plan exists for. Click to collapse,
   `await page.reload()`, then assert the row is still hidden and the button still reads
   `aria-expanded="false"`. Then assert the stored value is really the mechanism:
   ```ts
   const stored = await page.evaluate(() => window.localStorage.getItem('homon-collapsed-sections'))
   expect(stored).toContain(collapseGroupId)
   ```
   `toContain` rather than an exact string: the stored value is a JSON array and the group's id
   is a GUID minted by the `beforeEach`, so the id is the part worth asserting on.
3. **Expanding again removes the key.** Click twice, reload, assert the row is visible, then read
   storage again in this test's own `page.evaluate(...)` — `stored` from test 2 does not exist
   here — and assert it is now `null`, which is D6.
4. **A collapsed dashboard still fits and is still tappable.** Collapse the section, then call
   `expectNoHorizontalOverflow(page)` and `expectTappable(page, 'button')` (both imported from
   `./helpers`). This runs at both viewport projects automatically and is the belt-and-braces on
   D9 — `refresh.spec.ts` only ever sees the expanded state, and a taller wrapped header row is
   exactly the kind of thing that overflows a Pixel 7 without anyone noticing.

**Verify**: `./ci/run-ci.sh e2e` → `PASS — e2e`. The three specs most likely to catch a mistake
are `dashboard-groups.spec.ts` (exact heading text), `refresh.spec.ts` (the 40px floor) and
`contrast.spec.ts` (axe on `/`, both schemes).

### Step 7: The architecture note

Append a new section to `docs/ARCHITECTURE.md`. **Find the number first** —
`grep -n "^### 3\." docs/ARCHITECTURE.md | tail -1` (it is `### 3.21` at `9d70023`; take the next
one, do not hard-code `3.22` if the grep says otherwise). Title it along the lines of
**"Collapsed dashboard sections live in this browser's local storage, and the heading is the
control"**, and
record, in the voice of the surrounding sections:

- D1 — why `localStorage` and not the cookie that was asked for first: the `Secure` trap on a
  plain-HTTP LAN (cross-reference §3.21), a value on every request that no server code reads,
  and `lib/theme.ts` already doing precisely this. Say what a cookie would have bought (a
  server-readable value) and why that is worth nothing while Homon is an SPA behind nginx;
- D2 and D5 — one key, a JSON array, parsed defensively because the read runs inside a
  `useState` initialiser where a throw blanks the dashboard;
- D4 — collapsed ids only, so expanded is the default for a new group and a new browser alike;
- D8 and D9 — the button-inside-the-heading arrangement and the 40px floor, naming the four
  assertions that force it, and the deliberate deviation from the design brief's 10px gap;
- D13 — why this needs no bootstrap script when the theme does;
- D14 — no server-side state: this is a display preference, not a household setting, and moving
  it server-side later would be a different decision with a migration behind it.

**Verify**:
```bash
grep -c "^### 3\." docs/ARCHITECTURE.md
git diff --stat main -- docs/ARCHITECTURE.md
```
→ the first prints `22` (it is `21` at `9d70023`; if it already printed `22` before you started,
another plan took that number — use the next free one and ignore this count), and the second shows
**insertions only, no deletions**: you are appending a section, not editing an existing one.

### Step 8: Reconcile the index

Edit `plans/README.md`. The plan table **already has an 018 row** — it was added when this plan
was written. Change its status cell from `planned` to
`DONE (<today's date>, <the short SHA of your final commit>)`. Do not add a second 018 row, and
do not touch any other row or any follow-up bullet.

**Verify** (succeeds by finding nothing — read the printed word, not the exit code):
```bash
grep -c "^| 018 " plans/README.md
```
→ prints `1`.

## Test plan

| File | New tests | Pattern to follow |
| --- | --- | --- |
| `src/lib/collapsed-sections.test.ts` (new) | 6: parse; parse survives garbage; read with nothing stored; write→read round-trip; an empty set removes the key; the 50-id cap holds | `src/lib/theme.test.ts` |
| `src/lib/status.test.ts` | 3 in a new `describe('summariseProbeStates')`: empty; worst-first ordering from wrongly-ordered input; all five states | that file's existing fixtures |
| `src/components/collapsible-section.test.tsx` (new) | 5: named region with visible content; heading text is exactly the heading; `aria-expanded` follows `collapsed`; collapsed hides the body and shows the summary outside the heading; click calls `onToggle` once | `src/components/theme-toggle.test.tsx` |
| `src/pages/dashboard-page.test.tsx` | 3: expanded by default; click collapses, writes the stored array, and leaves the other section holding the same probe alone; a pre-stored id collapses on first paint with its summary. **Plus `window.localStorage.clear()` in `beforeEach`/`afterEach`** | the file's own existing tests |
| `e2e/dashboard-collapse.spec.ts` (new) | 4: collapse folds the body and keeps the heading; the choice survives a reload and the stored key holds the group id; expanding again removes the key; a collapsed dashboard still fits and is still tappable | `e2e/dashboard-groups.spec.ts` for seeding, `e2e/theme.spec.ts` for shape |

**Nothing tests the `try`/`catch` fallbacks**, on purpose: making `window.localStorage` throw in
jsdom means stubbing the global, and the test would then be asserting on the stub rather than on
the module. They exist for the same private-browsing and sandboxed-frame cases `lib/theme.ts`'s
do, and they are verified by reading the diff.

The existing suites are the real net here. Every one of them must pass **unmodified** except
`dashboard-page.test.tsx`, which gains three tests and cleanup and loses nothing. In particular
`e2e/dashboard-groups.spec.ts`'s exact `toHaveText` on every `<h2>` and `e2e/refresh.spec.ts`'s
`expectTappable(page, 'button')` on `/` are the two assertions most likely to catch a mistake in
Steps 3 and 4.

## Done criteria

All of these must hold:

- [ ] `cd src/Homon.Web && npm run lint` exits 0
- [ ] `cd src/Homon.Web && npm run build` exits 0
- [ ] `./ci/run-ci.sh web` prints `PASS — web`
- [ ] `./ci/run-ci.sh e2e` prints `PASS — e2e`
- [ ] `./ci/run-ci.sh` prints `PASS — web api e2e`
- [ ] `git diff --name-only main` lists no file outside the "In scope" list, and in particular
      nothing under `src/Homon.Api/`, `src/Homon.Domain/`, `src/Homon.Infrastructure/` or
      `tests/Homon.Api.Tests/` (D14)
- [ ] `git diff --stat main -- src/Homon.Web/e2e/dashboard-groups.spec.ts src/Homon.Web/e2e/refresh.spec.ts src/Homon.Web/e2e/layout.spec.ts src/Homon.Web/e2e/contrast.spec.ts src/Homon.Web/e2e/helpers.ts`
      prints nothing — the net was not adjusted to fit the change
- [ ] This block prints `OK` four times. Two lines pass by finding nothing and two by finding
      something, so judge the printed words and never the exit codes:
      ```bash
      grep -qE "document\.cookie" src/Homon.Web/src/lib/collapsed-sections.ts && echo "FAIL: this one is localStorage — see D1" || echo "OK"
      grep -q "JSON.parse" src/Homon.Web/src/lib/collapsed-sections.ts && echo "OK" || echo "FAIL: D2 — the stored value is JSON"
      grep -q "removeItem" src/Homon.Web/src/lib/collapsed-sections.ts && echo "OK" || echo "FAIL: D6 — an empty set must remove the key"
      grep -q 'className="sr-only"' src/Homon.Web/src/components/collapsible-section.tsx && echo "FAIL: sr-only text inside the heading" || echo "OK"
      ```
      That last line greps for the **attribute**, not the bare word `sr-only`: the component's own
      doc comment explains why an `sr-only` span is forbidden, and a looser pattern would match
      its own explanation and report a failure that is not there.
      **There is deliberately no grep for D12.** `collapsible-section.tsx` must not *use*
      `role="status"` or `aria-live`, but the comment Step 3 requires you to write names both by
      hand, so every such grep matches the explanation rather than a violation. Confirm D12 by
      reading the JSX.
- [ ] `grep -n "min-h-10" src/Homon.Web/src/components/collapsible-section.tsx` prints a line (D9)
- [ ] `grep -c "^| 018 " plans/README.md` → `1` (the existing row was updated, not duplicated)
- [ ] `grep -c "^### 3\." docs/ARCHITECTURE.md` → `22`. It is `21` at `9d70023`; your new section
      is the twenty-second. If it already printed `22` before you started, another plan took
      `### 3.22` while this one sat unexecuted — take the next free number instead and ignore
      this check.

## STOP conditions

Stop and report — do not improvise — if any of these happen:

- **The drift check is non-empty and the "Current state" excerpts no longer match the live code.**
  This plan was written against `9d70023`; the excerpts are how you confirm you are editing what
  it described.
- **`e2e/dashboard-groups.spec.ts`'s `toHaveText([...])` fails.** The fix is in your markup — the
  `<h2>` is carrying text it should not (an `sr-only` span, a chevron character, a count). Do
  **not** edit that spec's expected array. If you genuinely cannot get the heading's text down to
  the bare section name, stop and report what is adding to it.
- **`e2e/refresh.spec.ts`'s `expectTappable(page, 'button')` fails naming a section heading.**
  The intended fix is `min-h-10 min-w-10` on the button (D9). If that is already present and it
  still fails, report the reported size and which viewport project — do not lower the 40px floor
  in `e2e/helpers.ts`.
- **`e2e/contrast.spec.ts` reports a `color-contrast` violation on the new button.** It inherits
  `text-muted` on `bg`, which is the same pair today's section labels already use and already
  passes. A violation means a colour was changed; revert to inheritance rather than picking a new
  token.
- **A section renders while its button reports `aria-expanded="false"`.** That is D10's failure
  mode: something put a `display` utility on the `hidden` wrapper. Remove the class; do not
  switch to a conditional render without reporting, because `aria-controls` then points at
  nothing.
- **The e2e spec's `1 paused` assertion is flaky or reads a different word.** That means the
  seeded probe is not actually paused — check the `PUT /api/v1/probes/{id}/pause` call landed
  before `page.goto`. Report rather than loosening the assertion to a regex.
- **You find yourself wanting to add a dependency** (a headless disclosure/accordion, a storage
  or cookie wrapper). Stop. A disclosure here is a `<button>` and a `hidden` attribute,
  persistence here is `getItem`/`setItem`, and `src/components/ui/*` is explicitly out of scope.

## Maintenance notes

- **Every future dashboard section must go through `CollapsibleSection`.** Backups (plan 008) and
  the Calendar widget (plan 011) both add sections to this page. When they land they should reuse
  this component and claim a stable id of their own (`backups`, `calendar`) — and add it to
  `knownSectionIds` in `dashboard-page.tsx`, or D7's pruning will quietly forget that section's
  collapsed state on the next toggle of any other section. That list is the one thing in this
  feature a new module has to remember to update; it is worth a comment where it is declared.
- **The id list is the stored value's schema.** Renaming a section id (say `ungrouped` → `other`)
  silently expands that section for every existing reader. Harmless, but do it knowingly.
- **If the stored value ever needs to grow** — a per-section sort order, a pinned section — widen
  the array into an object under the **same** key and let `parseCollapsedSections` accept both
  shapes for a release. D2's defensive parse is exactly what makes that a two-line migration
  instead of a flag day; do not introduce a second key.
- **This is per-browser, not per-household, and that is a decision (D14).** A request to sync the
  arrangement across the family's devices is a server-side feature with a table behind it, not a
  tweak to this module.
- **Watch in review**: any new `<button>` on the dashboard inherits `refresh.spec.ts`'s 40px
  floor, and any new `<h2>` on the dashboard inherits `dashboard-groups.spec.ts`'s exact-text
  assertion. Both are easy to trip and neither failure message says so.
