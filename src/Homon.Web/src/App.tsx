import { lazy, Suspense } from 'react'
import { Outlet, Route, Routes } from 'react-router'

import { AppShell } from '@/components/app-shell'
import { RequireAdministrator } from '@/components/require-administrator'
import { DashboardPage } from '@/pages/dashboard-page'
import { PagePage } from '@/pages/page-page'
import { SignInPage } from '@/pages/sign-in-page'

// The admin pages are code-split: the family never loads them, and every one of them is
// a table-and-form the dashboard bundle has no use for. Named exports everywhere else, so
// each lazy import maps the name onto the `default` React.lazy expects.
const AdminHomePage = lazy(() => import('@/pages/admin-home-page').then((m) => ({ default: m.AdminHomePage })))
const AdminProbesPage = lazy(() => import('@/pages/admin-probes-page').then((m) => ({ default: m.AdminProbesPage })))
const AdminLinksPage = lazy(() => import('@/pages/admin-links-page').then((m) => ({ default: m.AdminLinksPage })))
const AdminPagesPage = lazy(() => import('@/pages/admin-pages-page').then((m) => ({ default: m.AdminPagesPage })))
const AdminApiKeysPage = lazy(() => import('@/pages/admin-api-keys-page').then((m) => ({ default: m.AdminApiKeysPage })))

/**
 * The route table. Everything lives under one `AppShell` (header, main, footer); the admin
 * subtree is additionally gated by `RequireAdministrator`, which renders a sign-in prompt in
 * place rather than redirecting — the URL a person typed is the URL they should keep.
 */
export default function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<DashboardPage />} />
        <Route path="pages/:slug" element={<PagePage />} />
        <Route path="admin/sign-in" element={<SignInPage />} />
        <Route
          path="admin"
          element={
            <RequireAdministrator>
              <Suspense fallback={null}>
                <AdminOutlet />
              </Suspense>
            </RequireAdministrator>
          }
        >
          <Route index element={<AdminHomePage />} />
          <Route path="probes" element={<AdminProbesPage />} />
          <Route path="links" element={<AdminLinksPage />} />
          <Route path="pages" element={<AdminPagesPage />} />
          <Route path="api-keys" element={<AdminApiKeysPage />} />
        </Route>
        {/* An unknown path is the dashboard, not a 404: on a home dashboard there is nothing
            better to show, and a stale bookmark should land somewhere useful. */}
        <Route path="*" element={<DashboardPage />} />
      </Route>
    </Routes>
  )
}

/** The admin subtree's outlet, wrapped so `RequireAdministrator` has a single child to gate. */
function AdminOutlet() {
  return <Outlet />
}
