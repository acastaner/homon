import { NavLink, Outlet } from 'react-router'

import { useMeta } from '@/lib/meta'
import { useSession, useSignOut } from '@/lib/session'

/**
 * The page frame every route renders inside: a banner with the site navigation, the main
 * region, and a footer naming the release.
 *
 * Landmarks and accessible names are the contract with the tests — `getByRole('banner')`,
 * `getByRole('navigation', { name: 'Site' })`, `getByRole('main')` — and the design pass must
 * keep them. No `className` anywhere by design; see docs/design-brief.md.
 */
export function AppShell() {
  const session = useSession()
  const meta = useMeta()
  const signOut = useSignOut()

  return (
    <>
      <header>
        <p>
          <NavLink to="/">Homon</NavLink>
        </p>
        <nav aria-label="Site">
          <ul>
            <li>
              <NavLink to="/">Dashboard</NavLink>
            </li>
            <li>
              <NavLink to="/admin">Admin</NavLink>
            </li>
          </ul>
        </nav>
        {session.data ? (
          <p>
            <span>Signed in as {session.data.name}</span>{' '}
            <button type="button" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
              Sign out
            </button>
          </p>
        ) : null}
      </header>
      <main>
        <Outlet />
      </main>
      <footer>
        <p>
          Homon {import.meta.env.VITE_APP_VERSION || 'dev'}
          {meta.data ? ` · API ${meta.data.release}` : null}
        </p>
      </footer>
    </>
  )
}
