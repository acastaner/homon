import { useEffect, useState } from 'react'
import { RotateCw } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'

import { formatRefreshed } from '@/lib/format-refreshed'
import { fetchStatus, STATUS_QUERY_KEY } from '@/lib/status'

export function RefreshIndicator() {
  // A DISABLED observer: it subscribes to the ['status'] cache entry and re-renders when
  // DashboardPage's own useStatus() writes to it, but `enabled: false` means it never issues a
  // request of its own. That is the whole reason this can live in AppShell — a plain useStatus()
  // here would fetch /status on /admin/sign-in and on every admin route, which is exactly why
  // plan 012 left this timestamp unwired.
  const { dataUpdatedAt } = useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: fetchStatus, enabled: false })
  const queryClient = useQueryClient()

  // Re-read the wall clock every second so the text counts up between polls; without it the
  // banner would freeze at "refreshed 0 s ago" for the whole 30 s.
  //
  // The interval lives in THIS leaf component, not in AppShell, on purpose: a state update in
  // AppShell re-renders its <Outlet /> — the entire dashboard — once a second.
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(id)
  }, [])

  return (
    <div className="flex items-center gap-2.5">
      {/* dataUpdatedAt is 0 until something has populated the cache — on /admin/sign-in, and on
          the dashboard's very first paint. No timestamp then, rather than "refreshed 57 years ago".

          Deliberately NOT role="status" or aria-live: the text changes every second, and a live
          region would make a screen reader announce every tick forever. role="status" in this app
          belongs to the session check (components/require-administrator.tsx). */}
      {dataUpdatedAt === 0 ? null : (
        <span className="mono text-[12.5px] text-muted">{formatRefreshed(now - dataUpdatedAt)}</span>
      )}
      <button
        type="button"
        // No filter, deliberately: this refetches whatever the CURRENT route has mounted —
        // status, links, pages and weather on the dashboard; the admin lists under /admin — so one
        // button is correct everywhere and there is no list of query keys to keep in sync as
        // further modules land. Invalidating STATUS_QUERY_KEY alone was the alternative; it would
        // make the button a no-op on every admin page.
        onClick={() => void queryClient.invalidateQueries()}
        // h-10 w-10 is the 40px tap-target floor e2e/helpers.ts's expectTappable enforces over
        // every <button> on the page. Not cosmetic; do not shrink it.
        className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-line text-muted hover:border-line-strong hover:text-text"
      >
        <RotateCw aria-hidden="true" size={18} />
        <span className="sr-only">Refresh</span>
      </button>
    </div>
  )
}
