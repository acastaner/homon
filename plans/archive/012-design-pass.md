# 012 — Design pass: Status board, dark by default

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving to the next step. If anything in "STOP
> conditions" occurs, stop and report — do not improvise. When done, update this plan's row
> in `plans/README.md`.
>
> **Re-inventory before you start.** This plan was written against `docs/design-brief.md`
> and `docs/design/` as they stand at the commit below, and against the *shapes* this
> session's other five plans (013, 002, 003, 006, 007, 010) say they will leave (those plans
> had not executed when this one was written). Before Step A1, re-read every
> `src/Homon.Web/src/pages/*.tsx` file, `App.tsx` and `e2e/helpers.ts` as they actually
> landed, and update the "Current state" inventory with what you find — headings, section ids
> and accessible names are load-bearing, and a plan executed out of order or with local
> deviations is expected, not an error, as long as those contracts held.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- docs/design-brief.md
> docs/design/`. This session's other plans legitimately change nearly every file under
> `src/Homon.Web/` — that is not what this check is for. It exists to catch the one thing
> that *would* be a
> problem: the design brief or artboards themselves changing underneath this plan between
> when it was written and when it runs. If this diff is non-empty, read what changed before
> proceeding; a design-guideline edit made by a module plan (006's `rel` fix, 007's Pages
> note, 010's empty-state wording) is expected and listed in their own plans — cross-check
> against those before treating it as drift in *this* plan's sense. Plans 004, 005, 008, 009
> and 011 do not run this session (see Status), so nothing of theirs should appear in the
> diff at all; if one of their files does show up, someone ran a plan out of the session's
> order — STOP and report rather than styling markup this plan was not scoped for.

## Status

- **Priority**: P1 (the repository README and CLAUDE.md both point here as the reason the
  SPA still looks unstyled)
- **Effort**: L
- **Risk**: LOW for Slice A (additive, no existing markup touched); MED for slices C–E (every
  page's markup gains `className` and must not change an accessible name, landmark role, or
  heading text the test suite already asserts on)
- **Depends on**: `plans/013`, `plans/002`, `plans/003`, `plans/006`, `plans/007`,
  `plans/010` — the six plans that land in this session, in that order (Slices B–F style
  what they built). **Slice A depends on `plans/001` only** and is independently shippable
  at any time — see "Sequencing" below. Plans 004 (SMB), 005 (SNMP), 008 (Backups + API-key
  admin), 009 (Alerts) and 011 (Calendar) do **not** land this session — every reference to
  them below is marked "deferred" and styles nothing; see Maintenance notes for what their
  own executors reuse from this plan when they eventually run.
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (review-plan; styles only what landed: 002, 003, 006, 007, 010;
  execution order 013 → 002 → 003 → 006 → 007 → 010 → 012)

## Why this matters

Phase 0 deliberately shipped zero style (`CLAUDE.md`: "There is deliberately no style yet");
by the time this session's other five module plans land (013, 002, 003, 006, 007, 010),
every one of *their* admin and reader surfaces exists but is bare HTML. `docs/design-
brief.md` and `docs/design/` fix the target — a dark-by-default "Status board" — down to
exact oklch values, a type ramp and per-component pixel rules, but nothing in the repository
yet reads a token, ships a font, or renders a themed pixel. This plan is what makes
`docs/ARCHITECTURE.md` §3.10's "must keep surviving [style]" claim true: it wires the tokens,
the theme, and every component the brief describes onto the markup this session's other
plans already built, without changing one accessible name the test suite depends on. Plans
004, 005, 008, 009 and 011 are out of scope entirely (see Status) — their surfaces stay
unstyled until they land and reuse what this plan ships.

## Context — read before writing any CSS

This plan does not restate `docs/design-brief.md`; it tells you which parts of it apply to
which step. **Read the brief's "Design guidelines" section in full before Slice A** — Tone,
Schemes, Colour tokens (the oklch table and the shadcn variable mapping), Type, Space and
shape, Components, Not drawn yet. The Colour tokens table especially is **not** reproduced
here — open the file and copy the oklch values verbatim; a transcription error in this plan
would propagate to every dashboard.

**Line numbers into `docs/design-brief.md` throughout this plan were correct at `f4e7261` and
may have drifted**: 002's own Slice 9 edits the "Screens" section (Dashboard, Admin bullets)
*above* "Design guidelines" in the same file, and 006 edits one word inside "Design
guidelines" itself (`rel="noopener"` → `rel="noopener noreferrer"`) — both shift every line
number below their edit. Treat every `docs/design-brief.md:NNN` citation below as "was here,
confirm by heading/section text" rather than "is exactly here" — the drift check at the top of
this plan already tells you to read what changed if it is non-empty; this is what to do with
that information for citations specifically, not just content.

`docs/ARCHITECTURE.md` §3.10 (Phase 0 ships no style — tests query by role and name) and
§3.12 (the Status board decision: dark default, warm-paper light behind an explicit
remembered choice, the theme bootstrap must be an external script because the CSP is
`default-src 'self'`) are the decision record this plan executes — **locate both by
`grep -n '^### 3\.1[02]' docs/ARCHITECTURE.md`, not by line number**: plan 013 (which runs
before this session's 002) inserts one sentence into §3.3, and 002/003 append §3.13 onward
after §3.12, so every line number after §3.3 has moved since this plan was written.
`docs/design/README.md` explains the artboard format and how to preview a scheme without the
canvas (`sed 's/class="root {{themeClass}}"/class="root light"/' … && npx playwright
screenshot …`) — useful for eyeballing a step's output against the artboard.

## Sequencing decision

**This plan runs last in this session, after 013, 002, 003, 006, 007 and 010, in that
order.** Justification: every dashboard section, every admin table and every form field this
plan styles is markup another plan writes; styling ahead of that markup means guessing its
final shape (accessible names, section ids, row structure) and re-touching it once the real
markup lands — exactly the churn each of those plans' own drift-check preambles is written to
avoid causing *each other*. Running last also means Slices C–F touch each page's markup
exactly once. Plans 004, 005, 008, 009 and 011 are not part of this session (see Status) —
this plan styles nothing of theirs; a later session runs them against this plan's already-
landed primitives (Maintenance notes).

**If the maintainer wants the foundation earlier** (tokens, fonts, the theme toggle, the
shell), **Slice A is separable and depends only on plan 001.** It adds `@theme` tokens, the
font packages, the theme bootstrap script and a working toggle to the *existing* unstyled
shell (`AppShell`, `SignInPage`, `RequireAdministrator`, `DashboardPage`'s two Phase-0
sections) without touching any page a later module plan will still rewrite. A maintainer who
wants dark mode and real fonts visible before the rest of this session lands can execute
Slice A on its own, any time after 001, and the later slices attach to whatever markup exists
by then. Say so in your summary if you run Slice A independently rather than as part of the
full sequence.

## Decisions

**1. Tokens live in `src/index.css` as `@theme`, dark as the bare `:root`, light under an
explicit selector — settled prefix and mechanism, not left for the executor to guess.**
Tailwind v4's `@theme` directive is how `src/index.css:7`'s `@import 'tailwindcss'` becomes
real utility classes; there is no `tailwind.config.js` (`components.json:7` — `"config": ""`).
The artboards put dark on the bare `.root` class and light on `.root.light`
(`docs/design/dashboard/Main.dc.html:14-27`) — mirror that in the app: dark values inside
`:root`, the same names' light values inside `[data-theme="light"] { … }` on `<html>`.
`data-theme`, not a class, because the theme bootstrap script (Decision 3) sets it before
React mounts.

The prefix is `--color-` (confirmed against `tailwindcss@4.3.3`, `package.json`'s pinned
version): a variable named `--color-bg` inside `@theme` both becomes the CSS custom property
Tailwind emits at the document root *and* generates the utilities `bg-bg`/`text-bg`/
`border-bg` (Tailwind always compiles a colour utility as `background-color: var(--color-bg)`
etc — never a baked-in literal — so the *same* property name re-declared under
`[data-theme="light"]` outside `@theme` is enough to flip every utility; you do not need a
second `@theme` block, and Tailwind would ignore one if you wrote it). This is the whole
mechanism, concretely:

```css
@import 'tailwindcss';

