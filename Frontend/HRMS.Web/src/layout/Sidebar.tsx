import { useEffect, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import type { TenantBranding } from '../api/types.ts'
import { useAuth } from '../auth/useAuth.ts'
import { initials } from '../lib/format.ts'
import { visibleNavModules, type NavItem, type NavModule } from './navigation.ts'

export function Sidebar({ branding, open, onClose, collapsed = false, onToggleCollapsed, onOpenFlyout, closeFlyoutKey = 0 }: { branding: TenantBranding | null; open: boolean; onClose: () => void; collapsed?: boolean; onToggleCollapsed?: () => void; onOpenFlyout?: () => void; closeFlyoutKey?: number }) {
  const { can, user } = useAuth()
  const location = useLocation()
  const modules = visibleNavModules(can, user)
  const activeModule = modules.find(module => module.items.some(item => isItemActive(item, location.pathname)))?.id
  const [openModuleId, setOpenModuleId] = useState<NavModule['id'] | undefined>()
  const [flyoutPosition, setFlyoutPosition] = useState({ top: 12, left: 0 })
  const triggerRefs = useRef(new Map<string, HTMLButtonElement>())
  const flyoutRef = useRef<HTMLDivElement>(null)
  const activeTriggerRef = useRef<HTMLButtonElement>(null)
  const openModule = modules.find(module => module.id === openModuleId)

  function closeFlyout(restoreFocus = false) {
    setOpenModuleId(undefined)
    if (restoreFocus) window.requestAnimationFrame(() => activeTriggerRef.current?.focus())
  }

  function toggleModule(module: NavModule) {
    onOpenFlyout?.()
    const trigger = triggerRefs.current.get(module.id)
    activeTriggerRef.current = trigger ?? null
    if (openModuleId === module.id) closeFlyout()
    else setOpenModuleId(module.id)
  }

  useEffect(() => {
    if (!openModuleId || !modules.some(module => module.id === openModuleId)) setOpenModuleId(undefined)
  }, [modules, openModuleId])

  useEffect(() => {
    if (!openModuleId) return
    const updatePosition = () => {
      const trigger = triggerRefs.current.get(openModuleId)
      if (!trigger) return
      const rect = trigger.getBoundingClientRect()
      const flyoutHeight = flyoutRef.current?.getBoundingClientRect().height ?? 420
      const top = Math.max(12, Math.min(rect.top, window.innerHeight - flyoutHeight - 12))
      setFlyoutPosition({ top, left: rect.right + 10 })
    }
    updatePosition()
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)
    return () => { window.removeEventListener('resize', updatePosition); window.removeEventListener('scroll', updatePosition, true) }
  }, [openModuleId])

  useEffect(() => {
    if (!openModuleId) return
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (flyoutRef.current?.contains(target)) return
      if (Array.from(triggerRefs.current.values()).some(trigger => trigger.contains(target))) return
      closeFlyout()
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') { event.preventDefault(); closeFlyout(true) }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => { document.removeEventListener('pointerdown', onPointerDown); document.removeEventListener('keydown', onKeyDown) }
  }, [openModuleId])

  useEffect(() => { if (!open) closeFlyout() }, [open])
  useEffect(() => { closeFlyout() }, [closeFlyoutKey])

  return <>
    {open && <button type="button" className="sidebar-scrim" aria-label="Close navigation" onClick={onClose} />}
    <nav className={`sidebar${open ? ' is-open' : ''}${collapsed ? ' is-collapsed' : ''}`} aria-label="Main">
      <div className="sidebar-brand">
        {branding?.logoUrl ? <img className="shell-logo" src={branding.logoUrl} alt="" /> : <span className="sidebar-mark" aria-hidden="true">HR</span>}
        <span className="sidebar-brand-text">{branding?.displayName || user?.tenantName || 'HRMS'}</span>
        <button type="button" className="sidebar-close" onClick={onClose} aria-label="Close navigation">×</button>
      </div>
      <ul className="nav-list">
        {modules.map(module => <li key={module.id} className={`nav-module${activeModule === module.id ? ' is-active' : ''}`}>
          {module.id === 'home' ? <NavEntry item={module.items[0]!} onClose={onClose} collapsed={collapsed} /> : <button ref={element => { if (element) triggerRefs.current.set(module.id, element); else triggerRefs.current.delete(module.id) }} type="button" className={`nav-module-summary${openModuleId === module.id ? ' is-open' : ''}`} title={collapsed ? module.label : undefined} aria-expanded={openModuleId === module.id} aria-controls={`nav-flyout-${module.id}`} onClick={() => toggleModule(module)}>
            <ModuleIcon module={module} /><span className="nav-module-label">{module.label}</span><span className="nav-module-chevron" aria-hidden="true">›</span>
          </button>}
        </li>)}
      </ul>
      {openModule ? <div ref={flyoutRef} id={`nav-flyout-${openModule.id}`} className="sidebar-flyout" aria-label={`${openModule.label} navigation`} style={{ '--flyout-top': `${flyoutPosition.top}px`, '--flyout-left': `${flyoutPosition.left}px` } as React.CSSProperties}>
        <div className="sidebar-flyout-header"><div><span className="sidebar-flyout-eyebrow">Navigate</span><h2>{openModule.label}</h2></div><button type="button" className="sidebar-flyout-close" aria-label={`Close ${openModule.label} navigation`} onClick={() => closeFlyout(true)}>×</button></div>
        <div className="sidebar-flyout-list">{flyoutSections(openModule).map(section => <section className="sidebar-flyout-section" key={section.label}>
          {section.showHeading && <h3 className="sidebar-flyout-section-heading">{section.label}</h3>}
          <ul className="sidebar-flyout-section-list">{section.items.map(item => <li key={item.to}><NavEntry item={item} onClose={() => { closeFlyout(); onClose() }} collapsed={false} /></li>)}</ul>
        </section>)}</div>
      </div> : null}
      <div className="sidebar-footer">
        {onToggleCollapsed && <button type="button" className="sidebar-collapse-toggle" onClick={onToggleCollapsed} aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'} title={collapsed ? 'Expand navigation' : 'Collapse navigation'}>{collapsed ? '›' : '‹'}<span>{collapsed ? 'Expand' : 'Collapse'}</span></button>}
        {user && <div className="sidebar-profile"><span className="sidebar-profile-avatar">{initials(user.fullName)}</span><div className="sidebar-profile-details"><strong className="sidebar-profile-name">{user.fullName}</strong><span className="sidebar-profile-role">{user.roles[0] || 'User'}</span><span className="sidebar-profile-status"><span className="sidebar-profile-status-dot" aria-hidden="true" />Online</span></div></div>}
      </div>
    </nav>
  </>
}

function NavEntry({ item, onClose, collapsed }: { item: NavItem; onClose: () => void; collapsed: boolean }) {
  const content = <><NavItemIcon label={item.label} /><span className="nav-item-label">{item.label}</span>{!collapsed && !item.available && <span className="nav-soon">soon</span>}</>
  return item.available ? <NavLink to={item.to} end={item.to === '/dashboard'} className={({ isActive }) => isActive ? 'nav-link is-active' : 'nav-link'} onClick={onClose} title={collapsed ? item.label : undefined}>{content}</NavLink> : <span className="nav-link is-disabled" aria-disabled="true" title={collapsed ? item.label : undefined}>{content}</span>
}

function isItemActive(item: NavItem, pathname: string) { return item.to === '/dashboard' ? pathname === item.to : pathname === item.to || pathname.startsWith(`${item.to}/`) }
function flyoutSections(module: NavModule) {
  const hasSections = module.items.some(item => item.flyoutSection)
  const sections = new Map<string, NavItem[]>()
  module.items.forEach(item => {
    const label = hasSections ? (item.flyoutSection ?? 'More') : ''
    const items = sections.get(label) ?? []
    items.push(item)
    sections.set(label, items)
  })
  return Array.from(sections, ([label, items]) => ({ label, items, showHeading: hasSections }))
}

function ModuleIcon({ module }: { module: NavModule }) {
  const paths: Record<NavModule['id'], string> = {
    home: 'M4 11.5 12 5l8 6.5M6.5 10v9h11v-9M10 19v-5h4v5',
    people: 'M9 10a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM3.5 19c.4-3 2.2-4.5 5.5-4.5s5.1 1.5 5.5 4.5M16 5.5a3 3 0 1 1 0 5.7M17 14.7c2 .5 3.2 1.9 3.5 4.3',
    attendance: 'M4 7h16M7 4v6m10-6v6M6 20h12a2 2 0 0 0 2-2V7H4v11a2 2 0 0 0 2 2Z',
    leave: 'M7 3v3m10-3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm3 8 2 2 4-4',
    payroll: 'M4 7h16v10H4zM7 11h4m-4 3h7M7 4h10a2 2 0 0 1 2 2v1H5V6a2 2 0 0 1 2-2Z',
    separation: 'M13 5H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h7M15 8l4 4-4 4M9 12h10',
    reports: 'M5 19V5h14v14M8 16v-3m4 3V9m4 7v-5',
    communication: 'M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v7a2.5 2.5 0 0 1-2.5 2.5H11l-4.5 3v-3H6.5A2.5 2.5 0 0 1 4 13.5zM8 9h8m-8 3h5',
    administration: 'M12 3 19 6v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3Zm-3 9 2 2 4-4',
    workspace: 'M6 4h12v16H6zM9 8h6M9 12h6M9 16h4',
  }
  return <span className="nav-module-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d={paths[module.id]} /></svg></span>
}

function NavItemIcon({ label }: { label: string }) {
  const path = label === 'Dashboard' ? 'M4 11.5 12 5l8 6.5M6.5 10v9h11v-9M10 19v-5h4v5' : label.startsWith('My ') ? 'M9 10a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM3.5 19c.4-3 2.2-4.5 5.5-4.5s5.1 1.5 5.5 4.5M16 5.5a3 3 0 1 1 0 5.7M17 14.7c2 .5 3.2 1.9 3.5 4.3' : label.includes('Leave') ? 'M7 3v3m10-3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Z' : label.includes('Attendance') ? 'M4 7h16M7 4v6m10-6v6M6 20h12a2 2 0 0 0 2-2V7H4v11a2 2 0 0 0 2 2Z' : label.includes('Report') ? 'M5 19V5h14v14M8 16v-3m4 3V9m4 7v-5' : label.includes('Role') || label.includes('Access') ? 'M12 3 19 6v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3Z' : 'M5 5h14v14H5zM8 9h8M8 13h5'
  return <span className="nav-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d={path} /></svg></span>
}
