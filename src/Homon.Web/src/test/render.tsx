import { StrictMode, type ReactElement, type ReactNode } from 'react'
import { render, type RenderOptions } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

/**
 * Renders a component inside the providers the app supplies at runtime. Retries are off so
 * a failing query fails the test immediately instead of stalling it.
 *
 * The nesting mirrors main.tsx exactly — `StrictMode` outermost, wrapping
 * `QueryClientProvider` — and that order is load-bearing rather than cosmetic: only the
 * app's nesting detaches a TanStack mutation from its observer across StrictMode's
 * development remount, so an inverted order here can pass a component that hangs in the
 * browser. Do not reorder these providers to make a test go green.
 */
export function renderWithProviders(
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'> & { initialEntries?: string[] },
) {
  const { initialEntries = ['/'], ...renderOptions } = options ?? {}

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <StrictMode>
        <QueryClientProvider client={queryClient}>
          <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>
        </QueryClientProvider>
      </StrictMode>
    )
  }

  return render(ui, { wrapper: Wrapper, ...renderOptions })
}
