import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { toApiError } from '../../api/errors.ts'
import { usePlatformAuth } from '../../auth/PlatformAuthProvider.tsx'

export function PlatformLoginPage() {
  const { login } = usePlatformAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError('')
    try { await login(email, password); navigate('/platform/tenants', { replace: true }) } catch (reason) { setError(toApiError(reason).message) }
  }
  return <main><h1>Platform administration</h1><p>Sign in with your platform administrator account.</p><form onSubmit={(event) => void submit(event)}><label>Email<input required type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label><label>Password<input required type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>{error && <p role="alert">{error}</p>}<button type="submit">Sign in</button></form></main>
}
