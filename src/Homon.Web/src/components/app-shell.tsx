import { NavLink, Outlet } from 'react-router'

import { ThemeToggle } from '@/components/theme-toggle'
import { useMeta } from '@/lib/meta'
import { useSession, useSignOut } from '@/lib/session'

/**
 * The page frame every route renders inside: a banner with the site navigation, the main
 * region, and a footer naming the release.
 *
 * Landmarks and accessible names are the contract with the tests — `getByRole('banner')`,
 * `getByRole('navigation', { name: 'Site' })`, `getByRole('main')` — and the design pass must
 * keep them. The outer `<div>` below is not a landmark-changing element (only
 * article/aside/main/nav/section strip a nested `<header>`'s implicit `banner` role), so it
 * is safe for layout only.
 *
 * The brief (`docs/design-brief.md`'s Banner rule) moves "Signed in as … / Sign out" into
 * the page header row on phone. Deliberately not done here: `e2e/admin.spec.ts` asserts that
 * exact text is visible at *both* viewport projects in the same test run, so a responsive
 * `hidden`/`md:flex` pair would either duplicate the node (breaking every `getByRole`/
 * `getByText` strict-mode query that expects exactly one match) or hide it outright on
 * mobile. One instance, always visible, wrapping onto a second line on narrow viewports
 * instead — the pragmatic reading of Decision 7's hard boundary.
 */
export function AppShell() {
  const session = useSession()
  const meta = useMeta()
  const signOut = useSignOut()

  return (
    <div className="flex min-h-screen flex-col bg-bg text-text">
      <header className="flex min-h-[52px] shrink-0 flex-wrap items-center gap-x-6 gap-y-2 border-b border-line-strong bg-surface px-4 py-2 sm:min-h-14 sm:gap-x-8 sm:px-12 sm:py-0">
        <p className="shrink-0">
          <NavLink to="/" className="text-[17px] font-bold no-underline sm:text-lg">
            Homon
          </NavLink>
        </p>
        <nav aria-label="Site" className="flex h-[52px] items-center sm:h-14">
          <ul className="flex h-full list-none items-center gap-5 sm:gap-6">
            <li className="h-full">
              <NavLink
                to="/"
                end
                className={({ isActive }) =>
                  `flex h-full items-center border-b-2 text-[15px] font-medium no-underline ${
                    isActive ? 'border-text text-text' : 'border-transparent text-muted'
                  }`
                }
              >
                Dashboard
              </NavLink>
            </li>
            <li className="h-full">
              <NavLink
                to="/admin"
                className={({ isActive }) =>
                  `flex h-full items-center border-b-2 text-[15px] font-medium no-underline ${
                    isActive ? 'border-text text-text' : 'border-transparent text-muted'
                  }`
                }
              >
                Admin
              </NavLink>
            </li>
          </ul>
        </nav>
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
      </header>
      <main className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-8 px-4 pt-5 pb-8 sm:gap-8 sm:px-0 sm:pt-8 sm:pb-12">
        <Outlet />
      </main>
      <footer className="mono border-t border-line px-4 py-3.5 text-[12px] text-muted sm:px-12">
        <p>
          Homon {import.meta.env.VITE_APP_VERSION || 'dev'}
          {meta.data ? ` · API ${meta.data.release}` : null}
        </p>
      </footer>
    </div>
  )
}
