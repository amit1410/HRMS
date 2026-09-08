import { useMemo, useState, type CSSProperties, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { identifyPasswordRecovery, resendPasswordRecoveryOtp, resetPassword, sendPasswordRecoveryOtp, verifyPasswordRecoveryOtp } from '../api/passwordRecovery.ts'
import type { PasswordRecoveryChannel, RecoveryChallenge } from '../api/types.ts'
import { toApiError } from '../api/errors.ts'
import { useTenantBranding } from '../hooks/useTenantBranding.ts'

const policy = 'Password must be 8-128 characters and include upper, lower, and numeric characters.'
const genericRecoveryMessage = 'If the account is eligible for password recovery, you can continue with the available verification method.'

export function ForgotPasswordPage() {
  const navigate = useNavigate()
  const { branding, isLoading, error: brandingError } = useTenantBranding()
  const [identifier, setIdentifier] = useState('')
  const [challenge, setChallenge] = useState<RecoveryChallenge | null>(null)
  const [channel, setChannel] = useState<PasswordRecoveryChannel | null>(null)
  const [otp, setOtp] = useState('')
  const [resetToken, setResetToken] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const workspaceStyle = useMemo<CSSProperties | undefined>(() => branding?.primaryColor ? { '--recovery-accent': branding.primaryColor } as CSSProperties : undefined, [branding?.primaryColor])
  const loginMode = branding?.loginIdentifierMode ?? 'EmailOrEmployeeCode'
  const identifierLabel = loginMode === 'EmailOnly' ? 'Email' : loginMode === 'EmployeeCodeOnly' ? 'Employee Code' : 'Email or Employee Code'
  const identifierPlaceholder = loginMode === 'EmailOnly' ? 'name@company.com' : loginMode === 'EmployeeCodeOnly' ? 'ANV_1001' : 'name@company.com or ANV_1001'
  const displayName = branding?.displayName || 'HRMS'

  async function identify(event: FormEvent) {
    event.preventDefault()
    setError('')
    setNotice('')
    setBusy(true)
    try {
      const result = await identifyPasswordRecovery(identifier.trim())
      setChallenge(result)
      if (result.availableChannels.length === 1) setChannel(result.availableChannels[0]?.channel ?? null)
      if (result.availableChannels.length === 0) setNotice(result.message)
    } catch (caught) {
      setError(toApiError(caught).message)
    } finally {
      setBusy(false)
    }
  }

  async function send(event: FormEvent) {
    event.preventDefault()
    if (!challenge || !channel) return
    setError('')
    setBusy(true)
    try {
      const result = await sendPasswordRecoveryOtp(challenge.challengeId, channel)
      setNotice(result.developmentOtp ? `Development verification code: ${result.developmentOtp}` : result.message)
      setChallenge({ ...challenge, availableChannels: challenge.availableChannels.filter((option) => option.channel === channel) })
    } catch (caught) {
      setError(toApiError(caught).message)
    } finally {
      setBusy(false)
    }
  }

  async function verify(event: FormEvent) {
    event.preventDefault()
    if (!challenge) return
    setError('')
    setBusy(true)
    try {
      const result = await verifyPasswordRecoveryOtp(challenge.challengeId, otp)
      setResetToken(result.resetToken)
      setNotice('Verification successful. Set a new password to finish.')
    } catch (caught) {
      setError(toApiError(caught).message)
    } finally {
      setBusy(false)
    }
  }

  async function resend() {
    if (!challenge) return
    setError('')
    setBusy(true)
    try {
      const result = await resendPasswordRecoveryOtp(challenge.challengeId)
      setNotice(result.developmentOtp ? `Development verification code: ${result.developmentOtp}` : result.message)
    } catch (caught) {
      setError(toApiError(caught).message)
    } finally {
      setBusy(false)
    }
  }

  async function reset(event: FormEvent) {
    event.preventDefault()
    setError('')
    if (password.length < 8 || password.length > 128 || !/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/\d/.test(password)) {
      setError(policy)
      return
    }
    if (password !== confirm) {
      setError('Passwords do not match.')
      return
    }
    setBusy(true)
    try {
      await resetPassword(resetToken, password, confirm)
      setDone(true)
    } catch (caught) {
      setError(toApiError(caught).message)
    } finally {
      setBusy(false)
    }
  }

  if (isLoading) return <RecoveryMessage title="Loading workspace…" />
  if (brandingError && !branding) return <RecoveryMessage title="Workspace unavailable" error={brandingError.message} />
  if (branding && branding.passwordRecoveryEnabled === false) return <RecoveryMessage title="Password recovery unavailable" description="Password recovery has been disabled for this workspace." />
  if (done) return <RecoveryShell displayName={displayName} logoUrl={branding?.logoUrl} style={workspaceStyle} step="Success"><div className="recovery-success" role="status"><div className="recovery-success-icon" aria-hidden="true"><CheckIcon /></div><h1>Password reset successful</h1><p>You can now sign in with your new password.</p><button type="button" className="recovery-primary-button" onClick={() => navigate('/login')}>Back to Sign In <ArrowIcon /></button></div></RecoveryShell>

  const currentStep = resetToken ? 'Set password' : channel ? 'Verify code' : challenge ? 'Delivery method' : 'Identify account'
  return <RecoveryShell displayName={displayName} logoUrl={branding?.logoUrl} style={workspaceStyle} step={currentStep}>
    {!challenge && <section className="recovery-form-section" aria-labelledby="recovery-title"><StepBadge icon={<KeyIcon />} label="Account recovery" /><div className="recovery-heading"><h1 id="recovery-title">Forgot Password?</h1><p>No worries. Enter your {identifierLabel.toLowerCase()} and we’ll send a verification code to reset your password.</p></div><form className="recovery-form" onSubmit={(event) => void identify(event)} noValidate><RecoveryField id="identifier" label={identifierLabel} type={loginMode === 'EmailOnly' ? 'email' : 'text'} value={identifier} onChange={setIdentifier} placeholder={identifierPlaceholder} autoComplete="username" icon={<UserIcon />} /><button type="submit" className="recovery-primary-button" disabled={busy}>{busy ? 'Sending…' : 'Send Verification Code'} <ArrowIcon /></button></form><div className="recovery-divider" aria-hidden="true"><span>OR</span></div></section>}
    {challenge && !resetToken && !channel && challenge.availableChannels.length === 0 && <section className="recovery-form-section" aria-labelledby="recovery-title"><StepBadge icon={<ShieldIcon />} label="Account recovery" /><div className="recovery-heading"><h1 id="recovery-title">Check your options</h1><p role="status">{notice || challenge.message || genericRecoveryMessage}</p></div><Link className="recovery-back-link" to="/login"><ArrowBackIcon /> Back to Sign In</Link></section>}
    {challenge && !resetToken && !channel && challenge.availableChannels.length > 0 && <section className="recovery-form-section" aria-labelledby="recovery-title"><StepBadge icon={<ShieldIcon />} label="Verification method" /><div className="recovery-heading"><h1 id="recovery-title">Choose verification method</h1><p>Select where you would like to receive your one-time verification code.</p></div><div className="recovery-channel-list" role="radiogroup" aria-label="Verification method">{challenge.availableChannels.map((option) => <button key={option.channel} type="button" role="radio" aria-checked={channel === option.channel} className={`recovery-channel-card${channel === option.channel ? ' is-selected' : ''}`} onClick={() => setChannel(option.channel)}><span className="recovery-channel-icon" aria-hidden="true">{option.channel === 'Sms' ? <PhoneIcon /> : <MailIcon />}</span><span><strong>{option.channel === 'Sms' ? 'Mobile' : 'Email'}</strong><small>Send code by {option.channel === 'Sms' ? 'text message' : 'email'}</small></span><span className="recovery-radio" aria-hidden="true" /></button>)}</div><Link className="recovery-back-link" to="/login"><ArrowBackIcon /> Back to Sign In</Link></section>}
    {challenge && channel && !resetToken && <section className="recovery-form-section" aria-labelledby="recovery-title"><StepBadge icon={<ShieldIcon />} label={notice ? 'Verify your code' : 'Send verification code'} /><div className="recovery-heading"><h1 id="recovery-title">{notice ? 'Enter verification code' : 'Send verification code'}</h1><p>{notice ? 'Enter the code you received to continue securely.' : `We’ll send a one-time code by ${channel === 'Sms' ? 'mobile' : 'email'}.`}</p></div>{!notice && <form className="recovery-form" onSubmit={(event) => void send(event)}><button type="submit" className="recovery-primary-button" disabled={busy}>Send OTP <ArrowIcon /></button></form>}{notice && <form className="recovery-form" onSubmit={(event) => void verify(event)}><RecoveryField id="otp" label="Verification Code" type="text" value={otp} onChange={setOtp} placeholder="Enter 6-digit code" autoComplete="one-time-code" inputMode="numeric" icon={<ShieldIcon />} /><button type="submit" className="recovery-primary-button" disabled={busy}>Verify OTP <ArrowIcon /></button><button type="button" className="recovery-secondary-button" disabled={busy} onClick={() => void resend()}>Resend OTP</button></form>}<button type="button" className="recovery-back-link recovery-back-button" onClick={() => { setChannel(null); setNotice(''); setError('') }}><ArrowBackIcon /> Back to verification methods</button></section>}
    {resetToken && <section className="recovery-form-section" aria-labelledby="recovery-title"><StepBadge icon={<LockIcon />} label="Set new password" /><div className="recovery-heading"><h1 id="recovery-title">Create a new password</h1><p>Choose a strong password you’ll use the next time you sign in.</p></div><form className="recovery-form" onSubmit={(event) => void reset(event)} noValidate><RecoveryField id="new-password" label="New Password" type="password" value={password} onChange={setPassword} placeholder="Enter new password" autoComplete="new-password" icon={<LockIcon />} /><RecoveryField id="confirm-password" label="Confirm New Password" type="password" value={confirm} onChange={setConfirm} placeholder="Re-enter new password" autoComplete="new-password" icon={<LockIcon />} /><p className="recovery-policy">{policy}</p><button type="submit" className="recovery-primary-button" disabled={busy}>Reset Password <ArrowIcon /></button></form></section>}
    {notice && !resetToken && challenge && channel && <p className="recovery-notice" role="status">{notice}</p>}
    {error && <p className="recovery-error" role="alert">{error}</p>}
  </RecoveryShell>
}

function RecoveryShell({ displayName, logoUrl, style, step, children }: { displayName: string; logoUrl?: string | null; style?: CSSProperties; step: string; children: ReactNode }) { return <main className="recovery-page" style={style}><div className="recovery-layout"><section className="recovery-brand-panel" aria-label={`${displayName} workspace`}><div className="recovery-brand-top"><BrandMark displayName={displayName} logoUrl={logoUrl} /><span className="recovery-secure-label"><ShieldIcon /> Secure recovery</span></div><div className="recovery-brand-copy"><p className="recovery-eyebrow">{displayName} workspace</p><h2>Secure access<br />for a brighter<br />workday.</h2><p>Get back to the people and work that keep your organization moving.</p></div><div className="recovery-art" aria-hidden="true"><span className="recovery-art-orb recovery-art-orb-one" /><span className="recovery-art-orb recovery-art-orb-two" /><span className="recovery-art-wave recovery-art-wave-one" /><span className="recovery-art-wave recovery-art-wave-two" /><span className="recovery-art-spark">✦</span><span className="recovery-art-check">✓</span></div><p className="recovery-brand-footer">Your workspace, protected.</p></section><section className="recovery-form-panel"><div className="recovery-form-wrap"><div className="recovery-stepper" aria-label={`Recovery step: ${step}`}><span className="recovery-step-dot" /><span>{step}</span></div>{children}<p className="recovery-form-footer">Need help? Contact your HR administrator.</p>{!step.includes('Success') && <Link className="recovery-back-link recovery-footer-link" to="/login"><ArrowBackIcon /> Back to Sign In</Link>}</div></section></div></main> }
function RecoveryMessage({ title, description, error }: { title: string; description?: string; error?: string }) { return <main className="recovery-message-page"><div className="recovery-message-card"><div className="recovery-message-mark"><ShieldIcon /></div><h1>{title}</h1><p>{description || error}</p><Link className="recovery-primary-button recovery-button-link" to="/login">Back to Sign In <ArrowBackIcon /></Link></div></main> }
function BrandMark({ displayName, logoUrl }: { displayName: string; logoUrl?: string | null }) { return <div className="recovery-brand-mark">{logoUrl ? <img src={logoUrl} alt={displayName} /> : <span aria-hidden="true">HR</span>}<strong>{displayName}</strong></div> }
function StepBadge({ icon, label }: { icon: ReactNode; label: string }) { return <div className="recovery-step-badge">{icon}<span>{label}</span></div> }
function RecoveryField({ id, label, type, value, onChange, placeholder, autoComplete, icon, inputMode }: { id: string; label: string; type: 'email' | 'password' | 'text'; value: string; onChange: (value: string) => void; placeholder: string; autoComplete: string; icon: ReactNode; inputMode?: 'numeric' }) { return <div className="recovery-field"><label htmlFor={id}>{label}</label><div className="recovery-input-wrap">{icon}<input id={id} name={id} type={type} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} autoComplete={autoComplete} inputMode={inputMode} required /></div></div> }
function Icon({ children }: { children: ReactNode }) { return <svg viewBox="0 0 24 24" aria-hidden="true">{children}</svg> }
function UserIcon() { return <Icon><circle cx="12" cy="8" r="3.5" /><path d="M4.5 20c.7-3.2 3.1-5 7.5-5s6.8 1.8 7.5 5" /></Icon> }
function MailIcon() { return <Icon><path d="M4 6.5h16v11H4zM4 7l8 6 8-6" /></Icon> }
function PhoneIcon() { return <Icon><path d="M7 4.5 9.5 4l2 4-2 1.5c1 2.1 2.9 4 5 5l1.5-2 4 2-.5 2.5c-.3 1.3-1.5 2.1-2.8 2-6.5-.7-11.5-5.7-12.2-12.2C4.4 6 5.3 4.8 7 4.5Z" /></Icon> }
function ShieldIcon() { return <Icon><path d="M12 3.5 19 6v5.2c0 4.2-2.8 7.8-7 9.3-4.2-1.5-7-5.1-7-9.3V6l7-2.5Z" /><path d="m9 12 2 2 4-4" /></Icon> }
function KeyIcon() { return <Icon><circle cx="8.5" cy="15.5" r="3.5" /><path d="m11 13 8-8m-2 2 2 2m-5-1 2 2" /></Icon> }
function LockIcon() { return <Icon><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v2" /></Icon> }
function CheckIcon() { return <Icon><path d="m5 12 4.5 4.5L19 7" /></Icon> }
function ArrowIcon() { return <Icon><path d="M5 12h13M13 6l6 6-6 6" /></Icon> }
function ArrowBackIcon() { return <Icon><path d="M19 12H6m6-6-6 6 6 6" /></Icon> }
