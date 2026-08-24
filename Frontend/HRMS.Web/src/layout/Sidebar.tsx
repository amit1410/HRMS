import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/useAuth.ts'
import { visibleNavItems } from './navigation.ts'

export function Sidebar() {
  const { can } = useAuth()
  const items = visibleNavItems(can)

  return (
    <nav className="sidebar" aria-label="Main">
      <div className="sidebar-brand">
        <span className="sidebar-mark" aria-hidden="true">
          HR
        </span>
        <span className="sidebar-brand-text">HRMS</span>
      </div>

      <ul className="nav-list">
        {items.map((item) =>
          item.available ? (
            <li key={item.to}>
              <NavLink
                to={item.to}
                className={({ isActive }) => (isActive ? 'nav-link is-active' : 'nav-link')}
              >
                {item.label}
              </NavLink>
            </li>
          ) : (
            <li key={item.to}>
              {/* Not a link: the screen does not exist yet, and a dead link is worse than a marked one. */}
              <span className="nav-link is-disabled" aria-disabled="true">
                {item.label}
                <span className="nav-soon">soon</span>
              </span>
            </li>
          ),
        )}
      </ul>
    </nav>
  )
}
