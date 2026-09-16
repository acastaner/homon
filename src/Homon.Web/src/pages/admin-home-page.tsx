import { Link } from 'react-router'

import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminHomePage() {
  useDocumentTitle(pageTitle('Admin'))

  return (
    <>
      <h1 className="border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">
        Admin
      </h1>
      <p className="text-muted">Manage what the household sees. Each section arrives with its module.</p>
      <nav aria-label="Admin sections">
        <ul className="flex flex-col divide-y divide-line rounded-md border border-line bg-surface px-4">
          {[
            { to: '/admin/probes', label: 'Probes' },
            { to: '/admin/probe-groups', label: 'Probe groups' },
            { to: '/admin/links', label: 'Links' },
            { to: '/admin/pages', label: 'Pages' },
            { to: '/admin/api-keys', label: 'API keys' },
            { to: '/admin/weather', label: 'Weather' },
          ].map((item) => (
            <li key={item.to}>
              <Link
                to={item.to}
                className="flex min-h-12 items-center text-[15px] font-medium text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
              >
                {item.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
    </>
  )
}
