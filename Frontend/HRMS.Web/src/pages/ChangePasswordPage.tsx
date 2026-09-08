import { useState, type FormEvent, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { changePassword } from '../api/passwordRecovery.ts'
import { toApiError } from '../api/errors.ts'
import { useAuth } from '../auth/useAuth.ts'

const policyText = 'Password must be 8-128 characters and include upper, lower, and numeric characters.'

export function ChangePasswordPage() {
  const navigate = useNavigate()
  const { logout } = useAuth()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    if (!current) { setError('Current Password is required.'); return }
    if (next.length < 8 || next.length > 128 || !/[A-Z]/.test(next) || !/[a-z]/.test(next) || !/\d/.test(next)) { setError(policyText); return }
    if (next !== confirm) { setError('Passwords do not match.'); return }
    setBusy(true)
    try { await changePassword(current, next, confirm); await logout(); setDone(true) } catch (reason) { setError(toApiError(reason).message) } finally { setBusy(false) }
  }

  if (done) return <main className="change-password-page change-password-success-page"><div className="change-password-success" role="status"><div className="change-password-success-icon" aria-hidden="true"><CheckIcon /></div><p className="change-password-eyebrow">ACCOUNT SETTINGS</p><h1>Password changed</h1><p>For your security, you have been signed out. Please sign in again using your new password.</p><button type="button" className="change-password-primary" onClick={() => navigate('/login')}>Go to Sign In <ArrowIcon /></button></div></main>

  const requirements = [
    { label: '8-128 characters', met: next.length >= 8 && next.length <= 128 },
    { label: 'At least one uppercase letter', met: /[A-Z]/.test(next) },
    { label: 'At least one lowercase letter', met: /[a-z]/.test(next) },
    { label: 'At least one numeric character', met: /\d/.test(next) },
  ]

  return <main className="change-password-page">
    <div className="change-password-heading"><div><p className="change-password-eyebrow">ACCOUNT SETTINGS</p><h1>Change Password</h1><p>Keep your account secure with a strong and unique password.</p></div><nav className="change-password-breadcrumb" aria-label="Breadcrumb"><span>Home</span><span aria-hidden="true">/</span><span>My Profile</span><span aria-hidden="true">/</span><strong>Change Password</strong></nav></div>
    <div className="change-password-grid">
      <section className="change-password-card change-password-form-card" aria-labelledby="update-password-title">
        <div className="change-password-card-header"><div className="change-password-lock-badge" aria-hidden="true"><LockIcon /></div><div><h2 id="update-password-title">Update Your Password</h2><p>Enter your current password and choose a new password for your account.</p></div></div>
        <form className="change-password-form" onSubmit={(event) => void submit(event)} noValidate>
          <PasswordField id="current-password" label="Current Password" autoComplete="current-password" value={current} onChange={setCurrent} />
          <PasswordField id="new-password" label="New Password" autoComplete="new-password" value={next} onChange={setNext} />
          <PasswordField id="confirm-password" label="Confirm New Password" autoComplete="new-password" value={confirm} onChange={setConfirm} />
          {error && <p className="change-password-error" role="alert">{error}</p>}
          <div className="change-password-actions"><button type="submit" className="change-password-primary" disabled={busy}>{busy ? 'Changing Password…' : 'Change Password'} <ArrowIcon /></button><button type="button" className="change-password-secondary" onClick={() => navigate('/my-profile')}>Cancel</button></div>
        </form>
      </section>
      <aside className="change-password-side-column">
        <section className="change-password-card change-password-requirements" aria-labelledby="password-requirements-title"><div className="change-password-section-heading"><div className="change-password-section-icon" aria-hidden="true"><ShieldIcon /></div><div><h2 id="password-requirements-title">Password Requirements</h2><p>Your password must meet the following criteria:</p></div></div><ul className="change-password-requirement-list">{requirements.map((requirement) => <li className={requirement.met ? 'is-met' : ''} key={requirement.label}><span aria-hidden="true"><CheckIcon /></span>{requirement.label}</li>)}</ul></section>
        <section className="change-password-security-note" aria-labelledby="security-note-title"><div className="change-password-note-icon" aria-hidden="true"><LockIcon /></div><div><h2 id="security-note-title">Security Note</h2><p>After changing your password, you will be signed out for security reasons. Please sign in again using your new password.</p></div></section>
        <section className="change-password-tips" aria-labelledby="password-tips-title"><h2 id="password-tips-title">Tips for a stronger password</h2><ul><li>Use a combination of letters, numbers, and symbols</li><li>Avoid using personal information</li><li>Do not reuse passwords from other accounts</li></ul></section>
      </aside>
    </div>
  </main>
}

function PasswordField({ id, label, autoComplete, value, onChange }: { id: string; label: string; autoComplete: string; value: string; onChange: (value: string) => void }) {
  const [visible, setVisible] = useState(false)
  return <div className="change-password-field"><label htmlFor={id}>{label}</label><div className="change-password-input-wrap"><LockIcon /><input id={id} name={id} type={visible ? 'text' : 'password'} autoComplete={autoComplete} value={value} onChange={(event) => onChange(event.target.value)} required /><button type="button" className="change-password-visibility" onClick={() => setVisible((wasVisible) => !wasVisible)} aria-label={visible ? `Hide ${label}` : `Show ${label}`} aria-pressed={visible}>{visible ? <EyeOffIcon /> : <EyeIcon />}</button></div></div>
}

function Icon({ children }: { children: ReactNode }) { return <svg viewBox="0 0 24 24" aria-hidden="true">{children}</svg> }
function LockIcon() { return <Icon><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v2" /></Icon> }
function ShieldIcon() { return <Icon><path d="M12 3.5 19 6v5.2c0 4.2-2.8 7.8-7 9.3-4.2-1.5-7-5.1-7-9.3V6l7-2.5Z" /><path d="m9 12 2 2 4-4" /></Icon> }
function CheckIcon() { return <Icon><path d="m5 12 4.5 4.5L19 7" /></Icon> }
function ArrowIcon() { return <Icon><path d="M5 12h13M13 6l6 6-6 6" /></Icon> }
function EyeIcon() { return <Icon><path d="M2.5 12s3.5-5 9.5-5 9.5 5 9.5 5-3.5 5-9.5 5-9.5-5-9.5-5Z" /><circle cx="12" cy="12" r="2.5" /></Icon> }
function EyeOffIcon() { return <Icon><path d="m3 3 18 18M10.6 7.3A10.8 10.8 0 0 1 12 7c6 0 9.5 5 9.5 5a17 17 0 0 1-3.2 3.3M6.5 6.8C3.9 8.4 2.5 12 2.5 12s3.5 5 9.5 5c1.1 0 2.1-.2 3-.5" /><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" /></Icon> }