/* Design tokens. Dark is the resident scheme — literal oklch values here become both the
   CSS custom properties (`--color-bg`, …) and the utilities that read them (`bg-bg`,
   `text-text`, `border-line`, …). See docs/design-brief.md's colour token table for values —
   copy them verbatim from the file, this is only the shape. */
@theme {
  --color-bg: oklch(23.1% 0.004 84.6);
  --color-surface: oklch(26.1% 0.006 78.2);
  --color-line: oklch(33.8% 0.008 75.3);
  --color-line-strong: oklch(40.7% 0.011 78.2);
  --color-text: oklch(93.2% 0.012 84.6);
  --color-muted: oklch(69.8% 0.017 82.8);
  --color-up: oklch(69.5% 0.181 145.6);
  --color-unstable: oklch(72.7% 0.153 52.8);
  --color-down: oklch(68.5% 0.203 27.0);
  --color-unknown: oklch(64.0% 0.020 86.2);
  --color-unstable-bg: oklch(30.4% 0.037 57.2);
  --color-down-bg: oklch(29.0% 0.045 20.2);
  --color-source-1: oklch(62.7% 0.150 253.4);
  --color-source-2: oklch(63.5% 0.124 76.3);
  --color-source-3: oklch(66.3% 0.161 303.2);
}

/* Same property names, light values — a plain override outside @theme, not a second
   @theme block (Tailwind only ever reads one). Every utility above already resolves to
   var(--color-bg) etc at the browser, so re-declaring the same custom property here is
   the entire flip mechanism; nothing else has to change per scheme. */
[data-theme='light'] {
  --color-bg: oklch(96.7% 0.007 88.6);
  /* …the remaining light values from docs/design-brief.md's table, one line per token above. */
}

/* shadcn's nova preset reads --background/--card/etc on the generated ui/*.tsx components'
   own bg-background/text-foreground/border-border classes — Tailwind must still generate
   those utilities, so this has to live inside @theme too, but `inline` is required here
   specifically because each value is a var() REFERENCE to a token above, not a literal:
   plain (non-inline) @theme bakes in whichever value was true when the file was parsed,
   which would freeze every shadcn-styled control on whichever scheme was active at that
   moment instead of following [data-theme]. `inline` keeps the reference live. */
