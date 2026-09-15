# Homon — design brief

For the design pass (plan 012). This document says what the application is, who uses it,
what every screen must contain, and what the design may not change. The visual direction
chosen for it is the "Design guidelines" section at the end.

## What Homon is

A **home dashboard** a family opens on their phones: is the internet up, is the NAS up, is
Jellyfin up, where are the links to those things, what is the weather, what is on the
family calendar this week, did last night's backup run. One page, read in a glance, by
people who did not build it and do not want to learn it. One person — the administrator —
configures it from admin pages that can be desktop-first.

It is open source and generic: the design must not assume any particular household,
service names, or number of cards. Zero probes is a state; forty is a state.

## Who

- **Readers** — the family. Phones first (Pixel-class, 412px), tablets, a laptop on the
  kitchen counter. They never sign in. They want an answer in two seconds: "is it broken,
  and if so what". Some are children; some are grandparents.
- **The administrator** — one technical person, usually on a desktop, occasionally fixing
  something from a phone. Signs in. Adds probes, links, pages, API keys.

## Screens

### Dashboard (`/`) — the whole product

- **Services**: one section per named, non-empty `ProbeGroup` the administrator has
  created (in the administrator's own order), followed by a final section for probes in no
  group — labelled "Services" when it is the only section shown, "Other" once at least one
  named group exists. A probe belonging to two groups appears once in each. With zero
  groups, this is exactly one "Services" section, unchanged from a flat list. Within each
  section, a grid of **service cards**, one per probe. Each card carries:
  - the service name;
  - a **status dot** — green *up*, orange *unstable*, red *down*, grey *unknown* (never
    polled), and a *paused* state for a probe the admin switched off;
  - the **uptime**, overlaid as text on/near the dot, two decimals: `98.32%`. `—` when there
    is nothing to compute;
  - last checked ("2 min ago") and, for ping probes, room for a small RTT sparkline (30 days);
  - optionally a short detail line ("HTTP 503", "share unreachable").
  The grid must read at a glance: red should be findable from across a room. A card may be
  tappable to reveal detail, but the resting state carries the answer.
- **Links**: the household's bookmarks — title, optional description, always opening in a
  new tab. May be visually attached to the service card they belong to, or a separate list.
- **Weather** widget: current conditions and a short forecast for one location.
- **Calendar** widget: the coming days, several sources merged, one colour per source.
- **Backups**: a card per backup job — last run, outcome, and orange/red when late or failed.
  May live in the Services grid with a distinct glyph.
- Empty states for each section ("No probes yet — an administrator adds them under
  Admin → Probes"). A fresh installation shows only these.

### Page (`/pages/{slug}`)

An administrator-written explanation page: title and a body of prose with headings, lists,
links, maybe an image. Readable on a phone. Reached from the dashboard (a Pages list or the
links area) — the navigation between dashboard and pages is the design's call.

### Sign-in (`/admin/sign-in`)

Email, password, "Keep me signed in", one button, one error line (the server's sentence,
verbatim). A note when no administrator is configured on the installation.

### Admin (`/admin`, `/admin/probes`, `/admin/probe-groups`, `/admin/links`, `/admin/pages`, `/admin/api-keys`)

Desktop-first is acceptable. Each is a list with add / edit / delete:

- **Probes**: name, type (ping / SMB / HTTP / SNMP), destination, interval, failure
  threshold, and a per-type option set (SMB: share + credentials; HTTP: method, path,
  expected status/text with negation, credentials; SNMP: community/version, OID). A
  pause toggle. The current status beside each row. The probe form also carries a
  **Groups** fieldset — one checkbox per `ProbeGroup`, so a probe's membership is set from
  the same form that creates or edits it, with a link to `/admin/probe-groups` when none
  exist yet.
- **Probe groups**: name, order (drag or up/down); each group's own member list with its
  own order and a way to add/remove a probe. Deleting a group keeps its probes — they land
  in "Other" (or "Services" if it was the last group).
- **Links**: title, URL, description, order (drag or up/down).
- **Pages**: slug, title, published toggle, and a **WYSIWYG editor** for the body.
- **API keys**: name, created, last used, revoke; and a "new key" flow that shows the key
  **once** with a copy button and a warning that it will not be shown again.

An anonymous visitor to any admin URL sees an "Administrators only" prompt with a sign-in
link, in place — not a redirect.

### Shell

A banner with the site name and a two-item navigation (Dashboard, Admin), a "Signed in as …
/ Sign out" affordance when applicable, a main region, a footer with the release. On a
phone the navigation must remain reachable without a second row that moves sticky offsets.

## Vocabulary

up · unstable · down · unknown · paused — those five words, that order of severity. Green,
orange (not yellow — it must be distinguishable from green by a colour-blind reader and in
sunlight), red, grey, and a muted/hatched treatment for paused. Uptime always with two
decimals.

## Constraints

- **Tailwind v4 + shadcn/ui** (Base UI, `nova` style; `components.json` is committed). Tokens
  go in `src/Homon.Web/src/index.css` as `@theme`. **Light and dark** both. Dark is the
  default for every visitor and light an explicit, remembered choice (this replaced
  "respect `prefers-color-scheme`" on 15 September 2026; see `docs/ARCHITECTURE.md` §3.12).
- **No fonts have been chosen.** Self-host anything chosen (`@fontsource-*`), so no request
  leaves the origin — the SPA's CSP is `default-src 'self'`.
- **Keep the landmarks and accessible names.** The tests query `banner`, `navigation` named
  `Site` and `Admin sections`, `main`, `contentinfo`, headings by text, buttons named
  `Sign in` / `Sign out`, labels `Email` / `Password` / `Keep me signed in`, `role="alert"`
  for errors, `role="status"` for the session check. Rename only with the tests.
- **Mobile first, and measured.** Playwright runs every page at a Pixel 7 and 1440×900 and
  asserts no horizontal overflow; tappable controls should be ≥40px. Tables that must be
  wider than a phone scroll inside their own `.overflow-x-auto` container, never the page.
- **Status is not colour alone.** A glyph or a word accompanies the dot for the colour-blind.
- **Nothing household-specific** in the design: no "Jellyfin" mock data that becomes a
  layout assumption. Use placeholder names.

## Component inventory (suggested, not prescribed)

`ServiceCard`, `StatusDot` (with uptime), `Sparkline`, `LinkTile`, `WeatherWidget`,
`CalendarWidget`, `BackupCard`, `EmptyState`, `SiteHeader`, `SiteFooter`, `SignInForm`,
`AdminTable`, `ProbeForm` (per-type sections), `LinkForm`, `PageEditor`, `ApiKeyReveal`,
`AdminGate`.

## Design guidelines

**Final for implementation, 15 September 2026.** The direction is the *Status board*, dark
by default. The decision and the directions it beat are in `docs/ARCHITECTURE.md` §3.12;
the picture is `docs/design/` (desktop `Main.dc.html`, phone `BoardPhone.dc.html`, fresh
install `BoardEmpty.dc.html`). Where a picture and this text disagree, this text wins.

### Tone

A well-kept instrument panel in a warm room. Charcoal, not black; warm off-white, not
white. Hairlines and flat panels, no shadows, no gradients. Colour is reserved for
status and calendar sources; everything else is two greys of ink. Numbers are monospace
so columns align. Nothing decorative: every element is either data or a way to it.

### Schemes

- **Dark is the default for every visitor.** Light (warm paper) is an explicit choice,
  remembered per browser in `localStorage`. The chosen scheme is applied before first
  paint by a small *external* script in `<head>` — an inline one would break the CSP
  (`default-src 'self'`). Where the switch sits is the implementation's call; it must be
  a real button with an accessible name.
- Both schemes are the same token names with different values. Components never branch
  on the scheme; they read tokens.

### Colour tokens

Hex is what the artboards use; oklch is what goes in `@theme`. Every text pair below
measures at least 4.5:1 on each surface it sits on, including the tinted rows.

| Token | Dark | Light | Use |
| --- | --- | --- | --- |
| `bg` | `#1e1d1b` · oklch(23.1% 0.004 84.6) | `#f6f4ef` · oklch(96.7% 0.007 88.6) | Page ground |
| `surface` | `#262421` · oklch(26.1% 0.006 78.2) | `#fffdf9` · oklch(99.4% 0.006 84.6) | Banner, panels |
| `line` | `#3a3733` · oklch(33.8% 0.008 75.3) | `#e2ddd3` · oklch(89.9% 0.015 84.6) | Panel borders, row separators |
| `line-strong` | `#4d4943` · oklch(40.7% 0.011 78.2) | `#c9c2b4` · oklch(81.6% 0.021 84.6) | Banner rule, page-header rule, link underline, empty-state dash |
| `text` | `#ece8e0` · oklch(93.2% 0.012 84.6) | `#1c1a17` · oklch(21.9% 0.007 78.2) | Primary ink, links |
| `muted` | `#a39d92` · oklch(69.8% 0.017 82.8) | `#6b655b` · oklch(50.9% 0.017 80.6) | Labels, details, timestamps, sparkline stroke |
| `up` | `#3fb950` · oklch(69.5% 0.181 145.6) | `#1a7f37` · oklch(52.4% 0.140 148.0) | Up, succeeded |
| `unstable` | `#f0883e` · oklch(72.7% 0.153 52.8) | `#be3d04` · oklch(54.1% 0.174 38.4) | Unstable, late |
| `down` | `#fe5950` · oklch(68.5% 0.203 27.0) | `#b91c1c` · oklch(50.5% 0.190 27.5) | Down, failed |
| `unknown` | `#928c7e` · oklch(64.0% 0.020 86.2) | `#746d63` · oklch(53.9% 0.017 77.0) | Never polled |
| `unstable-bg` | `#3d2a1c` · oklch(30.4% 0.037 57.2) | `#fdeadb` · oklch(94.8% 0.029 60.8) | Row tint, unstable or late |
| `down-bg` | `#3f2222` · oklch(29.0% 0.045 20.2) | `#fbe1e1` · oklch(93.1% 0.029 17.7) | Row tint, down or failed |
| `source-1` | `#3d8ae0` · oklch(62.7% 0.150 253.4) | `#3a67d0` · oklch(54.0% 0.170 264.1) | Calendar source, blue |
| `source-2` | `#b57f1e` · oklch(63.5% 0.124 76.3) | `#946500` · oklch(54.3% 0.114 76.9) | Calendar source, amber |
| `source-3` | `#a877e3` · oklch(66.3% 0.161 303.2) | `#8d55c4` · oklch(56.3% 0.170 304.8) | Calendar source, violet |

- The three source colours pass a colour-blind separation check in both schemes (worst
  adjacent pair ΔE 25 for deuteranopia) and stay clear of the status hues. They are
  validated as a set of three. A fourth source needs a validated fourth colour, never a
  generated hue; until then sources beyond three reuse the order and the legend's text
  carries identity.
- **shadcn mapping** (nova's variables): `--background` = `bg`; `--card`, `--popover` =
  `surface`; `--foreground`, `--card-foreground`, `--primary` = `text`;
  `--primary-foreground` = `bg`; `--muted-foreground` = `muted`; `--border`, `--input` =
  `line`; `--ring` = `line-strong`; `--destructive` = `down`. No accent hue exists; do not
  introduce one.

### Type

- **IBM Plex Sans** for words, **IBM Plex Mono** for numbers, times, schedules and the
  footer. Self-hosted from Fontsource (`@fontsource/ibm-plex-sans` at 400/500/600/700 and
  `@fontsource/ibm-plex-mono` at 400/500/600, or the variable packages where Fontsource
  offers them). Fallbacks: `'Segoe UI', 'Helvetica Neue', Arial, sans-serif` and
  `'SFMono-Regular', Menlo, Consolas, monospace`.
- Mono always carries `font-variant-numeric: tabular-nums`.

| Role | Desktop | Phone |
| --- | --- | --- |
| Page title `h1` | 26px / 600 / −0.01em | 22px / 600 |
| Stat value (mono) | 22px / 600 | 18px / 600 |
| Site name | 18px / 700 | 17px / 700 |
| Nav item | 15px / 500 | 15px / 500 |
| Service or job name | 15px / 600 | 15.5px / 600 |
| Body, event title | 14.5px / 400 | 14.5px / 400 |
| Detail, uptime | 14px | 13.5px detail, 13px uptime |
| Status chip word | 13px / 600 | 13px / 600 |
| Timestamps, legend | 13px | 12.5px |
| Section label `h2` | 12px / 600 / 0.12em / uppercase | same |
| Table column header | 11.5px / 600 / 0.08em / uppercase | not shown |
| Footer (mono) | 12px | 12px |

### Space and shape

- Desktop: content column 1240px, centred; banner side padding 48px; main padding 32px top,
  48px bottom; 32px between sections. Phone: 16px side gutter, 20px top, 26px between
  sections.
- Section label sits 10px above its panel.
- Panels: `surface`, 1px `line` border, **6px radius**. The only other radius is 2px on
  legend swatches. No shadows anywhere.
- Rows: 1px `line` separator; 12px × 16px padding and 48px minimum height on desktop;
  12px × 14px and 60px on phone.
- Icons: Lucide (the `components.json` icon library), stroke style only. 16px at stroke
  2.25 inside status chips; 20px at 1.75 in weather rows; 40px at 1.5 for current
  conditions.

### Components

**Banner.** `surface` background, 1px `line-strong` bottom rule, 56px tall (52px on
phone), one row at every width. Site name left, then the Site navigation. Nav items fill
the banner's height, `muted` at rest; the current one is `text` with a 2px `text`
underline on the banner's bottom edge. On desktop a mono 12.5px `muted` "refreshed 42 s
ago" sits at the right; on phone it moves into the page header. "Signed in as … / Sign
out", when present, takes the right end of the banner on desktop and the page header row
on phone.

**Page header.** `h1` left, the stat strip right on desktop and below on phone, a 1px
`line-strong` rule under both. Stats read `5 up · 1 unstable · 1 down · 1 paused ·
99.44% uptime, 30 days`: mono value, `muted` label, and the unstable and down values in
their status colours. Every probe counts exactly once here regardless of how many
`ProbeGroup`s it belongs to — the totals and the uptime average are computed over the
probe list, not once per section.

**Status chip.** Glyph and word, never one without the other, in the status colour:

| State | Glyph | Word | Row |
| --- | --- | --- | --- |
| up | check | Up | plain |
| unstable | triangle with exclamation | Unstable | `unstable-bg`, detail text in `unstable` at 500 |
| down | cross | Down | `down-bg`, detail text in `down` at 500 |
| unknown | circle with question mark | Unknown | plain, uptime `—` |
| paused | two bars | Paused | name and detail in `muted`, 135° hatch of 10px clear / 2px `bg` |

Backup outcomes reuse the same chips: *Succeeded* (up), *Late* (unstable), *Failed* (down).

**Services.** Desktop is a table in a panel, one per dashboard section (each named group,
then "Other"/"Services") — the table structure and its columns repeat per section, each
under its own `<h2>`: Status 132px · Service 1.1fr · Detail 1.6fr · Uptime 96px,
right-aligned · 30 days 92px · Checked 120px. The header row is a real table header. Rows
keep the administrator's order within their own section (a group's own member order, or
`Probe.Position` for the ungrouped section). Phone folds each row into two lines — name and
chip, then detail and uptime — and sorts by severity *within each section independently*:
down, unstable, unknown, up, paused. A shared probe therefore may sort to a different
position in each of the sections it appears in.

**Uptime.** Mono, two decimals, `98.32%`; `—` when there is nothing to compute.

**Sparkline.** Ping probes only; other probe types leave the cell empty. 88 × 22px in the
table, 60 × 18px inline after the detail on phone. 2px `muted` stroke, rounded joins, and
a 2.5px dot on the latest point in the row's status colour. No axis, no fill.

**Backups.** Its own section, the same table: Outcome 132px · Job 1.4fr · Last run 1.4fr ·
Schedule 140px, schedule in mono `muted`. Phone folds like Services.

**Links.** A panel of rows. Title is a link in `text` with a `line-strong` underline, 3px
offset, turning `text` on hover; description in `muted` beside it on desktop, below it on
phone. Always `target="_blank"` with `rel="noopener"`.

**Weather.** A panel with 16px padding. A 40px `muted` condition icon, the temperature in
mono 30px / 500, a `muted` line of condition, wind and feels-like. Then three forecast rows:
day in `muted`, 20px icon, the condition word, and high / low in mono at the right.

**Calendar.** The section label with a legend at its right: 10px swatch, 2px radius,
source name in `muted`. Rows: the day in mono `muted` in a 76px column (64px on phone),
then events stacked, each a swatch, the title, and the time in mono `muted`.

**Empty state.** In place of the panel: `surface`, 1px dashed `line-strong` border, 6px
radius, 16px × 14px padding. A 15px / 600 title ("No probes yet") and one `muted`
sentence naming where an administrator fixes it, with a link when that place is an admin
page.

**Footer.** Mono 12px `muted`, 1px `line` top rule, "Homon 0.1.0 · API 0.1.0".

### Not drawn yet

The page view, sign-in, admin lists, probe form, probe-group admin page and API-key reveal
were not drawn. Build them from the tokens and components above: panels, table rows,
section labels, status chips, the link style and the empty state. A form field is a
`surface` input with a 1px `line` border and 6px radius, label above it in 13px / 500. A
primary button is `text` on `bg` inverted; a destructive one is `down`. The probe-group
admin page is a list-with-add/edit/delete like every other admin list, plus each group's
own member list — reuse the same row and reorder affordances the probe list already needs.
