import type { ReactNode } from 'react'
import { Link, useLocation } from 'react-router'

import { useMeta } from '@/lib/meta'
import { useSession } from '@/lib/session'

/**
 * Renders its children only to an administrator; everybody else sees an explanation and a
 * link to the sign-in page. Presentation only — the API's `Administrator` policy is the real
 * gate, and a person who defeats this component gets 401s, not data.
 *
 * Renders in place rather than redirecting: the address bar keeps the page the person
 * wanted, so signing in and pressing back lands them on it.
 */
export function RequireAdministrator({ children }: { children: ReactNode }) {
  const session = useSession()
  const meta = useMeta()
  const location = useLocation()

  if (session.isPending) {
    return (
      <p role="status" className="text-muted">
        Checking your session…
      </p>
    )
  }

  if (session.data?.kind === 'administrator') {
    return children
  }

  return (
    <section aria-labelledby="admin-gate-heading" className="flex flex-col gap-4">
      <h1
        id="admin-gate-heading"
        className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]"
      >
        Administrators only
      </h1>
      <p className="text-muted">This page changes what the household sees. Sign in to continue.</p>
      {meta.data && !meta.data.administratorConfigured ? (
        <p role="alert" className="rounded-md border border-down/40 bg-down-bg px-4 py-3 text-[14px] font-medium text-down">
          No administrator is configured on this installation yet — see the README for
          <code className="mono"> hash-password</code>.
        </p>
      ) : null}
      <p>
        <Link
          to="/admin/sign-in"
          state={{ from: location.pathname }}
          className="inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg no-underline hover:opacity-90"
        >
          Sign in
        </Link>
      </p>
    </section>
  )
}
