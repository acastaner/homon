# Plan 019: Hide the empty ungrouped section, and let a reader arrange the dashboard

> **This plan was executed as it was written**, in this checkout, on branch
> `plan/019-arrange-dashboard-sections`. It is kept as the record of what changed and why; the
> decisions themselves are `docs/ARCHITECTURE.md` §3.23.

## Status

- **Priority**: P2 — a visible falsehood on the family's own page, plus a feature the maintainer
  asked for in the same breath.
- **Effort**: M
- **Risk**: MEDIUM. SPA only — no API, no database, no migration. The risk is entirely in the five
  existing suites that assert the dashboard's headings, their order, its tap targets and its
  contrast: this is the second change to reshape the dashboard's section markup since plan 012.
- **Depends on**: none (builds on 002, 006, 007, 010, 012, 014, 018 — all DONE)
- **Category**: bug (the empty section) + feature (the ordering)
- **Planned at**: commit `1ce816e`, 2026-09-24
- **Requested by**: the maintainer, 2026-09-24. Verbatim: the home page "will always display a
  'Others' even if it's empty. This one should be hidden if it's empty, and show only if probes were
  not placed in any existing probe group." And, in the same request, users "need to be able to order
  the various sections (probe groups, pages, links, weather, etc) manually (eg: move the weather
  widget to the top)", remembered "using the localStorage the same way we remember if the widgets
  are collapsed or not".

## Why this matters

