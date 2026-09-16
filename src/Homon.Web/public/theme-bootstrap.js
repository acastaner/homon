// Sets the theme attribute before first paint, from a stored, explicit choice only — dark
// is the resident scheme for every visitor per docs/ARCHITECTURE.md §3.12, which overrides
// the more usual "respect prefers-color-scheme" on purpose. Do not add a system-preference
// read here without re-reading that section's reasoning first.
//
// External, never inline: the SPA's CSP is `default-src 'self'` with no `unsafe-inline` on
// script-src (nginx.conf), so an inline <script> here would be a silent violation once the
// CSP moves from report-only to enforced. `homon-theme` must match THEME_STORAGE_KEY in
// src/lib/theme.ts literally — a rename on one side without the other silently breaks the
// no-flash guarantee.
(function () {
  try {
    var stored = window.localStorage.getItem('homon-theme')
    if (stored === 'light') {
      document.documentElement.dataset.theme = 'light'
    }
  } catch {
    // localStorage unavailable (private browsing, disabled storage) — dark renders, same as
    // a first-ever visit. Not an error state.
  }
})()
