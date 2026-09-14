import path from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

/**
 * The SPA and the API are served from ONE origin — this proxy locally, the SPA container's
 * nginx in production (docs/ARCHITECTURE.md §3) — which keeps the session cookie first-party
 * and takes CORS out of the picture entirely. That is why there is no VITE_API_BASE_URL in
 * any environment: lib/api.ts's relative '/api/v1' fallback is the only value, here and in
 * the deployed bundle alike, and the tests assert exactly those relative URLs.
 *
 * `changeOrigin: false` is deliberate rather than a default left alone: the API reads the
 * Origin header for its same-origin posture, and rewriting it here would make every proxied
 * request look cross-origin.
 */
function apiProxy(target: string) {
  return { '/api': { target, changeOrigin: false } }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    port: 5300,
    proxy: apiProxy('http://localhost:5301'),
  },
  // `vite preview` serves the built bundle, and the end-to-end suite drives that rather than
  // the dev server — a spec that passes against an unbuilt module graph proves nothing about
  // what ships. Preview has no proxy of its own, so it needs the same one-origin arrangement
  // stated again here. The ports come from the environment because ci/run-ci.sh moves them
  // off 5300/5301, so an e2e run never collides with a dev server somebody left up.
  preview: {
    // 127.0.0.1 explicitly, not the `localhost` default: Vite resolves that to `::1` first,
    // and Playwright's readiness probe — and every spec's baseURL — address the v4 loopback.
    host: '127.0.0.1',
    port: Number(process.env.HOMON_E2E_WEB_PORT ?? 5310),
    strictPort: true,
    proxy: apiProxy(`http://127.0.0.1:${process.env.HOMON_E2E_API_PORT ?? '5311'}`),
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/components/ui/**', 'src/test/**', 'src/**/*.d.ts'],
    },
  },
})
