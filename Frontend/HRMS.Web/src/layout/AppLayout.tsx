import { Outlet } from 'react-router-dom'
import { Header } from './Header.tsx'
import { Sidebar } from './Sidebar.tsx'

/** The shell every signed-in screen renders inside. */
export function AppLayout() {
  return (
    <div className="app-shell">
      <Sidebar />
      <div className="app-main">
        <Header />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
