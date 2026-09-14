import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { SignInPage } from '@/pages/sign-in-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('SignInPage', () => {
  it('posts the credentials as JSON to the sign-in endpoint', async () => {
    const calls = stubFetch({ '/api/v1/auth/sign-in': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<SignInPage />)

    await user.type(screen.getByLabelText('Email'), 'admin@example.test')
    await user.type(screen.getByLabelText('Password'), 'correct-horse-battery-staple')
    await user.click(screen.getByLabelText('Keep me signed in'))
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    const signIn = calls.find((call) => call.path === '/api/v1/auth/sign-in')

    expect(signIn?.init?.method).toBe('POST')
    expect(signIn?.init?.headers).toMatchObject({ 'Content-Type': 'application/json' })
    expect(JSON.parse(String(signIn?.init?.body))).toEqual({
      email: 'admin@example.test',
      password: 'correct-horse-battery-staple',
      keepSignedIn: true,
    })
  })

  it("surfaces the server's own sentence when sign-in fails", async () => {
    stubFetch({
      '/api/v1/auth/sign-in': {
        status: 401,
        body: { title: 'Sign-in failed', detail: 'The email address or password is incorrect.' },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<SignInPage />)

    await user.type(screen.getByLabelText('Email'), 'admin@example.test')
    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The email address or password is incorrect.',
    )
  })
})