@theme inline {
  --color-background: var(--color-bg);
  --color-card: var(--color-surface);
  --color-popover: var(--color-surface);
  --color-foreground: var(--color-text);
  --color-card-foreground: var(--color-text);
  --color-primary: var(--color-text);
  --color-primary-foreground: var(--color-bg);
  --color-muted-foreground: var(--color-muted);
  --color-border: var(--color-line);
  --color-input: var(--color-line);
  --color-ring: var(--color-line-strong);
  --color-destructive: var(--color-down);
}
```

This is the brief's mapping table (`docs/design-brief.md`'s "Design guidelines" → "Colour
tokens" → "shadcn mapping" bullet — the exact line range has likely moved if 002/006 already
edited this file, per the drift-check note above; locate it by the "shadcn mapping" text, not
a line number) — the mapping is exact and already tests every pair for 4.5:1; do not hand-pick
different shadcn values.

**2. Fonts: static Fontsource weight files, both faces — no variable package for Mono.**
Verified read-only (`npm view <pkg> version license`, while writing this plan — reproduce if
it executes much later):

| Package | Version | Licence | Notes |
| --- | --- | --- | --- |
| `@fontsource/ibm-plex-sans` | 5.3.0 | OFL-1.1 | static, per-weight CSS files |
| `@fontsource/ibm-plex-mono` | 5.3.0 | OFL-1.1 | static, per-weight CSS files |
| `@fontsource-variable/ibm-plex-sans` | 5.3.0 | OFL-1.1 | exists |
| `@fontsource-variable/ibm-plex-mono` | — | — | **404 — does not exist** |

The brief allows "the variable packages where Fontsource offers them"
(`docs/design-brief.md:177-178`), but Fontsource has no variable IBM Plex Mono. Use the
**static** packages for both faces, importing only the weight files the type ramp needs —
`400/500/600/700` Sans, `400/500/600` Mono (`docs/design-brief.md:175-176`) — rather than
mixing a variable Sans with a static Mono for no benefit (the variable package's advantage is
a continuous range; this app uses four fixed weights). Import path:
`@fontsource/ibm-plex-sans/400.css` etc. from `src/index.css`, so Vite bundles the woff2
files under `/assets/` and no request leaves the origin — the CSP is `default-src 'self'`
(`nginx.conf:37`), so a Google Fonts `<link>` (canvas preview only,
`docs/design/README.md:36-37`) is never an option here. Fallback stacks exactly as specified
(`docs/design-brief.md:178-179`).

**3. The theme bootstrap: `public/theme-bootstrap.js`, loaded from `index.html`'s `<head>`
before the stylesheet, external — never inline.** `docs/ARCHITECTURE.md` §3.12 (grep for it,
per the "Context" section's note on why line numbers there have moved) and
`docs/design-brief.md`'s "Schemes" bullet both say this explicitly: an inline `<script>`
violates `script-src 'self'` under the CSP nginx already serves (`nginx.conf:37`, no
`'unsafe-inline'` on `script-src`). Logic: read `localStorage.getItem('homon-theme')`; if it
is `'light'`, set `document.documentElement.dataset.theme = 'light'`; otherwise set nothing and
dark renders by default (dark is the bare `:root`, Decision 1). No `prefers-color-scheme`
read — §3.12 overrides that override deliberately; comment the script accordingly so a future
"just respect the system" edit has to consciously remove that reasoning. Vite serves `public/`
unprocessed at the site root, so `index.html` references `/theme-bootstrap.js` — add
`<script src="/theme-bootstrap.js"></script>` immediately after `<meta charset="UTF-8" />` at
`index.html:4`, ahead of every other `<head>` element, so the attribute is set before the
browser paints anything.

**4. The theme toggle: a real `<button>` in the banner, `src/components/theme-toggle.tsx`,
`src/lib/theme.ts` for the read/write logic.** `lib/theme.ts` follows the existing `lib/*.ts`
shape (`lib/meta.ts:1-25`) minus any network call: `THEME_STORAGE_KEY = 'homon-theme'` (must
match the bootstrap script's key literally — comment both sides with the same literal and
grep for it in Step A5's verification), `getStoredTheme()`, `setTheme(theme)` (writes
`localStorage` inside `try/catch` per the brief's own bar), and `useTheme()` syncing React
state with the DOM attribute the bootstrap script may already have set before React existed.
The button's accessible name states the *action*, not the current state ("Switch to light
theme" / "Switch to dark theme"), so `getByRole('button', { name: … })` in Slice A's
Playwright spec is unambiguous. Placement: `AppShell`'s `<header>`, beside the existing
sign-in affordance, so it appears on every page without a second `<nav>`.

**5. shadcn components: install only the form primitives; hand-style everything the brief
gives exact pixel rules for.** `components.json:3` fixes `"style": "base-nova"` (Base UI,
nova preset) — already committed, so the CLI has a target to generate into. The brief's
Components section specifies exact structure for `Banner`, `Status chip`, `Services` (and,
once 008 lands, `Backups`) tables, `Links`, `Weather`, `Empty state` — none maps to a generic
shadcn component 1:1 (a shadcn `Table` bundles a wrapper the brief's grid-column layout does
not use; a shadcn `Card` adds shadow/padding defaults the brief's `panel` class overrides
anyway). Hand-build these as semantic HTML with Tailwind utility classes reading the `@theme`
tokens. **Install via the shadcn CLI, non-interactively**:

```bash
npx shadcn@latest add button input label select checkbox switch --yes --overwrite --cwd src/Homon.Web
```

`--yes` skips the "proceed?" confirmation prompt (there is no other prompt this command has —
no framework/style questionnaire, since `components.json` already answers all of that);
`--overwrite` makes a second run idempotent rather than erroring on an existing file, which
matters because Slice B/E may re-run this after Decision 5's own note about 010/011 possibly
having installed `lucide-react` already — the CLI call itself is still a fresh network fetch
each time. **This needs outbound network access to the shadcn registry** (`ui.shadcn.com`
today); if the executor's worktree cannot reach it (the same constraint that would also break
`npm ci` against the public registry, so check that first as the signal), hand-write the six
components instead: plain Base UI primitives from `@base-ui-components/react` (already a
transitive dependency of `shadcn`'s own generator; confirm with
`npm ls @base-ui-components/react` before assuming it needs installing) wrapped in Tailwind
utility classes under `src/components/ui/`, matching the shape the CLI would have produced
(a `cn()`-merged `className` prop, `data-slot` attributes) closely enough that a later `npx
shadcn diff` still finds them recognisable — note the substitution in your summary either way.

The theme toggle (Decision 4) is a natural `switch` or a plain `button` from this set,
whichever gives the exact accessible-name behaviour above. Do **not** install `table`, `card`,
`badge`, `tabs`, or `dialog` — none of the "Not drawn yet" surfaces need them, and the brief
has no accent hue for a shadcn default to lean on.

**6. New primitives this plan authors from scratch** (not shadcn, not a module plan's job):
`src/components/status-chip.tsx` (glyph + word per the state table,
`docs/design-brief.md:226-236` — Lucide icons: `Check`, `TriangleAlert`, `X`, `CircleHelp`,
a two-bars glyph for paused; verify exact Lucide icon names against the installed
`lucide-react` version, do not guess), `src/components/sparkline.tsx` (inline SVG, 88×22
desktop / 60×18 phone, per `docs/design-brief.md:246-248` and the artboard's path
construction, `Main.dc.html:99`, as a *pattern* — build the path from the probe's actual RTT
samples, not the artboard's static numbers), and `src/lib/format-uptime.ts` (`98.32%` / `—`,
mono, two decimals — `docs/design-brief.md:244`). These are Slice C.

**7. Test survival contract.** No step in this plan may change: any landmark role (`banner`,
`navigation`, `main`, `contentinfo`), the `Site` / `Admin sections` navigation accessible
names, any heading's visible text or level, any button's accessible name (`Sign in`,
`Sign out`, and every module plan's own — "Move {name} up/down", "Delete {name}", etc.), any
form label's text (`Email`, `Password`, `Keep me signed in`), `role="alert"` on error
elements, or `role="status"` on `RequireAdministrator`'s session-check paragraph
(`require-administrator.tsx:21`) and any module's equivalent. The exhaustive list of what
today's suite already asserts on:

| File | Asserts |
| --- | --- |
| `src/App.test.tsx:22-27,34,42-44,54-56` | `banner`, `navigation` named `Site`, `main`, `contentinfo` text `API 1.0.0`, heading `Dashboard`, heading `Administrators only`, link `Sign in` → `/admin/sign-in`, heading `Admin`, button `Sign out`, `navigation` named `Admin sections` |
| `src/pages/sign-in-page.test.tsx` | form structure, labels, `role="alert"` (read this file before Step E touches `SignInPage`) |
| `e2e/layout.spec.ts:20-23,31-33` | `banner`, `main` visible at every route; heading `Dashboard`, heading `Services`, heading `Links` |
| `e2e/admin.spec.ts:12-13,15-21,26,28-29,37-38,41,44-46,55` | heading `Admin`, text `Signed in as {email}`, `navigation` named `Admin sections` with links `Probes`/`Links`/`Pages`/`API keys` each landing on a heading of the same name, button `Sign out`, heading `Administrators only`, heading `Sign in` |

Every module plan that lands this session (002, 003, 006, 007, 010) adds its own such
assertions — re-read each page's own `*.test.tsx` and the relevant `e2e/*.spec.ts` file at
the start of the slice that styles it (the "re-inventory" instruction above), not only this
table, which is a floor, not a ceiling. Plans 004, 005, 008, 009 and 011 will add theirs
later, against whatever this plan ships as its own contract (Maintenance notes).

**8. New Playwright coverage this plan adds**, `e2e/theme.spec.ts`:
- Dark is the resident scheme on first visit, no stored preference: assert
  `document.documentElement.dataset.theme` is unset (or explicitly `'dark'`) via
  `page.evaluate`, taken **immediately after `page.goto`, before waiting on any
  React-rendered element** — the attribute must be correct before hydration, not merely
  eventually.
- No flash: `page.addInitScript` reading `document.documentElement.dataset` at
  `DOMContentLoaded` (before Vite's injected module script runs) and assert it already
  carries the resident scheme's signal — the automatable version of "no flash", since a
  human eyeballing a screenshot cannot reliably catch a sub-16ms one.
- Toggling persists: click the theme toggle, assert the token-driven `background-color` of
  `document.body` changed, reload, assert it is still the toggled scheme — proves
  `localStorage` round-trips, not just that `onClick` fired.
- Run at both viewport projects (`mobile`, `desktop`), matching every other spec
  (`playwright.config.ts:93-115`).

Keep `expectNoHorizontalOverflow` (`e2e/layout.spec.ts`) unmodified in mechanism — it becomes
meaningful once real widths exist, the whole point of "a net under it from its first commit"
(`layout.spec.ts:10-11`). Add `expectTappable` (`e2e/helpers.ts:106-120`, floor 40px) for
every new interactive control this plan introduces (theme toggle, table row action buttons,
etc.) at both viewports — in `layout.spec.ts`'s loop or a dedicated module spec, either is
fine, but every new clickable control must be checked somewhere.

**9. Contrast: add `@axe-core/playwright` as a devDependency, one dashboard scan per scheme
— recommended.** Verified read-only: `@axe-core/playwright` 4.13.0, MPL-2.0, fine as an MIT
project's devDependency (never bundled). The brief's own claim ("Every text pair … measures
at least 4.5:1 … including the tinted rows", `docs/design-brief.md:141-142`) is exactly what
axe's `color-contrast` rule checks against rendered, computed styles — hand-verifying oklch
pairs at design time (Decision 1) does not catch a later CSS change quietly breaking a pair
the brief promised. Add `e2e/contrast.spec.ts`: navigate to `/` once per scheme (toggle via
the same mechanism `theme.spec.ts` uses), run
`new AxeBuilder({ page }).withRules(['color-contrast']).analyze()`, assert `violations` is
empty. Scope to the dashboard only, both for speed and because it is the surface the brief's
contrast claim is about (readers, glancing, sometimes in sunlight —
`docs/design-brief.md:20-22`). New scope beyond the brief's literal ask; skipping it falls
back to the manual oklch-pair check (Decision 1), but a regression would then only be caught
by eye.

**10. CSP implications: none beyond Decisions 2–3.** Tailwind v4 produces a static stylesheet
with no runtime `<style>` injection. TipTap (007) sets inline `style="…"` on ProseMirror
decoration nodes — `nginx.conf:32-36`'s comment already anticipated this and the CSP already
carries `style-src 'self' 'unsafe-inline'`; no change needed, but Slice E's editor styling
should visually confirm it renders correctly (report-only CSP, `nginx.conf:37` — check the
console for a violation report, not a hard failure). `img-src 'self' data: blob:` already
covers Calendar/Weather icons and any page images. No third-party fetch is introduced (fonts
bundled, Decision 2; icons inline SVG via `lucide-react`).

## Defaults taken (change before implementation if wanted)

- Theme storage key: `'homon-theme'`, values `'light'` / absent-means-dark (Decision 3/4).
- The toggle's placement: banner, beside the sign-in affordance (Decision 4). The brief only
  requires "a real button with an accessible name" (`docs/design-brief.md:135`); the page
  header is an equally defensible spot if the banner feels crowded on phone.
- Fontsource weight files: Sans 400/500/600/700, Mono 400/500/600 — no italic files (nothing
  in the type ramp asks for italic).
- shadcn additions limited to Decision 5's six components; a form-field wrapper component is
  hand-written, not generated, since the brief's field spec (`docs/design-brief.md:277`) is
  three lines of Tailwind, not worth a generated abstraction.
- `@axe-core/playwright` is added (Decision 9); if the maintainer wants to skip it, remove
  Step F-2 and the corresponding Done-criteria line and say so in the summary.
- Sparkline and StatusChip live in `src/components/` (not `src/components/ui/`, which is
  shadcn-generated territory per `oxlint`'s own ignore pattern, `.oxlintrc.json:3`).

## Current state

### The SPA today (Phase 0, unstyled)

- `src/Homon.Web/src/index.css:1-7` — the whole file: a comment and `@import 'tailwindcss';`.
  Nothing else. This is where every `@theme` token, font `@import`, and any hand-written
  utility (e.g. the paused-row hatch pattern, Decision 6's sparkline styling if it needs a
  bespoke class) lands.
- `src/Homon.Web/index.html:1-14` — no theme script, no font links, no `color-scheme` meta.
  Step A3 inserts the bootstrap `<script>` after line 4 (`<meta charset="UTF-8" />`).
- `src/Homon.Web/public/` — currently only `favicon.svg` (referenced at `index.html:5`); Step
  A3 adds `theme-bootstrap.js` beside it.
- `src/Homon.Web/components.json` — already committed: `"style": "base-nova"`,
  `"iconLibrary": "lucide"`, `"cssVariables": true`, `"prefix": ""`, aliases already matching
  `@/components`, `@/lib`, `@/components/ui`.
- `src/Homon.Web/package.json:19-45` — no `@fontsource/*`, no `lucide-react`, no
  `@axe-core/playwright` yet (any of these three may already be present if 010/011 landed
  first and this plan runs after — re-check before Step A2/C1/F2, and reuse rather than
  reinstalling).
- `src/Homon.Web/src/lib/utils.ts:1-7` — `cn()` already exists, doc-commented "Unused until
  the design pass" — this plan is what makes that comment stop being true.
- `src/Homon.Web/src/components/app-shell.tsx` — the shell: `<header>` (site name, `Site`
  nav, conditional sign-in/out), `<main><Outlet/></main>`, `<footer>`. No `className`
  anywhere (line 12: "No `className` anywhere by design"). Slice B rewrites this markup with
  classes, adds the theme toggle (Decision 4) and the "refreshed N s ago" mono timestamp
  (`docs/design-brief.md:216-217` — this plan owns the display, not the data; render nothing
  if no timestamp source exists yet, and say so in the summary).
- `src/Homon.Web/src/components/require-administrator.tsx:20-44` — the admin gate: `role=
  "status"` loading text, `<section aria-labelledby="admin-gate-heading">`, conditional
  `role="alert"`. Style only; no structural change.
- `src/Homon.Web/src/pages/sign-in-page.tsx:30-80` — the sign-in form: `<form
  aria-labelledby="sign-in-heading">`, one `<p>` per field, `role="alert"` on failure. The
  "Not drawn yet" section (`docs/design-brief.md:272-278`) is this page's spec: `surface`
  inputs, `line` border, 6px radius, label above at 13px/500; primary button `text` on `bg`
  inverted.
- `src/Homon.Web/src/pages/dashboard-page.tsx:8-24` — **today** two sections
  (`services-heading`, `links-heading`). **By the time this plan runs**, this session's six
  module plans leave, in DOM order: grouped Services sections (002), Links (006), Pages —
  `<section aria-labelledby="pages-heading">`, rendered only when at least one page is
  published, otherwise absent from the DOM entirely, not an empty-state sentence (007's own
  decision) — then Weather appended last (010). **There is no Backups section and no Calendar
  section this session** — 008 and 011 do not land, so do not expect either and do not build
  an empty slot for them. Re-verify this exact order against the live file before Slice D —
  it is exactly what the "re-inventory" instruction at the top of this plan is for, and 007's
  own plan text is explicit that Pages sits "after the Links section", not wherever this
  plan's Decision might otherwise have placed it.

  **The artboard's grid does not match what actually ships this session.** `Main.dc.html`
  draws `h1` + stat strip → Services (full width) → a two-column row (Backups | Links) → a
  two-column row (Weather | Calendar) — but Backups and Calendar are both absent, and Pages
  (which postdates the artboards entirely) has no drawn position at all. Slice D's default,
  absent a further maintainer call on the point, is: **render every landed section
  full width, stacked, in DOM order** — Services, Links, Pages (conditional), Weather — and
  do *not* attempt to recreate the two-column pairing with an empty second column; 008 and
  011 restore the paired layout when they land (see Maintenance notes).
- `src/Homon.Web/src/pages/admin-home-page.tsx:5-30` — `<nav aria-label="Admin sections">` of
  plain `<Link>`s; today Probes/Links/Pages/API keys. By the time this plan runs: 002 adds
  `Probe groups` right after `Probes`; 010 adds `Weather` (position unspecified by 010's own
  text — confirm at the re-inventory step). **No Backups or Calendar entry this session** —
  008 and 011 do not land. Style the list as it exists; do not reorder it — reordering nav is
  out of this plan's scope even though it touches every `<li>`'s classes.
- `src/Homon.Web/src/pages/admin-probes-page.tsx`, `admin-links-page.tsx`,
  `admin-pages-page.tsx`, `admin-api-keys-page.tsx`, `page-page.tsx` — today all one- or
  two-line placeholders (quoted in full in this plan's own recon; by execution time each
  except `admin-api-keys-page.tsx` is the module's real page). Style whatever landed:
  `AdminTable`-shaped lists with inline edit/reorder buttons (`Probes`, `Probe groups`,
  `Links`), the `ProbeForm`'s per-kind `<fieldset>`s — **only `Ping` (002) and `HTTP` (003)
  exist this session; there is no SMB or SNMP fieldset to style** — the `TipTap` editor and
  its reader-side prose (007), and the Weather settings form (010).
  **`admin-api-keys-page.tsx` stays the Phase-0 placeholder** — 008, which builds the real
  key-management UI and the reveal-once flow, does not land this session. Give it shell/
  typography styling only (the `<h1>`, the placeholder paragraph, whatever landmark wrapping
  Slice B already applies to every page) — do not invent list/form markup for a page this
  plan has no real content to style.
- `src/Homon.Web/e2e/helpers.ts:4-17` — `READER_ROUTES`/`ADMIN_ROUTES`, already grown by
  every module plan that adds a route; `expectNoHorizontalOverflow` (`:26-64`, the
  `.overflow-x-auto` exemption a table wider than a phone needs — this session that means the
  Services table only, since Backups' matching table is 008's, not built here — wrap the
  table's scrollable region in a container carrying that exact class name, per the brief's
  "Tables that must be wider than a phone scroll inside their own `.overflow-x-auto`
  container, never the page"), `expectNoOverlap` (`:71-100`), `expectTappable` (`:106-120`,
  40px floor, `minimum` param).
- `nginx.conf:26-37` — the CSP as shipped; see Decision 10 for why this plan changes nothing
  here.
- `.oxlintrc.json:3` — `src/components/ui/**` already excluded from lint, anticipating shadcn-
  generated files.

### Hooks module plans left for this plan, by name

| Plan | Hook | What it means for Slice C/D/E |
| --- | --- | --- |
| 002 | `lib/status.ts`'s `dashboardSections(status, { phone })`, phone severity sort (down, unstable, unknown, up, paused) | Style the *result* of this pure function — don't reimplement sorting in CSS; the DOM order it produces is already correct, Slice D lays out rows in that order |
| 002 | `services-heading` id retained when grouped; per-group heading id `probe-group-${id}-heading` | Section-label styling (`h2`) applies uniformly to every such heading |
| 006 | `aria-label={`${title} (opens in a new tab)`}` on Links — "Plan 012 may swap this for a visually-hidden span; the accessible name string must not change" | Default: keep the `aria-label` as-is (Decision 7). If a visually-hidden span is added instead, the label text itself must still be identical to the accessible name today — this is a defensible product call either way, not settled by this plan |
| 007 | TipTap's rendered DOM + the stored `bodyHtml` rendered via `dangerouslySetInnerHTML` in `page-page.tsx` — needs **prose styles** for the sanitiser's exact allow-listed tags (`p, h2, h3, h4, strong, em, s, code, pre, blockquote, ul, ol, li, a, hr, br, img` — the `AllowedTags` list in `PageHtmlSanitizer.cs`, as specified by plan 007 — read the landed file, not a line number) | Slice E adds a `.prose`-equivalent utility (hand-written, scoped to the page body and the editor's `EditorContent`, reading the same type-ramp tokens) covering exactly that tag list — no more, since anything else is stripped server-side and would be dead CSS |
| 010 | Installs `lucide-react`; `weatherConditionIcon(condition)` maps a `WeatherCondition` to an icon component | Slice D imports that mapping, does not invent a second one |

**004 (SMB), 005 (SNMP), 008 (Backups), 009 (Alerts) and 011 (Calendar) do not land this
session** — there is no Backups table, Calendar section, SMB/SNMP fieldset, or API-key reveal
flow for this plan to style, and none of those rows appears above. Two primitives this plan
still builds anyway, because the brief already defines them and building them once now is
cheaper than a second design pass later: `StatusChip`'s *Succeeded*/*Late*/*Failed* words
(008's Backups will pass these instead of Up/Unstable/Down — same component, same colours,
nothing to build twice) and the `source-1`/`source-2`/`source-3` tokens (Decision 1; 011's
Calendar will be their first renderer — the legend swatches and event dots have no consumer
yet). See Maintenance notes for exactly what 004/005/008/009/011 should reuse when they land.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e` |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (typecheck), vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports, all pass |
| Add a font package | `npm install @fontsource/ibm-plex-sans @fontsource/ibm-plex-mono` (in `src/Homon.Web`) | lockfile updated |
| Add axe (optional, Decision 9) | `npm install -D @axe-core/playwright` (in `src/Homon.Web`) | lockfile updated |
| Add the shadcn components | `npx shadcn@latest add button input label select checkbox switch --yes --overwrite --cwd src/Homon.Web` | files created under `src/Homon.Web/src/components/ui/`, exit 0 |
| Typecheck + build | `npm run build` (in `src/Homon.Web`) | `tsc -b && vite build`, exit 0 |
| Lint | `npm run lint` (in `src/Homon.Web`) | `oxlint`, exit 0 |
| Unit tests | `npm test` (in `src/Homon.Web`) | `vitest run`, all pass |
| Preview a scheme without the canvas | `docs/design/README.md:39-50`'s `sed` + `npx playwright screenshot` recipe | a PNG to eyeball against the artboard |

## Suggested executor toolkit

- The `shadcn` skill, if available: knows this repository's `components.json` and how to add
  components without disturbing the committed style/preset.
- `docs/design/README.md`'s preview recipe (above) for a quick visual check of a step's CSS
  against the artboard, without needing the Claude Design canvas.
- Re-read each module plan's own "Component inventory"/"Not drawn yet" notes (this plan's
  "Hooks" table above summarises them, but the module plan is the source).

## Scope

**In scope**: `src/Homon.Web/src/index.css` (full rewrite of tokens/fonts, additive
utilities); `src/Homon.Web/index.html` (the bootstrap `<script>` tag, a `color-scheme` meta
tag if useful); `src/Homon.Web/public/theme-bootstrap.js` (new); `src/Homon.Web/src/lib/
theme.ts`, `format-uptime.ts` (new); `src/Homon.Web/src/components/theme-toggle.tsx`,
`status-chip.tsx`, `sparkline.tsx` (new); `src/Homon.Web/src/components/ui/*` (shadcn-
generated, Decision 5); every `src/Homon.Web/src/pages/*.tsx` and `src/Homon.Web/src/
components/app-shell.tsx`, `require-administrator.tsx` (adding `className` and the new
primitives — no structural/semantic change beyond what Decision 7 permits);
`src/Homon.Web/package.json`/`package-lock.json` (font, icon, axe packages);
`src/Homon.Web/e2e/theme.spec.ts`, `contrast.spec.ts` (new); `src/Homon.Web/e2e/helpers.ts`
(only if a new shared assertion helper is warranted — prefer adding to existing specs first);
`CLAUDE.md` (the "no style yet" paragraph, Slice F); `docs/ARCHITECTURE.md` §3.10 (past
tense, Slice F); `docs/design-brief.md` ("Not drawn yet" reconciled, Slice F); `plans/
README.md`.

**Out of scope**: any change to a page's accessible names, headings, landmarks, or route
structure (Decision 7 — a change here belongs to the module plan that owns that page, not
this one); reordering the admin nav or the dashboard's section order; adding any new backend
behaviour (this plan touches nothing under `src/Homon.Api`, `src/Homon.Domain`,
`src/Homon.Infrastructure`, or `tests/Homon.Api.Tests`); `nginx.conf` (Decision 10 — no change
needed); introducing an accent hue or any colour not in the brief's token table; building the
"Not drawn yet" surfaces' *behaviour* (forms, editors are each module plan's job) — this plan
only supplies their visual shape per the brief's own paragraph on the subject; **any markup
for Backups, Calendar, the SMB/SNMP probe fieldsets, or the API-key reveal-once flow** — 004,
005, 008 and 011 do not land this session, so none of that markup exists to style (see
Current state and Slices D/E).

## Git workflow

- The harness has already put you on the worktree's branch. Do not create, switch, or push a
  branch, and do not open a PR.
- One commit per lettered slice (A–F), each after that slice's own **Verify** line passes —
  not one commit for the whole plan, and not one commit per lettered step inside a slice.
  Message style from `git log --oneline`: `"<Area>: <summary> (plan 012)"`, e.g.
  `"Design: add colour tokens, fonts and the theme bootstrap (plan 012)"` for Slice A,
  matching `f4e7261 Design: fix the Status board direction, dark by default (plan 012)`'s own
  style.
- Update `plans/README.md`'s row for 012 once, in Slice F — not mid-plan.

## Steps

### Slice A — Foundation (tokens, fonts, theme bootstrap, toggle). Depends on 001 only.

**A1. Colour tokens.** Rewrite `src/Homon.Web/src/index.css`: update the opening comment
(currently "DELIBERATELY EMPTY", `:2`); then the three blocks Decision 1 gives verbatim
(`@theme` with the dark literals, `[data-theme="light"] { … }` with the light overrides,
`@theme inline` with the shadcn mapping) — copy the oklch values from
`docs/design-brief.md`'s colour token table (find it by the "Colour tokens" heading, not a
line number — see the caution in "Context" above) rather than retyping Decision 1's own
copies a third time.

**Verify**: `npm run build` (in `src/Homon.Web`) → 0 errors; `docs/design/README.md:39-50`'s
preview recipe against `Main.dc.html`; `npm run dev` and confirm the dashboard's `<body>`
background is now the dark `bg` token's colour (nothing else has classes yet).

**A2. Fonts.** `npm install @fontsource/ibm-plex-sans @fontsource/ibm-plex-mono` (check
`package.json` first — 010 may have added `lucide-react` already, but not these). In
`index.css`, after the Tailwind import, add the weight imports (Decision 2):
`@import '@fontsource/ibm-plex-sans/400.css'; … /700.css';` and the Mono equivalents at
400/500/600. Add `--font-sans`/`--font-mono` referencing the exact fallback stacks
(`docs/design-brief.md:178-179`). Add a `.mono` utility also setting
`font-variant-numeric: tabular-nums` (`:180` — not a Tailwind default).

**Verify**: `npm run build` → 0 errors; devtools' computed style on `<body>` resolves to IBM
Plex Sans; network tab shows the woff2 loading from `/assets/`, not `fonts.googleapis.com`.

**A3. `theme-bootstrap.js`.** Create `src/Homon.Web/public/theme-bootstrap.js` per Decision 3
— roughly:

```js
// Sets the theme attribute before first paint, from a stored, explicit choice only — dark
// is the resident scheme for every visitor per docs/ARCHITECTURE.md §3.12, which overrides
// the more usual "respect prefers-color-scheme" on purpose. Do not add a system-preference
// read here without re-reading that section's reasoning first.
(function () {
  try {
    var stored = window.localStorage.getItem('homon-theme')
    if (stored === 'light') {
      document.documentElement.dataset.theme = 'light'
    }
  } catch (e) {
    // localStorage unavailable (private browsing, disabled storage) — dark renders, same as
    // a first-ever visit. Not an error state.
  }
})()
```

Edit `index.html`: add `<script src="/theme-bootstrap.js"></script>` immediately after
`<meta charset="UTF-8" />` (line 4), before the viewport/description meta tags and well before
Vite's own injected `<script type="module" src="/src/main.tsx">` (line 12).

**Verify**: `npm run build && npm run preview` (in `src/Homon.Web`), open devtools, run
`document.documentElement.dataset.theme` before React has rendered (Network-throttle if
needed to catch the window) → `undefined` on a fresh profile (dark, no stored choice); set
`localStorage.setItem('homon-theme','light')` and reload → `'light'` before any visible
paint.

**A4. `lib/theme.ts` and `theme-toggle.tsx`.** Per Decision 4. `lib/theme.ts` exports
`THEME_STORAGE_KEY`, `getStoredTheme(): 'light' | 'dark' | null`, `setTheme(theme)`
(try/catch around the `localStorage` write, also sets the DOM attribute immediately so the
toggle is instant), and `useTheme()` (reads the DOM attribute on mount, since the bootstrap
script may have already set it). `theme-toggle.tsx` renders a `<button type="button">` (or
the shadcn `switch`, Decision 5) with the action-describing label, calling `setTheme` with
the *other* scheme on click.

**Verify**: `npm test -- theme` (new `theme.test.ts`/`theme-toggle.test.tsx` — assert
`getByRole('button', { name: /switch to (light|dark) theme/i })` exists, clicking it flips
`document.documentElement.dataset.theme` and calls `localStorage.setItem`) → passes.

**A5. Wire the toggle into `AppShell`.** Add `<ThemeToggle />` inside `app-shell.tsx`'s
`<header>`, beside the existing sign-in/out affordance (`app-shell.tsx:35-42`) — do not
remove or rename anything already there (Decision 7).

**Verify**: `npm run build && npm test` → 0 errors, all pass; `npm run dev`, click the toggle
in a browser, confirm the (still bare) page recolours.

**A6. `e2e/theme.spec.ts`.** Per Decision 8, all three cases (dark default, no-flash,
persist-across-reload), both viewport projects.

**Verify**: `./ci/run-ci.sh e2e` → exit 0, `theme.spec.ts` passes at both viewports.
**End of Slice A** — the gate is green and the foundation is live even though nothing else
has a `className` yet.

### Slice B — Shell (banner, navigation, footer, page header, stat strip)

**B1. Re-inventory `app-shell.tsx`, `require-administrator.tsx` as they stand** (per this
plan's top-of-file instruction) — confirm the landmark/heading contract in Decision 7's table
still holds.

**B2. Banner.** Style `app-shell.tsx`'s `<header>` per `docs/design-brief.md:213-219`:
`surface` background, 1px `line-strong` bottom rule, 56px/52px height, site name left, `Site`
nav filling the banner height with the active-item underline (use `NavLink`'s `isActive`/
`aria-current="page"` — do not hand-roll active detection), the mono "refreshed N s ago"
string at the right on desktop only (render nothing if no data source exists yet), and the
sign-in/out affordance plus the theme toggle at the right end. On phone, the nav "must remain
reachable without a second row that moves sticky offsets" — a single 52px row that wraps its
two items is the simplest reading; no hamburger/drawer, the brief lists only "Dashboard,
Admin" (`docs/design-brief.md:78`).

**Verify**: `npm test` → `app-shell` tests (if any exist by now from a module plan) still
pass unmodified; visually compare against `Main.dc.html:67-74` and `BoardPhone.dc.html:63-69`
via the preview recipe.

**B3. Page header + stat strip.** Each top-level page (`DashboardPage` first) gets an `h1`
left, stat strip right on desktop / below on phone, 1px `line-strong` rule under both, per
`docs/design-brief.md:221-224`. The stat strip's actual numbers come from whatever query
002's `useStatus()` (or equivalent) exposes — read `lib/status.ts` (or its real name) before
building this; if the totals shape differs from "5 up · 1 unstable · 1 down · 1 paused ·
99.44% uptime, 30 days", use the real field names and say so in your summary. Style-only —
no new data fetching in this plan beyond consuming what already exists.

**Verify**: `npm run build` → 0 errors; the dashboard's `h1` and stat strip render per the
artboard at both viewport widths (manual check via preview recipe or `npm run dev`).

**B4. Footer.** `docs/design-brief.md:270`: mono, 12px, `muted`, 1px `line` top rule,
"Homon {version} · API {release}" — `app-shell.tsx:47-52` already computes this text; add
only the classes.

**Verify**: `npm run build && npm test` → 0 errors, all pass.

**B5. Sign-in page, admin gate.** Style `sign-in-page.tsx` and `require-administrator.tsx`
per "Not drawn yet" (`docs/design-brief.md:272-278`): form fields as `surface` inputs with
1px `line` border, 6px radius, label above at 13px/500 (using the shadcn `input`/`label`
components from Decision 5, or hand-styled `<input>`/`<label>` if the generated components
fight the exact spec — either is acceptable, prefer the generated ones first); the submit
button as the primary-button style (`text` on `bg` inverted).

**Verify**: `npm test -- sign-in-page` → the existing test file (read it first, per Decision
7) still passes unmodified; `./ci/run-ci.sh e2e` → `admin.spec.ts`'s sign-in cases still pass.

### Slice C — Status primitives

**C1. `lucide-react`.** Check `package.json` first (010 may have added it already); install
if absent.

**C2. `status-chip.tsx`.** Glyph + word per the state table (`docs/design-brief.md:226-236`)
— five variants (up/unstable/down/unknown/paused), each taking an optional `detail` string
and rendering the row-tint classes the *table row* applies (the chip itself just renders the
icon+word pair; row background/hatch is the table row's concern, Slice D, since a Backup row
and a Services row share the same chip but different row markup).

**Verify**: `npm test -- status-chip` (new test file: five variants render the right icon
name — assert via `aria-hidden` presence and the visible word text — and no variant renders
colour text alone without the word, per "Status is not colour alone",
`docs/design-brief.md:104`) → passes.

**C3. `sparkline.tsx`.** SVG path builder from an array of RTT samples, 88×22 / 60×18, 2px
`muted` stroke, 2.5px dot in the row's status colour on the latest point, per
`docs/design-brief.md:246-248`. Accepts a `size: 'table' | 'inline'` prop for the two
dimensions. Empty/no-samples → renders nothing (ping probes only; other kinds' cells stay
empty per the same brief line).

**Verify**: `npm test -- sparkline` (new: a sample array produces a `<path>` with the right
number of points; empty samples render `null`) → passes.

**C4. `format-uptime.ts`.** `formatUptime(value: number | null): string` → `'98.32%'` or
`'—'`. Pure, one test file.

**Verify**: `npm test -- format-uptime` → passes.

**C5. The paused hatch and row tints as CSS utilities.** Add to `index.css` (or as inline
`style` where Tailwind's own gradient utilities don't reach the exact 135°/10px/2px spec,
`docs/design-brief.md:234`): `.row-tint-unstable`, `.row-tint-down`, `.row-paused` classes
consuming the `unstable-bg`/`down-bg`/`bg` tokens.

**Verify**: `npm run build` → 0 errors.

### Slice D — Dashboard sections

**D1. Re-inventory `dashboard-page.tsx`** exactly as shipped (Current state's caveat) —
confirm the section order and every heading id before writing a single class.

**D2. Services table(s).** Per the brief's Services component rule: desktop grid-column
layout (`Status 132px · Service 1.1fr · Detail 1.6fr · Uptime 96px right-aligned · 30 days
92px · Checked 120px`), a real `<thead>`/`<th>` header row, panel wrapper (`surface`, 1px
`line`, 6px radius). Consume `dashboardSections()`'s output (002) as-is — one table per
section, phone fold (name+chip line, then detail+uptime line) and severity sort already
computed upstream (the Hooks table above). Wrap the table region in a `.overflow-x-auto`
container so `expectNoHorizontalOverflow` exempts it correctly if the grid ever needs more
than the viewport (the exemption lives at `e2e/helpers.ts:42`, confirmed against the landed
file).

**Verify**: `npm test -- dashboard-page` → existing tests (002/006/etc.'s) pass unmodified;
`./ci/run-ci.sh e2e` → `layout.spec.ts` and any `dashboard-groups.spec.ts` (002) still pass,
now with real widths for `expectNoHorizontalOverflow` to actually exercise.

**D3. Backups — deferred, not built this session.** 008 does not land; there is no Backups
table in the DOM to style. Skip this step. When 008 lands, its own plan (or a follow-up
design note) reuses the same table shape as D2 with different columns (`Outcome 132px · Job
1.4fr · Last run 1.4fr · Schedule 140px`, schedule mono `muted`) and `StatusChip` with the
Succeeded/Late/Failed words already built in Slice C — see Maintenance notes.

**D4. Links.** Panel of rows per the brief's Links component rule: title as a `text`-coloured
link with a `line-strong` underline at 3px offset, description in `muted` beside it on
desktop / below on phone.

**D5. Weather.** Panel per the brief's Weather component rule: 40px `muted` condition icon
(`weatherConditionIcon`, 010's hook), mono 30px/500 temperature, `muted` condition/wind/
feels-like line, three forecast rows at 20px icon / condition word / mono high-low.

**D6. Calendar — deferred, not built this session.** 011 does not land; there is no Calendar
section in the DOM to style. Skip this step. When 011 lands, its own plan reuses
`source-1`/`source-2`/`source-3` (Decision 1, already in `@theme` from Slice A) for the
legend swatches and event dots — the first real renderer of those three tokens — per the
brief's Calendar component rule; see Maintenance notes.

**D7. Pages section.** Full-width, rendered only when non-empty (007's own decision — hidden
entirely when there are zero published pages, not an empty-state sentence, unlike D8's
sections below) — a plain `<ul>` of links, no special component; style consistent with the
section-label rule. No empty state to build for this one (see D8).

**D8. Empty states.** Per the brief's Empty state component rule: `surface`, 1px dashed
`line-strong` border, 6px radius, 15px/600 title, one `muted` sentence with a link to the
relevant admin page when one exists. Apply uniformly to every section's zero-data case that
this session actually ships: **Services, Links, Weather** — Backups and Calendar have no
empty state to style (D3/D6, deferred), and Pages has none by design (D7). Cross-check
against `BoardEmpty.dc.html` for wording style, but the *text itself* is each module plan's
(e.g. 010's "No weather location yet — an administrator sets it under Admin → Weather").

**Verify** (all of D2–D8): `npm run build && npm test` → 0 errors, all pass; `./ci/run-ci.sh
e2e` → every module's own dashboard-touching spec still passes; visually compare `/` against
`Main.dc.html` (desktop) and `BoardPhone.dc.html`/`BoardEmpty.dc.html` (phone, zero-data) via
the preview recipe.

### Slice E — Admin pages

**E1. `AdminTable`-shaped lists.** A shared *visual* pattern, not necessarily a shared React
component (several module plans already built their own list-with-inline-edit shape
independently — `plans/006-links.md`'s own note: "extract it once a third instance exists —
not before") for **Probes, Probe groups and Links** this session (Backups, Calendar and a
real API-keys list are 008/011's, not built here): row per item, action buttons at a
consistent size (≥40px, Decision 8), consistent spacing per the brief's Space and shape rule.
Style each admin page's existing markup; do not refactor these into one shared component here
— pixels, not extraction.

**E2. `ProbeForm`'s per-kind fieldsets.** Style whatever 002/003 shipped: a `<select>` for
kind, conditional `<fieldset><legend>` blocks per kind — **`Ping` and `HTTP` only this
session; there is no SMB or SNMP fieldset, since 004/005 do not land** — each field as the
"Not drawn yet" input pattern (Slice B5's shadcn components, reused here).

**E3. `PageEditor` + reader prose.** The TipTap toolbar's buttons (007's list: Bold, Italic,
Strikethrough, Code, Heading 2-4, lists, Blockquote, Link, hr, Undo, Redo) as a real button
row, each with its `aria-label` untouched (007's own note: name must match the action
exactly). Add the prose utility (Decision/Hooks table) scoped to `page-page.tsx`'s
`dangerouslySetInnerHTML` container and to TipTap's `EditorContent`, covering exactly the
sanitiser's allow-listed tags (the `AllowedTags` list in `PageHtmlSanitizer.cs`, as specified
by plan 007 — read the landed file, not a line number) — headings 2-4 at a readable size
relative to the type ramp, lists with visible markers, `blockquote` with a left rule in
`line-strong`, `code`/`pre` in mono with a `surface` background.

**E4. `admin-api-keys-page.tsx` — shell only, deferred otherwise.** 008 does not land, so
this page is still the Phase-0 placeholder (Current state) — apply only whatever shell/
typography classes every admin page gets from Slice B, nothing page-specific. The reveal-
once flow (`ApiKeyReveal`, the `role="alert"` warning that must stay assertive) does not
exist yet; skip it. See Maintenance notes for what 008 reuses when it lands.

**Verify** (E1–E3; E4 is a shell-only pass): `npm run build && npm test` → 0 errors, all
pass; `./ci/run-ci.sh e2e` → `admin.spec.ts` and every landed module's own admin spec still
pass; `expectTappable` passes on every new admin action button at both viewports.

### Slice F — Docs and the contrast check

**F1. Contrast scan (Decision 9).** If included: `npm install -D @axe-core/playwright`,
write `e2e/contrast.spec.ts` per Decision 9.

**Verify**: `./ci/run-ci.sh e2e` → `contrast.spec.ts` passes, zero `color-contrast`
violations, both schemes.

**F2. Docs.** `CLAUDE.md`: change "There is deliberately no style yet…" (`:24-29`) to past
tense, naming this plan as the source of the styling now in place, and correct the
instruction "Until plan 012 lands, do not add classes" to reflect that it has. `docs/
ARCHITECTURE.md` §3.10 (grep `^### 3\.10` — do not trust a line number, per the "Context"
section's note): rewrite in past tense — the SPA now renders styled HTML per §3.12/this plan,
and the tests still query by role and name (confirm this claim is still true by re-running
the full suite before writing it). `docs/design-brief.md`'s "Not drawn yet" section (locate by
its heading text): replace with a short note that the surfaces this session actually built
(page view, sign-in, admin lists, the Ping/HTTP probe form) now have their shape from this
plan; **leave the API-key reveal flow listed as "not drawn yet"** — 008 has not landed, so it
genuinely still isn't. `plans/README.md`: status row for 012 (mark it `DONE` only for what
this session built — say in the row or your summary that Backups/Calendar/SMB/SNMP/API-key
surfaces remain unstyled because their modules haven't landed, so the next reviewer doesn't
read `DONE` as "everything the brief describes exists").

**Verify**: `grep -n "deliberately no style yet" CLAUDE.md docs/ARCHITECTURE.md` → no match
(confirms both were actually rewritten, not just this plan's own copy of the phrase);
`git diff --stat -- docs/ CLAUDE.md plans/README.md` shows only the intended files.

## Test plan

- **Vitest, new**: `lib/theme.test.ts`, `theme-toggle.test.tsx`, `status-chip.test.tsx`,
  `sparkline.test.tsx`, `format-uptime.test.ts` — cases listed in each Slice A/C step above.
- **Vitest, existing**: every page's own `*.test.tsx` (`sign-in-page.test.tsx`, `App.test.tsx`,
  and whatever 002/003/006/007/010 added) must keep passing **unmodified in their
  assertions** — a `className` addition never changes what
  `getByRole`/`getByLabelText`/`getByText` finds. Model: re-run `npm test` after every slice,
  not just at the end, so a broken accessible name is caught at the slice that broke it.
- **Playwright, new**: `e2e/theme.spec.ts` (Decision 8), `e2e/contrast.spec.ts` (Decision 9,
  if included).
- **Playwright, existing**: `layout.spec.ts`'s viewport sweep and section-heading assertions;
  `admin.spec.ts`'s full gate/sign-out/anonymous flows; every landed module plan's own spec
  (`dashboard-groups.spec.ts` (002), `links.spec.ts` (006), `pages.spec.ts` (007),
  `weather.spec.ts` (010)) — all must keep passing. There is no `backups.spec.ts` or a
  Calendar spec this session.
- **Verification**: each slice's own **Verify** line above is the narrowest command that
  exercises what it changed; `./ci/run-ci.sh` → `PASS — web api e2e` runs in full at the end
  of Slice A (A6) and again at the end of Slice F (F1/the final check) — a slice that breaks
  a narrower check should be caught before the next slice starts, not only at the two full
  runs.

## Done criteria

- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`
- [ ] `npm run build` (in `src/Homon.Web`) exits 0; `npm run lint` exits 0
- [ ] Every existing Vitest/Playwright assertion on a role, accessible name, heading, or
      landmark still passes unmodified (Decision 7)
- [ ] `e2e/theme.spec.ts` passes: dark on first visit, no flash, toggle persists across reload
- [ ] `e2e/contrast.spec.ts` passes with zero `color-contrast` violations in both schemes
      (Decision 9) — or is omitted with a note in the summary if that recommendation was
      declined
- [ ] `grep -rn '#[0-9a-fA-F]\{3,8\}' src/Homon.Web/src --include='*.tsx' --include='*.ts'`
      returns no matches **outside `src/index.css`** — every colour used in a component reads
      a token, none is a hard-coded hex literal (this replaces the plan-template's usual
      "`className` now non-empty" check, since that is expected and not the interesting fact
      here — see the drift-check note about this in the header)
- [ ] `grep -n "deliberately no style yet" CLAUDE.md docs/ARCHITECTURE.md` → no match
- [ ] `docs/design-brief.md`'s "Not drawn yet" section reconciled — the API-key reveal flow
      stays listed there (008 has not landed); everything else this session built is removed
      from the list
- [ ] `plans/README.md`'s 012 row is `DONE` (or `DONE — Slice A only`, if only the
      foundation was executed per the Sequencing section), noting in the row or your summary
      that Backups/Calendar/SMB/SNMP/API-key-reveal surfaces remain unstyled
- [ ] Every new interactive control (theme toggle, and any newly-styled row-action button)
      passes `expectTappable` at both viewports

## STOP conditions

- This session's other five plans (013, 002, 003, 006, 007, 010) have not landed at all by
  the time this executes — run **Slice A only**, update `plans/README.md`'s row to
  `DONE — Slice A only`, and stop; do not guess at markup for pages that do not exist yet.
- Any markup for Backups, Calendar, an SMB/SNMP probe fieldset, or the API-key reveal flow
  exists in the working tree — that means a plan outside this session's order (004, 005, 008,
  009 or 011) ran anyway; STOP and report rather than styling markup this plan was never
  scoped to review.
- A design token does not render as specified once applied (an `oklch()` syntax mismatch, or
  a computed contrast ratio below 4.5:1 the brief claims passes) — stop and report; the
  brief's own text is the tie-breaker over the artboards (`docs/design-brief.md:120`), but
  this plan is not authorised to override the brief itself.
- A page's accessible name, landmark role, or heading text would have to change to fit a
  component into the brief's layout — stop; that decision belongs to the module plan that
  owns the page, not this one (Decision 7 is a hard boundary).
- A package this plan assumed absent/present (e.g. `@fontsource-variable/ibm-plex-mono`) has
  changed on npm since this plan was written — re-verify with `npm view` before installing,
  note any substitution in your summary.
- A step's verification fails twice after a reasonable fix attempt.
- The re-inventory step finds `dashboard-page.tsx` or `admin-home-page.tsx` no longer matches
  the flat-sections/flat-nav-list shape every module plan's own "Current state" assumed —
  reconcile before Slice D/E, report what you found rather than forcing a layout onto a shape
  it does not fit.

## Maintenance notes

- **A future module adds a styled section** by writing its own unstyled markup exactly as
  this session's plans did (semantic HTML, correct landmarks/headings, no pixel assumptions),
  then reusing this plan's primitives — `StatusChip`, `Sparkline`, the panel/row utilities in
  `index.css`, the empty-state pattern — rather than inventing new ones. A genuinely new
  visual shape extends `docs/design-brief.md` first, this plan's components second — never
  the reverse.
- **The theme bootstrap script and `lib/theme.ts` share a storage key as two independent
  string literals** (Decision 4) — a rename on one side without the other silently breaks the
  no-flash guarantee; keep `e2e/theme.spec.ts`'s no-flash case even though it looks redundant
  with the persistence case.
- **The three calendar source colours are validated as a set of three** — a fourth needs its
  own contrast/colour-blind validation before joining the token table, never a locally
  generated hue (`docs/design-brief.md:162-166`).
- **A reviewer should scrutinize**: every `className` addition against Decision 7's table (the
  fastest silent break is "cleaning up" a heading's text while adding a class); every table's
  `.overflow-x-auto` placement (a missed one reopens page-level horizontal scroll on phone);
  and the prose-style tag list in Slice E3 staying in lockstep with
  `PageHtmlSanitizer.cs`'s allow-list — a tag with no matching prose style renders unstyled,
  not broken, so the drift is easy to miss visually.
- **How 004, 005, 008, 009 and 011 style their own additions**, when each eventually lands
  (none does this session):
  - **004 (SMB), 005 (SNMP)**: extend `ProbeForm`'s conditional `<fieldset>` region exactly as
    003's HTTP fieldset does (Slice E2) — same input pattern, same shadcn primitives, no new
    visual language.
  - **008 (Backups + API-key admin)**: the Services table shape from Slice D2 and
    `StatusChip`'s Succeeded/Late/Failed words (already built in Slice C, unused until 008
    lands — Hooks table) cover the Backups table entirely; the API-key list follows the
    `AdminTable` pattern from Slice E1, and the reveal-once block gets the "Not drawn yet"
    form-field styling from Slice B5 plus its own `role="alert"` left exactly as assertive as
    008 specifies — this plan does not soften it because this plan never builds it.
  - **009 (Alerts)**: no visible surface of its own expected; if one is added later, style it
    from the same tokens, no new component.
  - **011 (Calendar)**: the `source-1`/`source-2`/`source-3` tokens are already live in
    `@theme` from Slice A — 011's Calendar section is their first renderer (legend swatches,
    event dots) per the brief's Calendar component rule; no new colour to add, no token
    rename needed.
  - Each of these lands as its own small design follow-up (a slice-sized addition to that
    module's own plan, or a short plan of its own) rather than a second full design pass —
    the primitives above are what make that follow-up small.
- **Deferred**: extracting the several independent `AdminTable`-shaped list implementations
  into one component (Slice E1); an enforced, non-report-only CSP (a separate decision with
  its own risk profile); a household-configurable accent colour (the brief is explicit there
  is none) — revisit only if the brief itself changes. Also deferred, because the owning
  module hasn't landed: Backups' table (D3), Calendar's section (D6), the SMB/SNMP fieldsets
  (E2), and the API-key reveal flow (E4) — see the guidance above for each.
