import { Link } from 'react-router-dom'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'

export function NotFoundPage() {
  useDocumentTitle('Page not found')

  return (
    <div className="state-block state-page">
      <p className="state-code">404</p>
      <p className="state-title">That page does not exist</p>
      <p className="state-message">The link may be out of date, or the screen may not be built yet.</p>
      <div className="state-action">
        <Link className="button button-primary" to="/dashboard">
          Back to dashboard
        </Link>
      </div>
    </div>
  )
}
