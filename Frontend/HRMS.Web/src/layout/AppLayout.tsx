import { useRef, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useTenantBranding } from '../hooks/useTenantBranding.ts'
import { Header } from './Header.tsx'
import { Sidebar } from './Sidebar.tsx'

/** The shell every signed-in screen renders inside. */
export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const { branding } = useTenantBranding()
  const shellStyle = branding?.primaryColor ? { '--shell-accent': branding.primaryColor } as React.CSSProperties : undefined

  return (
    <div className="app-shell" style={shellStyle}>
      <Sidebar branding={branding} open={menuOpen} onClose={() => { setMenuOpen(false); menuButtonRef.current?.focus() }} />
      <div className="app-main">
        <Header onMenu={() => setMenuOpen(true)} menuButtonRef={menuButtonRef} menuOpen={menuOpen} />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
