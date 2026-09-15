import { Link } from 'react-router'

import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

export function AdminHomePage() {
  useDocumentTitle(pageTitle('Admin'))

  return (
    <>
      <h1>Admin</h1>
      <p>Manage what the household sees. Each section arrives with its module.</p>
      <nav aria-label="Admin sections">
        <ul>
          <li>
            <Link to="/admin/probes">Probes</Link>
          </li>
          <li>
            <Link to="/admin/probe-groups">Probe groups</Link>
          </li>
          <li>
            <Link to="/admin/links">Links</Link>
          </li>
          <li>
            <Link to="/admin/pages">Pages</Link>
          </li>
          <li>
            <Link to="/admin/api-keys">API keys</Link>
          </li>
        </ul>
      </nav>
    </>
  )
}
