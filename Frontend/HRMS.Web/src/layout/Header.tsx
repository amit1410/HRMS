import { useState } from 'react'
import { useAuth } from '../auth/useAuth.ts'
import { Spinner } from '../components/Spinner.tsx'
import { initials } from '../lib/format.ts'

/**
 * Identity bar: which tenant this session belongs to, who is signed in, and the way out.
 *
 * The tenant is shown deliberately. Every request is scoped to it server-side from the token, and an
 * operator with accounts in two tenants needs to see, without checking, which one they are looking at.
 */
export function Header() {
  const { user, logout } = useAuth()
  const [signingOut, setSigningOut] = useState(false)

  async function onSignOut() {
    setSigningOut(true)
    try {
      // Never throws: it revokes the refresh token server-side where it can, and clears locally either
      // way. The guard then redirects as soon as the status flips to anonymous.
      await logout()
    } finally {
      setSigningOut(false)
    }
  }

  if (!user) return null

  return (
    <header className="app-header">
      <div className="tenant">
        <span className="tenant-label">Tenant</span>
        <span className="tenant-name">{user.tenantName}</span>
        <span className="tenant-code">{user.tenantCode}</span>
      </div>

      <div className="header-user">
        <div className="user-text">
          <span className="user-name">{user.fullName}</span>
          <span className="user-roles">{user.roles.join(' · ') || 'No roles assigned'}</span>
        </div>
        <span className="avatar" aria-hidden="true">
          {initials(user.fullName)}
        </span>
        <button
          type="button"
          className="button button-secondary"
          onClick={onSignOut}
          disabled={signingOut}
        >
          {signingOut ? <Spinner size={14} /> : 'Sign out'}
        </button>
      </div>
    </header>
  )
}
