import { defineConfig, devices } from '@playwright/test'

/**
 * The end-to-end suite: the third gate beside `web` and `api`, run by `./ci/run-ci.sh e2e`.
 *
 * jsdom applies no CSS, so a layout that has collapsed, overflowed or hidden a control is
 * green in every unit test. Only a real engine has a layout to measure, and a dashboard that
 * is read on phones needs one measured at a phone's width.
 *
 * ## The stack
 *
 * Two servers, started by Playwright itself and torn down with it:
 *
 * 1. The API — `dotnet run` against the `homon-ci` PostgreSQL, which is the same database
 *    `./ci/run-ci.sh api` migrates.
 * 2. The SPA — `vite build && vite preview`, so the specs drive the **built bundle**. The dev
 *    server serves an unbundled module graph with different CSS ordering, and a green run
 *    against it says nothing about what nginx will hand a reader.
 *
 * The preview server proxies `/api` to the API (see `vite.config.ts`), so the whole suite
 * runs against ONE origin exactly as production does: the session cookie is first-party.
 *
 * ## Why the ports move
 *
 * 5310/5311 rather than the development 5300/5301, so a gate run cannot collide with — or
 * silently *use* — a dev server the maintainer left running. `strictPort` on the preview
 * server turns that collision into a failure instead of a quiet port bump.
 *
 * ## The two projects
 *
 * `mobile` and `desktop` run the same specs at a Pixel 7 and at 1440×900. The mobile project
 * is the one the dashboard is designed for; the desktop project is there so a mobile fix
 * cannot quietly wreck the wide layout.
 */

/** Where the built SPA is served, and the origin every spec addresses. */
const WEB_PORT = Number(process.env.HOMON_E2E_WEB_PORT ?? 5310)

/** Where the API listens. Reached only through the preview server's proxy. */
const API_PORT = Number(process.env.HOMON_E2E_API_PORT ?? 5311)

/**
 * The database the API talks to: `ci/compose.ci.yaml`'s, on loopback.
 *
 * `./ci/run-ci.sh` sets this, and the default below is the same value spelled out so that a
 * developer can run `npx playwright test` by hand against a CI database they already have up
 * (`./ci/run-ci.sh api --keep` leaves one). It is deliberately NOT the development database.
 */
const CONNECTION =
  process.env.HOMON_E2E_CONNECTION ??
  'Host=127.0.0.1;Port=55433;Database=homon;Username=homon;Password=homon'

/**
 * The administrator the specs sign in as.
 *
 * A fixed, committed, non-secret pair — the same reasoning as `POSTGRES_PASSWORD: homon` in
 * `ci/compose.ci.yaml`. This account exists only for a throwaway API bound to loopback; the
 * hash is Identity's own salted PBKDF2 of the password beside it (`e2e/admin.ts`), and
 * minting a fresh one per run would put a `hash-password` invocation on the critical path of
 * every gate. Nothing here is a credential to anything that outlives the run.
 */
const ADMIN_EMAIL = process.env.HOMON_E2E_ADMIN_EMAIL ?? 'e2e@homon.invalid'
const ADMIN_HASH =
  process.env.HOMON_E2E_ADMIN_HASH ??
  'AQAAAAIAAYagAAAAEFx3AwvSfMfo9o1zEii9TNtasx+okmBbgh4lfYVH2IFXyU0bFoFNPQrZyTReNmCs6Q=='

/** Where the API's own log is teed, so a server that dies says why in the run's output. */
export const API_LOG = 'e2e/.results/api.log'

/** Where the administrator's cookie is kept between the setup project and the specs. */
export const ADMIN_STATE = 'e2e/.results/admin.json'

