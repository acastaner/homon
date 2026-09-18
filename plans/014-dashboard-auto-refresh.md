# Plan 014: Make the dashboard's auto-refresh visible, honest and controllable

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If anything under "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> and strike the follow-up line it retires (Step 8).
>
> **Drift check (run first)**:
> ```bash
> git diff --stat dc31f14..HEAD -- \
>   src/Homon.Web/src/lib/status.ts src/Homon.Web/src/lib/links.ts src/Homon.Web/src/lib/pages.ts \
>   src/Homon.Web/src/lib/weather.ts src/Homon.Web/src/main.tsx src/Homon.Web/src/components/app-shell.tsx \
>   src/Homon.Web/src/pages/dashboard-page.tsx docs/ARCHITECTURE.md
> ```
> Empty output means no drift. If any of those files changed, compare the "Current state"
> excerpts below against the live code before proceeding; on a mismatch, treat it as a STOP
> condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW (SPA only — no API, no database, no migration)
- **Depends on**: none (002, 006, 007, 010 and 012 are all DONE; this builds on what they left)
- **Category**: bug / dx
- **Planned at**: commit `dc31f14`, 2026-09-18

## Why this matters

The dashboard already polls `GET /api/v1/status` every 30 seconds, but **nothing on screen
says so**, and in the one case that matters most it silently lies.

TanStack Query pauses a `refetchInterval` while the document is hidden, and
`src/Homon.Web/src/main.tsx:13` additionally sets `refetchOnWindowFocus: false` for every
query in the app. So a browser tab left in the background — the normal state of a household
dashboard on a laptop or a wall-mounted tablet — stops polling, and on return shows arbitrarily
old numbers for up to 30 more seconds with no cue that they are old. The "Checked" column is
worse than merely stale: `formatCheckedAt` computes `now` at render time, so while polling is
paused the column keeps reading "2 min ago" for an hour. A reader looking at a green board
cannot tell a healthy house from a frozen page.

