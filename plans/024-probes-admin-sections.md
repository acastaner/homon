# 024 — The probe admin page lists probes the way the dashboard does

Status: in review on `plan/024-probes-admin-sections` (2026-10-07). Size S. Builds on 002, 012, 023.

## Why

`/admin/probes` was one flat list with five labelled buttons per probe ("Move Jellyfin down",
"Edit Jellyfin", …), ungrouped, with the add/edit form at the very bottom. Worse than dense, it
was misleading: its move buttons reordered `Probe.Position`, which the dashboard uses only for the
ungrouped section. A grouped probe sits in its group's own member order (set on the probe-groups
page), so moving it here changed nothing anyone could see.

## Design

Drawn on the Claude Design canvas "Homon Probes admin",
<https://claude.ai/artifact/8pJAFif7mfa8MyfkX6zfGR>: three desktop directions — A "Sections
table", B "Split view" with a side editor, C "Group cards" folding the probe-groups page in — and A
at 412px. **The maintainer chose A.** The canvas is the only copy; there are no `*.dc.html` files
for it in `docs/design/`.

## Decisions

- **D1 — Sections mirror the dashboard's default layout.** One section per probe group in group
  order, each in that group's member order, then the ungrouped rest in `Probe.Position` order under
  the dashboard's own label rule ("Services" while no group has members, "Other" once one does).
  A probe in two groups is two rows, each marked "also in …". Unlike the dashboard, an empty group
  is shown — with a note that the dashboard leaves it out — because an administrator needs to see
  it to fill it. `lib/probe-sections.ts` (`probeSections`).
- **D2 — Moving acts on the order that section actually uses.** In a group: `PUT
  /probe-groups/{id}/members` with the swapped list, the endpoint the probe-groups page already
  calls. In the ungrouped section: `PUT /probes/order` with only the two probes swapped, so grouped
  probes interleaved between them keep their positions (`moveWithinSection`). No API change.
- **D3 — Icon-only row actions, 40px.** Up, down, edit, pause/unpause, delete as Lucide icons with
  the probe's name in each `aria-label`, so every existing query ("Move NAS up", "Unpause NAS",
  "Confirm delete NAS") still finds them. 40px, not the canvas's 32px, because
  `e2e/layout.spec.ts` holds every admin button to that floor.
- **D4 — Edit opens inside the row; the add form steps aside meanwhile.** The form's inputs carry
  fixed ids, so only one form is ever rendered. The header's "New probe" jumps to the add form.
- **D5 — Delete confirms in a strip under the row,** not in the action cell: every row shares one
  grid template with a fixed 216px action column, and the question needs room to say a shared
  probe leaves its other groups too.
- **D6 — Below `lg` a row folds** into name and status, then kind · target · interval, then the
  actions, as the 412px artboard draws it.

## Verification

`web` → 203 tests (191 before; `lib/probe-sections.test.ts` and four new page tests). `e2e` → 125
at both viewport projects, unchanged. No C# changed, so `api` was not re-run. Rendered in Chromium
from the built SPA against eleven stubbed probes in four groups (one shared, one empty, every
status) at 1440, 1100 and 412px: no horizontal overflow, no button under 40px.

## Not done

- At 640–1240px the app shell's `<main>` has no side gutter (`sm:px-0` in `app-shell.tsx`), so
  this page — like every page — touches the window edge there. Pre-existing; left for its own
  change.
