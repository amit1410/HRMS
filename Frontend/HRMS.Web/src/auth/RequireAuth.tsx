import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { FullPageSpinner } from '../components/Spinner.tsx'
import { useAuth } from './useAuth.ts'

/**
 * Route guard for everything behind sign-in.
 *
 * While the session is being restored it renders a spinner rather than deciding — redirecting during
 * that window would bounce a signed-in user to the login form on every hard refresh.
 *
 * The attempted location travels in `state.from` so that signing in returns the user to the page they
 * asked for, not to a generic landing screen.
 */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'restoring') {
    return <FullPageSpinner label="Restoring your session…" />
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}
