import { useState, type FormEvent, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { toApiError } from '../../api/errors.ts'
import { usePlatformAuth } from '../../auth/PlatformAuthProvider.tsx'

export function PlatformLoginPage() {
  const { login } = usePlatformAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [passwordVisible, setPasswordVisible] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    try {
      await login(email, password)
      navigate('/platform/tenants', { replace: true })
    } catch (reason) {
      setError(toApiError(reason).message)
    }
  }

  return <main className="platform-login-page">
    <section className="platform-login-brand-panel" aria-labelledby="platform-brand-heading">
      <div className="platform-login-brand-content">
        <div className="platform-login-brand-mark"><span>HR</span></div>
        <div className="platform-login-brand-name">Anevra Technologies</div>
        <p className="platform-login-eyebrow">Platform administration</p>
        <div className="platform-login-hero">
          <p className="platform-login-kicker">One secure command center</p>
          <h1 id="platform-brand-heading">Manage Multiple Tenants Effortlessly</h1>
          <p>Create, configure and manage your organization tenants from a single, secure platform.</p>
        </div>
        <TenantOverview />
        <div className="platform-login-features">
          <Feature icon={<BuildingsIcon />} title="Centralized Tenant Management" text="Create and manage multiple organizations" />
          <Feature icon={<ShieldIcon />} title="Secure & Scalable" text="Enterprise-grade security" />
          <Feature icon={<SettingsIcon />} title="Complete Control" text="Monitor, configure and grow with ease" />
        </div>
      </div>
      <div className="platform-login-brand-footer"><span>One Platform. Many Possibilities.</span><small>Built for a brighter workday</small></div>
      <span className="platform-login-orb platform-login-orb-one" aria-hidden="true" />
      <span className="platform-login-orb platform-login-orb-two" aria-hidden="true" />
    </section>

    <section className="platform-login-form-panel" aria-labelledby="platform-login-heading">
      <div className="platform-login-form-shell">
        <div className="platform-login-form-card">
          <div className="platform-login-form-header">
            <p className="platform-login-form-eyebrow">Anevra Technologies</p>
            <h2 id="platform-login-heading">Platform administration</h2>
            <p>Sign in with your platform administrator account.</p>
          </div>
          {error && <div className="platform-login-alert" role="alert" aria-live="polite"><AlertIcon /><span>{error}</span></div>}
          <form className="platform-login-form" onSubmit={(event) => void submit(event)}>
            <div className="platform-login-field">
              <label htmlFor="platform-email">Email</label>
              <div className="platform-login-input-wrap"><EmailIcon /><input id="platform-email" name="email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" required /></div>
            </div>
            <div className="platform-login-field">
              <label htmlFor="platform-password">Password</label>
              <div className="platform-login-input-wrap"><LockIcon /><input id="platform-password" name="password" type={passwordVisible ? 'text' : 'password'} value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" required /><button className="platform-login-password-toggle" type="button" onClick={() => setPasswordVisible((visible) => !visible)} aria-label={passwordVisible ? 'Hide password' : 'Show password'}>{passwordVisible ? <EyeOffIcon /> : <EyeIcon />}</button></div>
            </div>
            <button className="platform-login-submit" type="submit"><span>Sign in</span><ArrowIcon /></button>
          </form>
        </div>
        <p className="platform-login-copyright">© 2026 Anevra Technologies. All rights reserved.</p>
      </div>
    </section>
  </main>
}

function Feature({ icon, title, text }: { icon: ReactNode; title: string; text: string }) {
  return <div className="platform-login-feature"><span className="platform-login-feature-icon">{icon}</span><span><strong>{title}</strong><small>{text}</small></span></div>
}

function TenantOverview() {
  return <div className="platform-login-overview" aria-label="Tenant management overview">
    <div className="platform-login-overview-top"><span><BuildingsIcon /> Tenant overview</span><SettingsIcon /></div>
    <div className="platform-login-tenant-row"><span className="platform-login-tenant-avatar">N</span><span><strong>Northwind Demo</strong><small>northwind.anevra.com</small></span><em className="platform-login-status-active">Active</em></div>
    <div className="platform-login-tenant-row"><span className="platform-login-tenant-avatar platform-login-tenant-avatar-alt">A</span><span><strong>Apex Industries</strong><small>apex.anevra.com</small></span><em className="platform-login-status-paused">Inactive</em></div>
  </div>
}

function BuildingsIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 20V5.5L12 3v17M12 20V8l8-2.5V20M7.5 8h1M7.5 11h1M7.5 14h1M15 10h1M15 13h1M15 16h1M2.5 20h19" /></svg> }
function ShieldIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 20 6v5.5c0 4.8-3.3 7.8-8 9.5-4.7-1.7-8-4.7-8-9.5V6l8-3Z" /><path d="m8.5 12 2.2 2.2 4.8-5" /></svg> }
function SettingsIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z" /><path d="m19.4 15 .1.1-1.6 2.7-.2-.1a2 2 0 0 0-2.8 1.2v.2h-3.2v-.2a2 2 0 0 0-2.8-1.2l-.2.1-1.6-2.7.1-.1a2 2 0 0 0 0-3l-.1-.1 1.6-2.7.2.1a2 2 0 0 0 2.8-1.2V8h3.2v.2a2 2 0 0 0 2.8 1.2l.2-.1 1.6 2.7-.1.1a2 2 0 0 0 0 3Z" /></svg> }
function EmailIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6.5h16v11H4zM4 7l8 6 8-6" /></svg> }
function LockIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></svg> }
function EyeIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2.5 12s3.5-5 9.5-5 9.5 5 9.5 5-3.5 5-9.5 5-9.5-5-9.5-5Z" /><circle cx="12" cy="12" r="2.5" /></svg> }
function EyeOffIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m3 3 18 18M10.6 7.3A10.8 10.8 0 0 1 12 7c6 0 9.5 5 9.5 5a17 17 0 0 1-3.2 3.3M6.5 6.8C3.9 8.4 2.5 12 2.5 12s3.5 5 9.5 5c1.1 0 2.1-.2 3-.5" /><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" /></svg> }
function ArrowIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h13M13 6l6 6-6 6" /></svg> }
function AlertIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 7.5v5M12 16.5h.01" /></svg> }
