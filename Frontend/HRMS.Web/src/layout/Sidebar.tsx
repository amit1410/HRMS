import { useEffect } from 'react'
import { NavLink } from 'react-router-dom'
import type { TenantBranding } from '../api/types.ts'
import { useAuth } from '../auth/useAuth.ts'
import { initials } from '../lib/format.ts'
import { visibleNavItems } from './navigation.ts'

export function Sidebar({ branding, open, onClose }: { branding: TenantBranding | null; open: boolean; onClose: () => void }) {
  const { can, user } = useAuth()
  const items = visibleNavItems(can)
  useEffect(() => {
    if (!open) return
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [onClose, open])

  return (
    <nav className={`sidebar${open ? ' is-open' : ''}`} aria-label="Main">
      <div className="sidebar-brand">
        {branding?.logoUrl ? <img className="shell-logo" src={branding.logoUrl} alt="" /> : <span className="sidebar-mark" aria-hidden="true">HR</span>}
        <span className="sidebar-brand-text">{branding?.displayName || user?.tenantName || 'HRMS'}</span>
        <button type="button" className="sidebar-close" onClick={onClose} aria-label="Close navigation">×</button>
      </div>

      <ul className="nav-list">
        {items.map((item) =>
          item.available ? (
            <li key={item.to}>
              <NavLink
                to={item.to}
                className={({ isActive }) => (isActive ? 'nav-link is-active' : 'nav-link')}
                onClick={onClose}
              >
                <NavItemIcon label={item.label} />{item.label}
              </NavLink>
            </li>
          ) : (
            <li key={item.to}>
              {/* Not a link: the screen does not exist yet, and a dead link is worse than a marked one. */}
              <span className="nav-link is-disabled" aria-disabled="true">
                <NavItemIcon label={item.label} />{item.label}
                <span className="nav-soon">soon</span>
              </span>
            </li>
          ),
        )}
      </ul>
      {user && <div className="sidebar-profile"><span className="sidebar-profile-avatar">{initials(user.fullName)}</span><span className="sidebar-profile-text"><strong>{user.fullName}</strong><small>{user.roles[0] || 'User'}</small></span></div>}
    </nav>
  )
}

function NavItemIcon({ label }: { label: string }) { return <span className="nav-icon" aria-hidden="true">{label === 'Dashboard' ? '⌂' : label === 'Employees' ? '♙' : label === 'Departments' ? '▦' : label === 'Designations' ? '✦' : '⚙'}</span> }
