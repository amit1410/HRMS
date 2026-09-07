import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { usePlatformAuth } from './PlatformAuthProvider.tsx'

export function RequirePlatformAuth() {
  const { status } = usePlatformAuth()
  const location = useLocation()
  if (status === 'restoring') return <p>Restoring platform session…</p>
  if (status === 'anonymous') return <Navigate to="/platform/login" replace state={{ from: location }} />
  return <Outlet />
}