export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/.results',
  // The specs share one API and one database. Serial within a file is Playwright's default;
  // this pins the file-level concurrency to one for the same reason.
  workers: 1,
  fullyParallel: false,
  // A `.only` left in a spec silently narrows the gate to one test and still exits 0.
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  timeout: 30_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL: `http://127.0.0.1:${WEB_PORT}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      // Runs once, before either viewport project: signs in as the administrator through
      // the form and saves the cookie to ADMIN_STATE.
      name: 'setup',
      testMatch: /auth\.setup\.ts/,
    },
    {
      // First of the two, and deliberately so: the dashboard is read on phones.
      name: 'mobile',
      use: { ...devices['Pixel 7'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'desktop',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
        storageState: ADMIN_STATE,
      },
      dependencies: ['setup'],
    },
  ],

  webServer: [
    {
      // ASPNETCORE_ENVIRONMENT=Development is required rather than tidy: outside Development
      // the session cookie is `SecurePolicy.Always`, and over plain HTTP the browser then
      // silently drops it — every sign-in spec would fail with no cookie and no error. It
      // also keeps the production email fail-fast out of the way, so the run needs no Resend
      // token and mails nothing.
      // `tee`, not `>`: Playwright still gets the stream (so a server that dies says why in
      // the run's output) and a spec can still read the file. `mkdir -p` first because
      // `outputDir` is created by the test run, which starts after this.
      command: [
        `mkdir -p ${API_LOG.replace(/\/[^/]+$/, '')} &&`,
        'dotnet run --project ../Homon.Api --configuration Release --no-launch-profile',
        `-- --urls http://127.0.0.1:${API_PORT}`,
        // Configuration keys are passed here rather than in `env` below, and the reason is a
        // trap worth naming: an environment variable whose NAME contains a dot does not
        // survive the shell. Playwright spawns this command through `sh -c`, and the shell
        // drops names that are not valid identifiers — so a
        // `Logging__LogLevel__Microsoft.EntityFrameworkCore.…` variable reaches nothing at
        // all, silently. Command-line configuration has no such rule, uses `:` directly, and
        // is the LAST source `WebApplication.CreateBuilder` adds — so it also outranks the
        // developer's user secrets.
        '--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning',
        '--Logging:LogLevel:Homon.Infrastructure.Email.LoggingEmailSender=Debug',
        `2>&1 | tee ${API_LOG}`,
      ].join(' '),
      url: `http://127.0.0.1:${API_PORT}/api/v1/meta`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Homon: CONNECTION,
        Administrator__Email: ADMIN_EMAIL,
        Administrator__PasswordHash: ADMIN_HASH,
        // The suite boots one host, but the machine's fs.inotify.max_user_instances is
        // shared with every other process, and `dotnet test` is often running beside this.
        // Same reasoning as ci/run-ci.sh's copy of this line.
        DOTNET_hostBuilder__reloadConfigOnChange: 'false',
        // NOT OPTIONAL. `dotnet run` in Development loads the developer's user-secrets
        // store, which may hold a real `Email:ResendApiToken`. An environment variable
        // outranks user secrets, and `IsResendConfigured` is a whitespace check, so an empty
        // value here is what puts `LoggingEmailSender` back. The suite then mails nothing at
        // all, which is the only acceptable posture for something that runs unattended.
        Email__ResendApiToken: '',
        // Emailed links must land on the SPA, and in this stack the SPA is the preview server.
        FrontEnd__PublicBaseUrl: `http://127.0.0.1:${WEB_PORT}`,
      },
      // Never reuse: an API left over from an earlier run may be on a different commit.
      reuseExistingServer: false,
      // Cold, this is a restore and a build.
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      // `build` then `preview`, always — see the note at the top of this file. A stale
      // `dist/` is the one failure mode that produces a confidently green run against code
      // that is not the code in the working tree.
      command: 'npm run build && npm run preview',
      url: `http://127.0.0.1:${WEB_PORT}/`,
      env: {
        HOMON_E2E_WEB_PORT: String(WEB_PORT),
        HOMON_E2E_API_PORT: String(API_PORT),
      },
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
})
