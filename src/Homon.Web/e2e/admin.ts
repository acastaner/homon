/**
 * The administrator the suite signs in as. The hash of this password is what
 * `playwright.config.ts` injects as `Administrator__PasswordHash`; change one and mint the
 * other with `dotnet run --project src/Homon.Api -- hash-password`.
 */
export const ADMIN_EMAIL = process.env.HOMON_E2E_ADMIN_EMAIL ?? 'e2e@homon.invalid'
export const ADMIN_PASSWORD = process.env.HOMON_E2E_ADMIN_PASSWORD ?? 'Homon-E2E-Password-1'
