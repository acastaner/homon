import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'

import { problemDetail } from '@/lib/api'
import { useSignIn } from '@/lib/session'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/**
 * The administrator's sign-in form. Functional in phase 0 — it is what the Playwright setup
 * signs in through. Fields follow docs/design-brief.md's "Not drawn yet" spec: a `surface`
 * input, 1px `line` border, 6px radius, label above at 13px/500; the submit button is `text`
 * on `bg` inverted.
 */
export function SignInPage() {
  useDocumentTitle(pageTitle('Sign in'))

  const navigate = useNavigate()
  const location = useLocation()
  const signIn = useSignIn()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [keepSignedIn, setKeepSignedIn] = useState(false)

  const from = (location.state as { from?: string } | null)?.from ?? '/admin'

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    signIn.mutate({ email, password, keepSignedIn }, { onSuccess: () => navigate(from, { replace: true }) })
  }

  return (
    <>
      <h1 className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">
        Sign in
      </h1>
      <form
        onSubmit={onSubmit}
        aria-labelledby="sign-in-heading"
        className="flex max-w-sm flex-col gap-4 rounded-md border border-line bg-surface p-5"
      >
        <p id="sign-in-heading" className="text-[13px] font-medium text-muted">
          Administrator credentials
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="email" className="text-[13px] font-medium text-text">
            Email
          </label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            className="h-10 rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong"
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="password" className="text-[13px] font-medium text-text">
            Password
          </label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className="h-10 rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong"
          />
        </p>
        <p>
          <label className="flex items-center gap-2 text-[14px] text-text">
            <input
              type="checkbox"
              checked={keepSignedIn}
              onChange={(event) => setKeepSignedIn(event.target.checked)}
              className="size-4 rounded border-line"
            />
            Keep me signed in
          </label>
        </p>
        {signIn.isError ? (
          <p role="alert" className="rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down">
            {problemDetail(signIn.error) ?? 'Sign-in failed. Try again.'}
          </p>
        ) : null}
        <p>
          <button
            type="submit"
            disabled={signIn.isPending}
            className="inline-flex h-10 w-full items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50"
          >
            Sign in
          </button>
        </p>
      </form>
    </>
  )
}