`dashboardSections` appended the ungrouped section unconditionally. Since the API already drops a
group with no members (`StatusEndpoints.cs`'s `.Where(g => g.Members.Count > 0)`), that fallback was
the only empty section the dashboard could produce — and it rendered a heading above a "No probes
yet. An administrator adds them under Admin → Probes" panel that stops being true the moment real
groups exist. A household that has tidied every probe into a group was being told it had none.

The second half is the ordering. The sequence was hard-coded in JSX — probe groups, Links, Pages,
Weather — so a reader who cares most about the weather could not move it up. Plan 018 had already
established the mechanism for a per-browser dashboard preference; this reuses it.

## Decisions — implement these exactly

**D1. The guard is `ungrouped.length > 0 || groups.length === 0`, on the resolved probes.** Not on
`ungroupedProbeIds.length`: `resolve()` drops an id with no matching probe, so a stale id in the
payload would keep an empty section alive. The `groups.length === 0` half is load-bearing and must
not be "simplified" away — with zero groups and zero probes the section is still returned, labelled
"Services", because that one **is** the bare install's onboarding hint. `e2e/layout.spec.ts:32` and
two whole-object assertions in `lib/status.test.ts` pin it.

**D2. The label stays "Other".** The request called it "Others"; the maintainer chose to leave it.
Plan 002's Decision 7 picked the word and `e2e/dashboard-groups.spec.ts` matches heading text
exactly, so a rename costs spec edits for no functional gain.

**D3. A second localStorage key, `homon-section-order`, against plan 018's maintenance note.** 018
said to widen its array into an object under the one key and not to add a second. Taken anyway, and
recorded: collapse and order have two independent "back to default" states, and one key gives them
one lifetime — `e2e/dashboard-collapse.spec.ts:89` asserts `homon-collapsed-sections` is `null` once
everything is expanded, which a stored order would quietly falsify. See §3.23.

**D4. A sequence is not a set — three deliberate divergences from `collapsed-sections.ts`.** The cap
is `slice(0, MAX_IDS)`, keeping the head; deduping keeps the first occurrence; and "back to default"
is `isNaturalOrder`, a comparison, not an emptiness check. Each has a named test. Do not make the
two modules symmetrical.

**D5. Moving a section is a swap, not a relocation.** Exchange the pressed section with the section
**visible** next to it, and in storage exchange exactly those two ids. Splicing an id in beside its
neighbour is not invertible: with `["links","pages","weather"]` stored and `pages` not rendering,
up-then-down returns the visible order but leaves `pages` one lower. Only a swap is its own inverse
and only a swap preserves an absent section's slot.

**D6. A section the stored order has never seen goes last.** One stable sort, no special case, and
it agrees with `hydrateSectionOrder`. A probe group created after a reader arranged the dashboard
appears below Weather until moved; Reset order answers that in one click.

**D7. `knownSectionIds` is built from `status.data.groups` plus four literals, never from
`dashboardSections`'s output.** This is the sharp edge D1 and D3 create together: once the ungrouped
section stops rendering, deriving the prune list from what renders drops `'ungrouped'` and the next
press of any other control wipes that section's remembered collapse *and* order slot. A section id
belongs in the list because it *can* exist. Nothing in the suite catches this; it is why the comment
in `dashboard-page.tsx` says so at length.

**D8. `arrange` is an optional typed prop on `CollapsibleSection`, and `undefined` renders nothing.**
Not an empty wrapper, not a disabled pair — so with arrange mode off the markup is byte-for-byte what
018 shipped and its four constraining assertions need no edit. The buttons are siblings of the
`<h2>`, never children. Typed props rather than `headerActions?: ReactNode`: that component's comment
owns the 40px floor, and a free-form slot lets a caller fail `e2e/refresh.spec.ts` from another file.

**D9. The move buttons are named from a stable `label`, not from `heading`.** Weather's heading
carries the configured place, which is household data; `Move Weather up` must not become
`Move Weather · Kitchen up`. `label` defaults to `heading`, so every other section is unaffected.

**D10. Arrange mode is React state and is never persisted.** It is a mode, not a preference. One key
per concern.

**D11. One page-level Reset order, not one per section.** It clears a single key, and N buttons
sharing that accessible name is an immediate Playwright strict-mode ambiguity. Disabled when nothing
is stored.

**D12. Up/down buttons, no new dependency.** Every reorder surface in this app already works this
way. Drag-and-drop would need `@dnd-kit`, a keyboard path anyway, and flaky two-viewport Playwright
coverage.

**D13. The section list must stay a direct child of the page fragment.** `app-shell.tsx:93` makes
`<main>` a `flex flex-col gap-8`; wrapping the `.map` in a `<div>` collapses every inter-section gap
into one, and no existing assertion is sensitive enough to notice.

## What changed

New:

- `src/Homon.Web/src/lib/section-order.ts` — the key, the defensive parse, `orderSections`,
  `hydrateSectionOrder`, `swapSectionIds`, `isNaturalOrder`, `useSectionOrder`.
- `src/Homon.Web/src/lib/section-order.test.ts` — 23 tests.
- `src/Homon.Web/e2e/dashboard-arrange.spec.ts` — 6 tests × 2 viewport projects.

Changed:

- `src/Homon.Web/src/lib/status.ts` — D1, plus the doc comment.
- `src/Homon.Web/src/lib/status.test.ts` — the one case that pinned the old behaviour is inverted
  and renamed (`drops the empty ungrouped section once a group exists`), and a second case covers
  the stale-id path. `DashboardSection` was **not** widened, so the two whole-object `toEqual`s are
  untouched.
- `src/Homon.Web/src/components/collapsible-section.tsx` — D8, D9.
- `src/Homon.Web/src/components/collapsible-section.test.tsx` — 5 new cases, existing 5 untouched.
- `src/Homon.Web/src/pages/dashboard-page.tsx` — the four hard-coded section blocks become one
  `SectionSlot[]` rendered in order; `probeSectionBody` and `weatherSectionBody` extracted; the
  Arrange / Reset controls; D7.
- `src/Homon.Web/src/pages/dashboard-page.test.tsx` — 11 new cases, existing 12 untouched.
- `docs/ARCHITECTURE.md` — new §3.23, and one sentence added to §3.22's "The markup" paragraph.

## The assertions that constrained this, all green without an edit

`e2e/layout.spec.ts:32` (bare `Services` heading), `e2e/dashboard-groups.spec.ts:74` (exact heading
sequence `['Hosts','Storage','Other','Links','Pages','Weather']` — that spec seeds an ungrouped
probe, so `Other` is non-empty there), `e2e/refresh.spec.ts:23` (`expectTappable` over every button
on `/`, which now also covers `Arrange`), `e2e/contrast.spec.ts` (axe `color-contrast` over `/` in
both schemes, which now also covers `Arrange`), `e2e/dashboard-collapse.spec.ts:89`
(`expect(stored).toBeNull()` — the whole justification for D3), and every existing case in
`dashboard-page.test.tsx` and `collapsible-section.test.tsx`.

`expectTappable` filters on measured width, not on `disabled`, so the greyed-out first-up and
last-down buttons are measured too. `e2e/dashboard-arrange.spec.ts`'s last test is the only place
the move buttons meet the floor and the Pixel 7 width, because they are on screen in arrange mode
and nowhere else.

## Test plan

```bash
./ci/run-ci.sh web     # PASS — 133 tests (93 before)
./ci/run-ci.sh e2e     # PASS — 87 (75 before; +6 new tests × 2 viewport projects)
./ci/run-ci.sh api     # PASS — the control: this change touches no C#
```

Then by hand on `npm run dev --prefix src/Homon.Web` (http://localhost:5300): with every probe in a
group, no "Other" section; move one out and it returns. Arrange → move Weather to the top → Done →
reload → still there. Reset order → natural order back and no `homon-section-order` key. Collapse a
section and confirm the two preferences are remembered independently. Both at phone width and in
light mode.

## Done criteria

- All three suites PASS, `api` with 0 skips.
- `git diff --stat` shows nothing under `src/Homon.Api/`, `src/Homon.Domain/`,
  `src/Homon.Infrastructure/` or `tests/Homon.Api.Tests/`.
- The five specs listed above are byte-identical to `main`.
- `oxlint` is silent.

## STOP conditions

- Any of those five specs needs an edit to pass. Each is load-bearing for a decision above; a
  failure there is a defect in the change, not in the test.
- `src/Homon.Web/src/lib/collapsed-sections.ts` needs an edit. Order and collapse are separate
  modules by D3; if the feature cannot be built without reaching into that module, D3 needs
  revisiting with the maintainer first.
- `./ci/run-ci.sh api` reports a skipped test.

## Git workflow

Branch `plan/019-arrange-dashboard-sections`, cut in this checkout and **left checked out** for the
maintainer's manual review. No worktree (`CLAUDE.md`, "Where the work happens"). Merging, pushing
and branch cleanup wait on the maintainer's word.

## Maintenance notes

**A third dashboard preference should be a third key, not a third field.** D3 has now set the
pattern that 018's note argued against, and the reason generalises: each preference wants its own
default and its own "back to default". If a fourth arrives and the key count starts to feel silly,
that is the moment to reconsider — with a migration, not by quietly nesting one inside another.

**A new dashboard section must claim an id in three places.** Plans 008 (Backups) and 011 (Calendar)
each need to pick a literal id (`backups`, `calendar`), add it to `knownSectionIds` in
`dashboard-page.tsx`, and push a `SectionSlot` in its natural position. Miss the second and that
section's collapse and order are pruned away on the next press of any control; miss the third and it
does not render at all. The `label` may equal the `heading` unless the heading carries data, as
Weather's does.

**The two storage modules are near-identical and must not be unified.** `section-order.ts` diverges
from `collapsed-sections.ts` in three places on purpose (D4), each documented in the file. A
well-meaning refactor into one generic `useStoredIds` helper would erase exactly those three
comments and reintroduce three bugs, one of which — the `slice` direction — is silent.

**The order layers over a household-wide one.** `PUT /api/v1/probe-groups/order` still sets the group
order every browser starts from. If a household ever wants one shared arrangement instead of one per
browser, that is §3.22's "different decision with its own migration", not an extension of this.
