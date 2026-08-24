import type { ReactNode } from 'react'
import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from './useAuth.ts'

interface RequirePermissionProps {
  /** A single permission the route needs. */
  permission?: string
  /** Or several, any one of which is enough (a screen reachable by more than one role). */
  anyOf?: readonly string[]
  /** Omit to guard nested routes via `<Outlet />`; pass children to guard a subtree inline. */
  children?: ReactNode
}

/**
 * Keeps a user out of a screen their permissions do not cover.
 *
 * This is a courtesy, not a control: it replaces a page full of 403s with one clear explanation. The
 * API enforces the same rule with `[HasPermission]`, so editing the bundle to reach the route buys
 * nothing but an empty screen.
 */
export function RequirePermission({ permission, anyOf, children }: RequirePermissionProps) {
  const { can, canAny } = useAuth()

  const required = anyOf ?? (permission ? [permission] : [])
  const allowed = required.length === 0 || (anyOf ? canAny(anyOf) : can(required[0] ?? ''))

  if (!allowed) {
    return <Navigate to="/forbidden" replace />
  }

  return <>{children ?? <Outlet />}</>
}
