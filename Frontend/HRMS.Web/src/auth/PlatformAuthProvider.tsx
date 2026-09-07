import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { platformLogin, platformLogout, platformMe, platformRefresh, type PlatformUser } from '../api/platformAuth.ts'
import { platformSession, platformSessionExpiredEvent } from './platformSession.ts'

type PlatformAuthValue = {
  status: 'restoring' | 'authenticated' | 'anonymous'
  user: PlatformUser | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: string) => boolean
}
const Context = createContext<PlatformAuthValue | null>(null)

export function PlatformAuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<PlatformAuthValue['status']>(() => platformSession.hasStoredSession() ? 'restoring' : 'anonymous')
  const [user, setUser] = useState<PlatformUser | null>(null)
  useEffect(() => {
    if (!platformSession.hasStoredSession()) return
    void platformRefresh().then((tokens) => tokens ? platformMe().then((me) => { setUser(me); setStatus('authenticated') }) : setStatus('anonymous')).catch(() => setStatus('anonymous'))
  }, [])
  useEffect(() => {
    const expire = () => { setUser(null); setStatus('anonymous') }
    window.addEventListener(platformSessionExpiredEvent, expire)
    return () => window.removeEventListener(platformSessionExpiredEvent, expire)
  }, [])
  const login = useCallback(async (email: string, password: string) => { await platformLogin(email, password); setUser(await platformMe()); setStatus('authenticated') }, [])
  const logout = useCallback(async () => { await platformLogout(); setUser(null); setStatus('anonymous') }, [])
  const value = useMemo(() => ({ status, user, login, logout, can: (permission: string) => Boolean(user?.permissions.includes(permission)) }), [status, user, login, logout])
  return <Context.Provider value={value}>{children}</Context.Provider>
}

export function usePlatformAuth(): PlatformAuthValue {
  const value = useContext(Context)
  if (!value) throw new Error('usePlatformAuth must be used inside PlatformAuthProvider.')
  return value
}
