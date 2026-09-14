# Homon — design brief

For the design pass (plan 012). This document says what the application is, who uses it,
what every screen must contain, and what the design may not change. The maintainer
supplies the visual direction separately; a placeholder section for it is at the end.

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

- **Services**: a grid of **service cards**, one per probe. Each card carries:
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

### Admin (`/admin`, `/admin/probes`, `/admin/links`, `/admin/pages`, `/admin/api-keys`)

Desktop-first is acceptable. Each is a list with add / edit / delete:

- **Probes**: name, type (ping / SMB / HTTP / SNMP), destination, interval, failure
  threshold, and a per-type option set (SMB: share + credentials; HTTP: method, path,
  expected status/text with negation, credentials; SNMP: community/version, OID). A
  pause toggle. The current status beside each row.
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
  go in `src/Homon.Web/src/index.css` as `@theme`. **Light and dark** both; respect
  `prefers-color-scheme` and allow an explicit choice later.
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

## Design guidelines (to be supplied by the maintainer)

_Visual direction, palette, typography, tone, references — added here before the design pass
begins._
