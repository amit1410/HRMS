import { useEffect, useRef, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useTenantBranding } from '../hooks/useTenantBranding.ts'
import { Header } from './Header.tsx'
import { Sidebar } from './Sidebar.tsx'
import { CommandPalette } from './CommandPalette.tsx'

/** The shell every signed-in screen renders inside. */
export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const searchButtonRef = useRef<HTMLButtonElement>(null)
  const [searchOpen, setSearchOpen] = useState(false)
  const [closeFlyoutKey, setCloseFlyoutKey] = useState(0)
  const { branding } = useTenantBranding()
  const shellStyle = branding?.primaryColor ? { '--shell-accent': branding.primaryColor } as React.CSSProperties : undefined

  useEffect(() => {
    function onShortcut(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setCloseFlyoutKey(value => value + 1)
        setSearchOpen(true)
      }
    }
    document.addEventListener('keydown', onShortcut)
    return () => document.removeEventListener('keydown', onShortcut)
  }, [])

  return (
    <div className="app-shell" style={shellStyle}>
      <Sidebar branding={branding} open={menuOpen} collapsed={sidebarCollapsed} closeFlyoutKey={closeFlyoutKey} onOpenFlyout={() => setSearchOpen(false)} onToggleCollapsed={() => setSidebarCollapsed(value => !value)} onClose={() => { setMenuOpen(false); menuButtonRef.current?.focus() }} />
      <div className="app-main">
        <Header onMenu={() => setMenuOpen(true)} menuButtonRef={menuButtonRef} menuOpen={menuOpen} onSearch={() => { setCloseFlyoutKey(value => value + 1); setSearchOpen(true) }} searchButtonRef={searchButtonRef} />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
      <CommandPalette open={searchOpen} onClose={() => setSearchOpen(false)} triggerRef={searchButtonRef} />
    </div>
  )
}
