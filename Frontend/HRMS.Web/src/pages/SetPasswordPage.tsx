import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { setPassword } from '../api/auth.ts'
import { toApiError } from '../api/errors.ts'

export function SetPasswordPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const token = params.get('token') ?? ''
  const [password, setPasswordValue] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)
  const [saving, setSaving] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault(); setError('')
    if (!token) { setError('This invitation is invalid or has expired.'); return }
    if (password.length < 8 || password.length > 128 || !/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/\d/.test(password)) {
      setError('Password must be 8-128 characters and include upper, lower, and numeric characters.'); return
    }
    if (password !== confirmPassword) { setError('Passwords do not match.'); return }
    setSaving(true)
    try { await setPassword({ token, password, confirmPassword }); setDone(true) } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }

  if (done) return <main><h1>Your password has been set.</h1><p>You can now sign in.</p><Link to="/login">Go to login</Link></main>

  return <main><h1>Set your password</h1><p>Choose a password for your employee portal account.</p><form onSubmit={(event) => void submit(event)}><label>Password<input required type="password" autoComplete="new-password" value={password} onChange={(event) => setPasswordValue(event.target.value)} /></label><label>Confirm password<input required type="password" autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} /></label><p>Password must be 8-128 characters and include upper, lower, and numeric characters.</p>{error && <p role="alert">{error}</p>}<button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Set password'}</button>{' '}<button type="button" onClick={() => navigate('/login')}>Cancel</button></form></main>
}