`docs/design-brief.md:229` already specifies the missing cue — a mono `muted` "refreshed 42 s
ago" at the right of the banner — and `plans/README.md` records it as a deliberate
non-delivery from plan 012 ("the banner's `refreshed N s ago` timestamp is not wired —
`Status.generatedAt` exists, but polling it from `AppShell` would add a fetch to every route
including sign-in").

After this plan: the banner shows how old the data is and ticks while you watch, returning to
the tab refetches immediately instead of after up to 30 s, and a Refresh control lets anyone
force the point. The poll interval stays 30 s, hard-coded, as today — the maintainer chose
"fixed, but honest" over a new configuration surface.

## Current state

### The files that matter

- `src/Homon.Web/src/lib/status.ts` — the `/status` wire types, `useStatus()` (the 30 s poll),
  `dashboardSections`, `formatCheckedAt`.
- `src/Homon.Web/src/main.tsx` — the app's single `QueryClient` and its global defaults.
- `src/Homon.Web/src/components/app-shell.tsx` — the banner every route renders inside.
- `src/Homon.Web/src/pages/dashboard-page.tsx` — the only caller of `useStatus()`.
- `src/Homon.Web/src/lib/links.ts`, `src/Homon.Web/src/lib/pages.ts` — `useLinks()` and
  `usePublishedPages()`, used by the dashboard *and* by admin pages.
- `src/Homon.Web/src/components/theme-toggle.tsx` — the exemplar for an icon button in the banner.
- `src/Homon.Web/src/lib/format-uptime.ts` + `format-uptime.test.ts` — the exemplar for a pure
  formatter and its test.

### Excerpts, as they exist at `dc31f14`

`src/Homon.Web/src/lib/status.ts:49-62` — the poll, and the only place the interval lives:

```ts
export const STATUS_QUERY_KEY = ['status'] as const

export function fetchStatus(): Promise<Status> {
  return apiFetch<Status>('/status')
}

/**
 * Polls every 30 seconds — frequent enough that a family glancing at the dashboard sees a
 * state change inside a poll interval or two, infrequent enough that forty open browser tabs
 * on a home network do not turn into a request storm.
 */
export function useStatus() {
  return useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: fetchStatus, refetchInterval: 30_000 })
}
```

`src/Homon.Web/src/main.tsx:9-16` — the global defaults this plan overrides per-query, never globally:

```ts
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
  },
})
```

`src/Homon.Web/src/lib/links.ts:28-30` and `src/Homon.Web/src/lib/pages.ts:48-50` — no interval at all:

```ts
export function useLinks() {
  return useQuery({ queryKey: LINKS_QUERY_KEY, queryFn: fetchLinks })
}
```
```ts
export function usePublishedPages() {
  return useQuery({ queryKey: PAGES_QUERY_KEY, queryFn: fetchPublishedPages })
}
```

`src/Homon.Web/src/components/app-shell.tsx:67-82` — the right-hand end of the banner, where the
new indicator and button go. Note `ml-auto` and that the outer `<header>` is `flex-wrap` with
`gap-y-2`:

```tsx
        <div className="ml-auto flex flex-wrap items-center gap-3 py-1">
          {session.data ? (
            <p className="flex items-center gap-2 text-[13px] text-muted">
              <span>Signed in as {session.data.name}</span>
              <button
                type="button"
                onClick={() => signOut.mutate()}
                disabled={signOut.isPending}
                className="inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50"
              >
                Sign out
              </button>
            </p>
          ) : null}
          <ThemeToggle />
        </div>
```

`src/Homon.Web/src/components/theme-toggle.tsx:15-24` — the icon-button shape to copy verbatim
(the `h-10 w-10` is the 40px tap-target floor `e2e/helpers.ts`'s `expectTappable` enforces, and
the `sr-only` span is how a glyph-only button gets an accessible name in this repo):

```tsx
    <button
      type="button"
      onClick={toggleTheme}
      className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-line text-muted hover:border-line-strong hover:text-text"
    >
      {theme === 'light' ? <Moon aria-hidden="true" size={18} /> : <Sun aria-hidden="true" size={18} />}
      <span className="sr-only">{label}</span>
    </button>
```

### The design constraints this must honour (quoted — you have not read these docs)

`docs/design-brief.md:226-232`, the Banner component rule:

> **Banner.** `surface` background, 1px `line-strong` bottom rule, 56px tall (52px on phone),
> one row at every width. Site name left, then the Site navigation. Nav items fill the banner's
> height, `muted` at rest; the current one is `text` with a 2px `text` underline on the banner's
> bottom edge. **On desktop a mono 12.5px `muted` "refreshed 42 s ago" sits at the right; on
> phone it moves into the page header.** "Signed in as … / Sign out", when present, takes the
> right end of the banner on desktop and the page header row on phone.

`docs/design-brief.md:110-113`, the constraint that outranks the above when they conflict:

> **Keep the landmarks and accessible names.** The tests query `banner`, `navigation` named
> `Site` and `Admin sections`, `main`, `contentinfo`, headings by text, buttons named
> `Sign in` / `Sign out`, labels `Email` / `Password` / `Keep me signed in`, `role="alert"`
> for errors, `role="status"` for the session check. Rename only with the tests.

`CLAUDE.md`, the repo-wide conventions that apply here:

> **Comments carry the reasoning.** Config files, scripts and Dockerfiles explain the
> alternative that was rejected and why. Keep that up; a bare setting is a setting somebody
> will "simplify" back.

> **TypeScript**: kebab-case files, named exports (`App.tsx` is the only default), `@/` alias,
> no MSW — stub `fetch` with `src/test/fetch.ts`. `npm ci`, never `npm install`, in the gate.

## Decisions — implement these exactly; the reasoning is the point

**D1. The age is measured from TanStack's `dataUpdatedAt`, not from `Status.generatedAt`.**
`generatedAt` is the *API server's* clock (`StatusEndpoints.cs` stamps it from `TimeProvider`);
`Date.now()` in the browser is a different clock. On a household machine whose clock has drifted,
`now - generatedAt` reads negative ("refreshed -3 s ago") or minutes wrong. `dataUpdatedAt` is
stamped by the browser when the response lands, so both sides of the subtraction come from one
clock and the number is always truthful about *this browser's* view of freshness. Leave
`generatedAt` in the wire type; this plan does not consume it. Say this in a comment.

**D2. One always-visible instance in the banner — not the brief's banner/page-header pair.**
Rendering it twice behind `hidden`/`sm:flex` is what the brief describes, and it is exactly what
plan 012 refused for "Signed in as …": jsdom applies no stylesheet, so both copies resolve and
every `getByText` / `getByRole` strict-mode query throws on multiple matches. One instance,
always visible, wrapping onto a second line on a phone — the banner's `<header>` is already
`flex-wrap` with `gap-y-2`, and `e2e/layout.spec.ts` asserts no horizontal overflow on every
route at both viewports, so the wrap is covered by the gate rather than by hope.

**D3. `AppShell` subscribes to the status cache but never fetches it.** This is the objection
recorded in `plans/README.md` and it is real: a plain `useStatus()` in `AppShell` would fire a
`/status` request on `/admin/sign-in`, on every admin page, on every page route. Use a *disabled*
observer — `useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: fetchStatus, enabled: false })`.
`enabled: false` suppresses only automatic fetching; the observer still subscribes to the cache
entry and re-renders when `DashboardPage`'s own `useStatus()` writes to it. With no cache entry
(sign-in, first paint) `dataUpdatedAt` is `0` and the indicator renders `null`.

**D4. The Refresh button invalidates every active query, not a hand-maintained key list.**
`queryClient.invalidateQueries()` with no filter refetches exactly what the current route has
mounted — status, links, pages and weather on the dashboard; the admin lists under `/admin` —
so one button is correct on every route and there is no list of query keys to keep in sync as
modules land. State the alternative (invalidating `STATUS_QUERY_KEY` alone) and why it was
rejected, in a comment.

**D5. Focus refetch comes back on for the dashboard's data queries only, never globally.**
`main.tsx`'s global `refetchOnWindowFocus: false` protects the admin forms; do not touch it. Set
`refetchOnWindowFocus: true` on `useStatus`, `useWeather`, and the dashboard's links/pages
queries. The global `staleTime: 30_000` still gates it: returning to the tab inside 30 s issues
no request, returning after 30 s refetches immediately. That gating is the whole reason this is
safe, so say it in a comment.

**D6. `useLinks` and `usePublishedPages` take the interval as an argument; only the dashboard
passes one.** Both hooks are shared with admin pages — `AdminLinksPage` (`admin-links-page.tsx:36`)
reorders rows through `useLinks().data`, and a background refetch landing mid-reorder would
shuffle the list under the administrator's cursor. Give each hook an optional
`{ refetchInterval?: number; refetchOnWindowFocus?: boolean }` argument defaulting to `{}`, and
pass `5 * 60 * 1000` from `DashboardPage` only. Five minutes, not thirty seconds: links and pages
change when an administrator edits them, which is rare, and the Refresh button covers the
impatient case.

**D7. The indicator is not a live region.** It must not carry `role="status"` or any `aria-live`.
The text changes every second; a polite live region would make a screen reader announce
"refreshed 1 s ago", "refreshed 2 s ago" forever. `role="status"` in this app is reserved for the
session check (`src/Homon.Web/src/components/require-administrator.tsx:22`) and the design brief
names it as such. Plain text.

**D8. The 1-second tick lives in a leaf component.** Putting a `setInterval` re-render in
`AppShell` would re-render the entire `<Outlet />` subtree — the whole dashboard, every second.
The interval belongs inside `RefreshIndicator`, which renders nothing but the timestamp and the
button.

## How to read every command in this plan

Two conventions, because getting either wrong will make a correct change look broken:

- **Working directory.** Every command is run from the **repository root** unless it begins with
  an explicit `cd`. `./ci/run-ci.sh` is repo-root only.
- **`grep` exits 1 when it matches nothing.** Several checks below succeed *by finding nothing*.
  A bare `grep` there will look like a failed command. Run those as written — each is wrapped so
  that it prints `OK` or `FAIL` and exits 0 either way. Judge the printed word, never the exit code.

## Commands you will need

`ci/run-ci.sh` is the gate. **Run one suite at a time on this machine** — the full run is
memory-hungry here, and an out-of-memory kill or a `0x80131506` abort is environmental, not a
failure of your change; retry the suite once before treating it as real.

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Install | `cd src/Homon.Web && npm ci` | exit 0 (never `npm install`) |
| Lint | `cd src/Homon.Web && npm run lint` | exit 0 |
| Typecheck + build | `cd src/Homon.Web && npm run build` | exit 0 |
| Unit tests | `cd src/Homon.Web && npm test` | exit 0, 0 failures |
| One unit file | `cd src/Homon.Web && npx vitest run src/lib/format-refreshed.test.ts` | exit 0 |
| Web gate | `./ci/run-ci.sh web` | `PASS — web` |
| E2E gate | `./ci/run-ci.sh e2e` | `PASS — e2e` |
| Full gate (last) | `./ci/run-ci.sh` | `PASS — web api e2e` |

The API and database are untouched by this plan, so `./ci/run-ci.sh api` should be unchanged;
run it once at the end to confirm you did not stray.

## Scope

**In scope** (the only files you may modify or create):

- `src/Homon.Web/src/lib/format-refreshed.ts` (create)
- `src/Homon.Web/src/lib/format-refreshed.test.ts` (create)
- `src/Homon.Web/src/components/refresh-indicator.tsx` (create)
- `src/Homon.Web/src/components/refresh-indicator.test.tsx` (create)
- `src/Homon.Web/src/components/app-shell.tsx`
- `src/Homon.Web/src/lib/status.ts`
- `src/Homon.Web/src/lib/links.ts`
- `src/Homon.Web/src/lib/pages.ts`
- `src/Homon.Web/src/lib/weather.ts`
- `src/Homon.Web/src/pages/dashboard-page.tsx`
- `src/Homon.Web/src/App.test.tsx`
- `src/Homon.Web/e2e/refresh.spec.ts` (create)
- `docs/ARCHITECTURE.md` (append one new `### 3.N` section — Step 7)
- `plans/README.md` (status row + retire one follow-up line — Step 8)

**Out of scope — do NOT touch, even though they look related:**

- `src/Homon.Web/src/main.tsx`. The global `refetchOnWindowFocus: false` and
  `staleTime: 30_000` stay exactly as they are; D5 depends on that `staleTime` being the gate,
  and flipping the global default would make every admin form refetch under the user's hands.
- Anything under `src/Homon.Api/`, `src/Homon.Domain/`, `src/Homon.Infrastructure/` or
  `tests/Homon.Api.Tests/`. There is no API change in this plan. `Status.generatedAt` already
  exists on the wire and stays unused (D1).
- `ProbeScheduler` / `MonitoringOptions`. The *server's* poll cadence is a separate knob and is
  already correct; this plan is about the browser's view of it.
- `src/Homon.Web/src/lib/format-uptime.ts`, `src/Homon.Web/src/components/sparkline.tsx`,
  `status-chip.tsx` — read them as exemplars, change nothing.
- `formatCheckedAt` in `status.ts` and the dashboard's `const now = new Date()`. Once polling is
  honest the column refreshes on every poll, which is enough; making it tick independently is a
  separate change and would touch `dashboard-page.test.tsx`'s existing assertions.
- `src/Homon.Web/src/components/ui/*` (shadcn primitives — committed but unused by every page;
  adopting them is its own plan).
- `docs/design-brief.md`. It is the spec; D2 documents the deviation in `ARCHITECTURE.md`
  instead, which is where this repo records decisions.

## Git workflow

- Make the worktree yourself from **local `main`**, not `origin/main`:
  `git worktree add ../homon-014 -b plan/014-dashboard-auto-refresh main`
- Commit per step or per logical unit. Message style, from `git log`:
  `Web: show validation problems' field messages instead of the generic fallback`,
  `Design: strengthen the theme e2e assertions and fix a contrast-spec leak (plan 012)`
  — an area prefix, a colon, an imperative sentence, the plan number in parentheses where it
  clarifies. Use `Dashboard:` as the prefix here.
- Do **not** push or open a PR unless the operator asks.

## Steps

### Step 1: The pure formatter

Create `src/Homon.Web/src/lib/format-refreshed.ts`, modelled on `src/Homon.Web/src/lib/format-uptime.ts`
(one exported function, a doc comment naming the design-brief rule it implements, no imports).

```ts
/**
 * `refreshed 42 s ago` — docs/design-brief.md's Banner rule. Seconds under a minute, whole
 * minutes under an hour, whole hours beyond.
 *
 * `elapsedMs` is measured browser-clock to browser-clock (TanStack's `dataUpdatedAt` against
 * `Date.now()`), never against the API's `Status.generatedAt`: those are two different clocks,
 * and a drifted household machine would otherwise render "refreshed -3 s ago". A negative
 * elapsed value is still clamped to 0 here, because a clock stepped backwards mid-session
 * (NTP correction, a laptop waking) can produce one from the same clock.
 */
export function formatRefreshed(elapsedMs: number): string {
  const seconds = Math.max(0, Math.floor(elapsedMs / 1000))

  if (seconds < 60) {
    return `refreshed ${String(seconds)} s ago`
  }

  const minutes = Math.floor(seconds / 60)

  if (minutes < 60) {
    return `refreshed ${String(minutes)} min ago`
  }

  return `refreshed ${String(Math.floor(minutes / 60))} h ago`
}
```

Create `src/Homon.Web/src/lib/format-refreshed.test.ts`, modelled on `format-uptime.test.ts`
(`import { describe, expect, it } from 'vitest'`, `@/` alias import, no providers needed).
Write **three** `it()` blocks, grouping cases the way the exemplar groups its three assertions into
one `it` — not one block per case, and not table-driven:

- `it('formats seconds under a minute', ...)` — `0` → `refreshed 0 s ago`; `42_000` →
  `refreshed 42 s ago`; `59_999` → `refreshed 59 s ago`.
- `it('rolls up to minutes, then hours', ...)` — `60_000` → `refreshed 1 min ago`; `3_599_000` →
  `refreshed 59 min ago`; `3_600_000` → `refreshed 1 h ago`.
- `it('clamps a clock that stepped backwards', ...)` — `-5_000` → `refreshed 0 s ago`.

**Verify**: `cd src/Homon.Web && npx vitest run src/lib/format-refreshed.test.ts` → exit 0, `3 passed`.

### Step 2: Turn focus refetch back on for the dashboard's queries

In `src/Homon.Web/src/lib/status.ts`, change `useStatus()` to add `refetchOnWindowFocus: true`,
and extend the existing doc comment to explain *why* it is needed here when `main.tsx` turns it
off globally (D5 — quote the mechanism: TanStack pauses `refetchInterval` while the document is
hidden, and the global `staleTime: 30_000` means a return inside 30 s still issues no request).

In `src/Homon.Web/src/lib/weather.ts`, add `refetchOnWindowFocus: true` to `useWeather()`,
with a one-line comment pointing at `useStatus`'s reasoning.

Do **not** edit `main.tsx`.

**Do not write a unit test for this step, and reject one if you are tempted.** It would pass for the
wrong reason. `src/Homon.Web/src/test/render.tsx` builds its own `QueryClient` with only
`{ queries: { retry: false } }` — it does **not** mirror `main.tsx`'s
`refetchOnWindowFocus: false`, so in the unit environment that option already sits at its library
default of `true`. A test that drives `focusManager.setFocused(false)` then `setFocused(true)` and
asserts a refetch therefore goes green whether or not you made this change, which is worse than no
test at all. Making it meaningful would mean changing `render.tsx` to mirror `main.tsx`, and
`render.tsx` is out of scope — its own comment warns that its provider nesting is load-bearing.
This step's guarantee is covered by the real browser in Step 7 and by review of the diff.

**Verify**: `cd src/Homon.Web && npm run build` → exit 0.
**Verify**: `grep -n "refetchOnWindowFocus" src/Homon.Web/src/main.tsx` → prints exactly one line, and it reads `refetchOnWindowFocus: false,`.

### Step 3: Give the links and pages hooks an optional interval

In `src/Homon.Web/src/lib/links.ts`, change `useLinks()` to:

```ts
/**
 * Takes its polling options from the caller rather than setting them here, because
 * `AdminLinksPage` shares this hook and reorders rows straight out of `data`: a background
 * refetch landing mid-reorder would shuffle the list under the administrator's cursor. The
 * dashboard passes an interval; the admin page passes nothing and keeps today's fetch-once
 * behaviour.
 */
export function useLinks(options: { refetchInterval?: number; refetchOnWindowFocus?: boolean } = {}) {
  return useQuery({ queryKey: LINKS_QUERY_KEY, queryFn: fetchLinks, ...options })
}
```

Do the same for `usePublishedPages()` in `src/Homon.Web/src/lib/pages.ts` — write the option type
inline there too. Do **not** extract a shared named type and import it across the two modules: two
optional fields do not justify making `pages.ts` depend on `links.ts`, and this repo has no
`lib/types.ts` to put it in.

Leave `useAdminPages()` and `usePage()` alone.

**Verify**: `cd src/Homon.Web && npm run build && npm run lint` → both exit 0.
**Verify**: `grep -n "useLinks()" src/Homon.Web/src/pages/admin-links-page.tsx` → still one match, unchanged (the default argument keeps the call site valid).

### Step 4: Pass the interval from the dashboard

In `src/Homon.Web/src/pages/dashboard-page.tsx:115-116`, change the two calls:

```tsx
  // Five minutes, not the status board's thirty seconds: links and pages change only when an
  // administrator edits them. The banner's Refresh button covers the impatient case.
  const { data: links = [] } = useLinks({ refetchInterval: 5 * 60 * 1000, refetchOnWindowFocus: true })
  const { data: pages = [] } = usePublishedPages({ refetchInterval: 5 * 60 * 1000, refetchOnWindowFocus: true })
```

Change nothing else in this file.

**Verify**: `cd src/Homon.Web && npm run build` → exit 0.
**Verify**: `cd src/Homon.Web && npx vitest run src/pages/dashboard-page.test.tsx` → exit 0, all existing tests still pass.

### Step 5: The `RefreshIndicator` component

Create `src/Homon.Web/src/components/refresh-indicator.tsx`. The shape below is the target; match
it. Every element of it is load-bearing and the reason is given.

```tsx
import { useEffect, useState } from 'react'
import { RotateCw } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'

import { formatRefreshed } from '@/lib/format-refreshed'
import { fetchStatus, STATUS_QUERY_KEY } from '@/lib/status'

export function RefreshIndicator() {
  // A DISABLED observer: it subscribes to the ['status'] cache entry and re-renders when
  // DashboardPage's own useStatus() writes to it, but `enabled: false` means it never issues a
  // request of its own. That is the whole reason this can live in AppShell — a plain useStatus()
  // here would fetch /status on /admin/sign-in and on every admin route, which is exactly why
  // plan 012 left this timestamp unwired.
  const { dataUpdatedAt } = useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: fetchStatus, enabled: false })
  const queryClient = useQueryClient()

  // Re-read the wall clock every second so the text counts up between polls; without it the
  // banner would freeze at "refreshed 0 s ago" for the whole 30 s.
  //
  // The interval lives in THIS leaf component, not in AppShell, on purpose: a state update in
  // AppShell re-renders its <Outlet /> — the entire dashboard — once a second.
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(id)
  }, [])

  return (
    <div className="flex items-center gap-2.5">
      {/* dataUpdatedAt is 0 until something has populated the cache — on /admin/sign-in, and on
          the dashboard's very first paint. No timestamp then, rather than "refreshed 57 years ago".

          Deliberately NOT role="status" or aria-live: the text changes every second, and a live
          region would make a screen reader announce every tick forever. role="status" in this app
          belongs to the session check (components/require-administrator.tsx). */}
      {dataUpdatedAt === 0 ? null : (
        <span className="mono text-[12.5px] text-muted">{formatRefreshed(now - dataUpdatedAt)}</span>
      )}
      <button
        type="button"
        // No filter, deliberately: this refetches whatever the CURRENT route has mounted —
        // status, links, pages and weather on the dashboard; the admin lists under /admin — so one
        // button is correct everywhere and there is no list of query keys to keep in sync as
        // further modules land. Invalidating STATUS_QUERY_KEY alone was the alternative; it would
        // make the button a no-op on every admin page.
        onClick={() => void queryClient.invalidateQueries()}
        // h-10 w-10 is the 40px tap-target floor e2e/helpers.ts's expectTappable enforces over
        // every <button> on the page. Not cosmetic; do not shrink it.
        className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-line text-muted hover:border-line-strong hover:text-text"
      >
        <RotateCw aria-hidden="true" size={18} />
        <span className="sr-only">Refresh</span>
      </button>
    </div>
  )
}
```

Three points the code above settles, so you do not have to decide them:

- **The button has no pending/disabled state.** `invalidateQueries` is not a mutation and exposes
  no `isPending`; the feedback a reader gets is the timestamp snapping back to "refreshed 0 s ago".
  Do not add a spinner or a `disabled` attribute — `signOut`'s `disabled={signOut.isPending}` in
  `app-shell.tsx` is a mutation and is not the pattern to copy here.
- **The button renders on every route, the timestamp does not.** On `/admin/sign-in` the button is
  present and refreshes the session/meta queries; that is harmless and keeps one rule instead of two.
- **`RotateCw` is the icon** — verified present in `lucide-react` 1.46.0. `RefreshCw` and `RotateCcw`
  also exist; use `RotateCw`.

Now create `src/Homon.Web/src/components/refresh-indicator.test.tsx`. Model its shape on
`src/Homon.Web/src/components/theme-toggle.test.tsx` — same imports, and `userEvent.setup()` inside
each test that clicks (that file's line 3 and line 27 are the exemplar). Use `renderWithProviders`
from `@/test/render` and `stubFetch` from `@/test/fetch`; this repo uses no MSW.

**Read this before writing tests 4 and 5.** `renderWithProviders` creates its `QueryClient`
*internally* and does not return it, so a test **cannot** pre-seed the status cache. The only way to
get data into that cache is to render something that fetches it. That is why tests 4 and 5 render
`DashboardPage` alongside `RefreshIndicator` in a single element — one `renderWithProviders` call
means one `QueryClient`, which is what makes the cache shared:

```tsx
renderWithProviders(
  <>
    <DashboardPage />
    <RefreshIndicator />
  </>,
)
```

Do **not** modify `src/test/render.tsx` to expose the client; it is out of scope.

The five tests:

1. **It issues no request of its own.** `const calls = stubFetch({})` — an empty route table, and
   `stubFetch` throws on any unmatched path. Render `<RefreshIndicator />` alone, then
   `expect(calls).toHaveLength(0)`. This is the test that protects D3: without it, a later
   "simplification" to `useStatus()` would pass every other test silently.
2. **The Refresh button renders with no cached data.**
   `expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()`.
3. **No timestamp with no cached data.**
   `expect(screen.queryByText(/refreshed/)).not.toBeInTheDocument()`.
4. **The timestamp appears once the status cache holds data.** Use the two-component arrangement
   above with a `stubFetch` route table copied from `dashboard-page.test.tsx`'s
   `statusWithASharedProbe` (it declares `/api/v1/status`, `/api/v1/links`, `/api/v1/pages` and
   `/api/v1/weather` — all four are needed or `DashboardPage` will throw on an unmatched path).
   Then `expect(await screen.findByText(/refreshed \d+ s ago/)).toBeInTheDocument()`.
5. **Clicking Refresh refetches.** Same arrangement. Await the timestamp first (so the first fetch
   has landed), count the `/api/v1/status` entries in `calls`, click
   `screen.getByRole('button', { name: 'Refresh' })` with `userEvent`, then `await waitFor(...)`
   until that count has increased. Count the entries rather than asserting an absolute number —
   `DashboardPage` fetches four endpoints and StrictMode may double-invoke.

**Verify**: `cd src/Homon.Web && npx vitest run src/components/refresh-indicator.test.tsx` → exit 0, 5 tests pass.

### Step 6: Mount it in the banner

In `src/Homon.Web/src/components/app-shell.tsx`, import `RefreshIndicator` and render it inside the
existing `<div className="ml-auto flex flex-wrap items-center gap-3 py-1">`, **before** the
`session.data` block, so the order left-to-right is: refresh indicator, "Signed in as … / Sign out",
theme toggle.

Extend the file's existing doc comment with a paragraph recording D2 — one always-visible instance
rather than the brief's banner/page-header pair, for the same jsdom reason the comment already gives
for "Signed in as …". Do not change the `<header>`'s classes; it is already `flex-wrap` with
`gap-y-2` and will wrap on a phone.

`src/Homon.Web/src/App.test.tsx`'s `anonymous` stub route table declares no `/api/v1/status` and no
`/api/v1/links`, and `stubFetch` throws on undeclared paths. Because `RefreshIndicator` is
`enabled: false` it adds no request, so those tests must still pass **unmodified** — that is the
check that D3 actually holds end to end. Add one test to `App.test.tsx` asserting the banner carries
a button named `Refresh` on the sign-in route (`initialEntries: ['/admin/sign-in']`), and confirm you
did not have to add `/api/v1/status` to the `anonymous` table to make anything pass.

**Verify**: `cd src/Homon.Web && npx vitest run src/App.test.tsx` → exit 0, all tests pass.
**Verify** (this one succeeds by finding nothing — read the printed word, not the exit code):
```bash
git diff -- src/Homon.Web/src/App.test.tsx | grep -q "api/v1/status" && echo "FAIL: D3 broken" || echo "OK"
```
→ prints `OK`. `FAIL` means you added a `/api/v1/status` route to the stub table to make something
pass, which is the one fix this step forbids; see STOP conditions.
**Verify**: `./ci/run-ci.sh web` → `PASS — web`.

### Step 7: The end-to-end spec, and the architecture note

Create `src/Homon.Web/e2e/refresh.spec.ts`, modelled on `src/Homon.Web/e2e/theme.spec.ts` (same
imports, same `test.describe` shape). It runs at both viewport projects automatically. Cover:

1. **The dashboard shows its age.** `page.goto('/')`, then
   `await expect(page.getByText(/refreshed \d+ s ago/)).toBeVisible()`.
2. **The Refresh button is there and is tappable at both viewports.** Import `expectTappable` from
   `./helpers` and call `await expectTappable(page, 'button')` on `/` — this is the 40px floor, and
   running it on `/` (not only the admin routes `layout.spec.ts` covers) is the point, because the
   banner is now where a reader-facing button lives.
3. **Clicking Refresh issues a new `GET /status`.** Use
   `const response = page.waitForResponse((r) => r.url().includes('/api/v1/status'))` *before* the
   click, click `page.getByRole('button', { name: 'Refresh' })`, then `await response` and assert
   `.ok()`.

Then append a new section to `docs/ARCHITECTURE.md`. Find the number first —
`grep -n "^### 3\." docs/ARCHITECTURE.md | tail -1` (it is `### 3.19` at `dc31f14`; take the next
one, do not hard-code `3.20` if the grep says otherwise). Title it along the lines of
**"The dashboard's freshness is the browser's clock, and the banner says so"**, and record, in the
voice of the surrounding sections: D1 (why `dataUpdatedAt` and not `generatedAt`), D2 (the deviation
from the brief's banner/page-header pair and the jsdom reason), D3 (the disabled observer and the
fetch it avoids), D5 (per-query focus refetch, gated by the global `staleTime`), and that the
interval stays a hard-coded 30 s in `status.ts` — the maintainer chose that over a configuration
surface on 2026-09-18.

**Verify**: `./ci/run-ci.sh e2e` → `PASS — e2e`.

### Step 8: Reconcile the index

`plans/README.md` **already contains a 014 row and already references this plan** — it was updated
when the plan was written. You are editing what is there, not adding to it. Read the file first.

1. The plan table already has a row reading
   `| 014 | planned | M | — | Dashboard auto-refresh: … |`. **Change its status cell** from
   `planned` to `DONE (<today's date>, <the short SHA of your final commit>)`. Do not add a second
   014 row.
2. Under **Cross-plan follow-ups**, the "refreshed N s ago" bullet currently ends with
   `(**claimed by plan 014**, which wires it from a disabled cache observer instead);`. Rewrite that
   whole bullet in the past tense — it is no longer a follow-up: plan 014 wired the timestamp from a
   disabled cache observer in `AppShell`, and `Status.generatedAt` stays deliberately unconsumed
   (the browser's own `dataUpdatedAt` is the clock, per `docs/ARCHITECTURE.md`'s new section). The
   words "not wired" must not survive.
3. Leave the **014 depends on nothing** bullet under "Dependency notes" in place, and leave every
   other follow-up bullet alone.

**Verify** (succeeds by finding nothing — read the printed word, not the exit code):
```bash
grep -q "not wired" plans/README.md && echo "FAIL: follow-up not retired" || echo "OK"
grep -c "^| 014 " plans/README.md
```
→ prints `OK`, then `1`.

## Test plan

| File | New tests | Pattern to follow |
| --- | --- | --- |
| `src/lib/format-refreshed.test.ts` (new) | 3 `it()` blocks covering 7 boundary cases — see Step 1 for the exact grouping | `src/lib/format-uptime.test.ts` |
| `src/components/refresh-indicator.test.tsx` (new) | 5: issues no request; button renders with no cache; no timestamp with no cache; timestamp appears once cached; click refetches | `src/components/theme-toggle.test.tsx` for shape, `src/pages/dashboard-page.test.tsx` for the `stubFetch` route table |
| `src/App.test.tsx` | 1: the banner carries a `Refresh` button on `/admin/sign-in` | the file's own existing tests |
| `e2e/refresh.spec.ts` (new) | 3: the age is visible on `/`; every button on `/` clears 40px; clicking Refresh produces a 2xx `/api/v1/status` | `e2e/theme.spec.ts` |

**Nothing unit-tests Step 2's focus refetch, on purpose.** `src/test/render.tsx`'s `QueryClient`
does not mirror `main.tsx`'s `refetchOnWindowFocus: false`, so such a test passes whether or not the
change was made — see Step 2. The e2e suite's real browser is what covers it. Do not "fix" this gap.

The existing suites are the real net here — every one of them must pass **unmodified** except
`App.test.tsx`, which gains a test and loses nothing. In particular `dashboard-page.test.tsx`'s
whole-string `getByText` on the stat strip and `e2e/layout.spec.ts`'s no-horizontal-overflow checks
are the assertions most likely to catch a mistake in Steps 4 and 6.

## Done criteria

All of these must hold:

- [ ] `cd src/Homon.Web && npm run lint` exits 0
- [ ] `cd src/Homon.Web && npm run build` exits 0
- [ ] `./ci/run-ci.sh web` prints `PASS — web`
- [ ] `./ci/run-ci.sh e2e` prints `PASS — e2e`
- [ ] `./ci/run-ci.sh` prints `PASS — web api e2e`
- [ ] `grep -n "refetchOnWindowFocus" src/Homon.Web/src/main.tsx` prints exactly one line, reading
      `refetchOnWindowFocus: false,`
- [ ] `git diff --name-only main` lists no file outside the "In scope" list
- [ ] This block prints `OK` four times — every line succeeds by finding nothing, so judge the
      printed words and ignore the exit codes:
      ```bash
      git diff main -- src/Homon.Web/src/App.test.tsx | grep -q "api/v1/status" && echo "FAIL: App.test.tsx stub was widened" || echo "OK"
      grep -rln "generatedAt" src/Homon.Web/src --include=*.tsx | grep -qv "\.test\.tsx" && echo "FAIL: a component reads generatedAt" || echo "OK"
      grep -q "not wired" plans/README.md && echo "FAIL: follow-up not retired" || echo "OK"
      grep -rq "DashboardQueryOptions" src/Homon.Web/src && echo "FAIL: shared type was extracted" || echo "OK"
      ```
      (The `generatedAt` line excludes `*.test.tsx` on purpose: test fixtures legitimately carry
      `generatedAt` because it is on the wire — `dashboard-page.test.tsx` already did at `dc31f14`.
      D1 is about no *component* reading it. There is deliberately no grep for D7. `refresh-indicator.tsx` must not *use* `role="status"`
      or `aria-live`, but the comment Step 5 requires you to write mentions both by name, so any
      such grep matches its own explanation. Confirm D7 by reading the JSX.)
- [ ] `grep -c "^| 014 " plans/README.md` → `1` (the existing row was updated, not duplicated)
- [ ] `grep -c "^### 3\." docs/ARCHITECTURE.md` → `20`. It is `19` at `dc31f14`; your new section is
      the twentieth. If it already printed `20` before you started, another plan took `### 3.20`
      while this one sat unexecuted — take the next free number instead and ignore this check.

## STOP conditions

Stop and report — do not improvise — if any of these happen:

- **The drift check is non-empty and the "Current state" excerpts no longer match the live code.**
  This plan was written against `dc31f14`; the excerpts are how you confirm you are editing what it
  described.
- **`enabled: false` does not behave as D3 assumes** — i.e. `RefreshIndicator` fires a `/status`
  request in test 1 of Step 5, or `App.test.tsx` starts failing with `Unexpected fetch:
  /api/v1/status`. Do **not** fix this by adding `/api/v1/status` to `App.test.tsx`'s stub table:
  that hides the regression the test exists to catch. Report the TanStack version
  (`@tanstack/react-query` is `^5.102.8` in `package.json`) and what you observed.
- **The banner overflows at the Pixel 7 viewport** — `./ci/run-ci.sh e2e` fails
  `layout.spec.ts` with a horizontal-overflow message naming the banner. The intended fix is the
  existing `flex-wrap` letting the row wrap; if it does not, report rather than inventing a
  responsive `hidden`/`sm:flex` pair, which D2 explicitly rejects.
- **A verification fails twice** after one reasonable fix attempt — except an out-of-memory kill or
  a `0x80131506` abort from `./ci/run-ci.sh`, which are environmental on this machine. Retry the
  suite once; only a reproducible failure counts.
- **A fix appears to require editing `src/Homon.Web/src/main.tsx`** or anything under `src/Homon.Api/`,
  `src/Homon.Domain/`, `src/Homon.Infrastructure/`. There is no API change in this plan.
- **The `e2e` run cannot reach `/api/v1/status` anonymously.** The dashboard already renders status
  sections for an anonymous reader in the current suite, so this would mean an auth change landed
  that this plan did not account for.

## Maintenance notes

For whoever owns this next:

- **`Status.generatedAt` is now dead weight on the wire, deliberately.** It is still the honest
  server-side answer to "when was this computed", and a future server-push or multi-instance
  deployment would want it. Do not delete it because nothing reads it; the `ARCHITECTURE.md`
  section from Step 7 is the record of why.
- **The interval is still one number in one place** — `refetchInterval: 30_000` in
  `src/Homon.Web/src/lib/status.ts`. If a household ever needs it configurable, the cheapest route
  is `GET /api/v1/meta` reporting it from `MonitoringOptions` (environment-configurable, matching
  every other Monitoring knob), not a database singleton and an admin page. That was weighed on
  2026-09-18 and deferred.
- **The Refresh button's unfiltered `invalidateQueries()` is a feature, not laziness.** Every module
  plan that lands a new query (004, 005, 008, 009, 011) gets refreshed by it for free. Resist a
  future PR that "tightens" it to a key list.
- **Anything a reviewer should scrutinise**: that `main.tsx` is untouched; that no second copy of the
  indicator was introduced behind a responsive class (D2); that `refresh-indicator.tsx` still uses
  `enabled: false` and not `useStatus()`; and that the new banner button is still `h-10 w-10`, since
  `e2e/layout.spec.ts` sweeps every `button` on the admin routes at 40px.
- **Deferred out of this plan, on purpose**: making the dashboard's "Checked" column tick
  independently of the poll (it would touch `dashboard-page.test.tsx`'s existing assertions), and
  server-push via SSE (weighed on 2026-09-18 and rejected — new auth, nginx buffering and reconnect
  surface for a home dashboard where 30 s is already plenty, and plan 009 already claims the
  `ProbeTransition` channel an SSE endpoint would want).
