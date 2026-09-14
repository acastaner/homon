import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'

import { problemDetail } from '@/lib/api'
import { useSignIn } from '@/lib/session'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/**
 * The administrator's sign-in form. Functional in phase 0 — it is what the Playwright setup
 * signs in through — and unstyled.
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
      <h1>Sign in</h1>
      <form onSubmit={onSubmit} aria-labelledby="sign-in-heading">
        <p id="sign-in-heading">Administrator credentials</p>
        <p>
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </p>
        <p>
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </p>
        <p>
          <label>
            <input
              type="checkbox"
              checked={keepSignedIn}
              onChange={(event) => setKeepSignedIn(event.target.checked)}
            />{' '}
            Keep me signed in
          </label>
        </p>
        {signIn.isError ? (
          <p role="alert">{problemDetail(signIn.error) ?? 'Sign-in failed. Try again.'}</p>
        ) : null}
        <p>
          <button type="submit" disabled={signIn.isPending}>
            Sign in
          </button>
        </p>
      </form>
    </>
  )
}
