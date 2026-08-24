import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth.ts'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'

/**
 * Where `RequirePermission` sends a user who lacks what a screen needs.
 *
 * Their roles are listed, because "ask an administrator" is only actionable if you can say what you
 * currently have.
 */
export function ForbiddenPage() {
  useDocumentTitle('No access')

  const { user } = useAuth()

  return (
    <div className="state-block state-page">
      <p className="state-code">403</p>
      <p className="state-title">You do not have access to that</p>
      <p className="state-message">
        That screen needs a permission your roles do not include. An administrator can grant it.
      </p>
      {user && (
        <p className="state-hint">
          Signed in as {user.email} in {user.tenantName} with{' '}
          {user.roles.length > 0 ? user.roles.join(', ') : 'no roles'}.
        </p>
      )}
      <div className="state-action">
        <Link className="button button-primary" to="/dashboard">
          Back to dashboard
        </Link>
      </div>
    </div>
  )
}
