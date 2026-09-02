import { useMemo, useState, type CSSProperties, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { hasFieldErrors, toApiError, type ApiError } from '../api/errors.ts'
import { useAuth } from '../auth/useAuth.ts'
import { TextField } from '../components/fields.tsx'
import { FullPageSpinner, Spinner } from '../components/Spinner.tsx'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { useTenantBranding } from '../hooks/useTenantBranding.ts'

/**
 * Sign-in.
 *
 * Two fields, because which directory of users to check is decided by the address the browser is
 * at — the workspace host — so there is deliberately nothing here that names an organization. An
 * email address that exists in two organizations is unambiguous the moment the host is known.
 *
 * The API answers a bad email and a bad password identically ("Invalid credentials"), so this page
 * has nothing to add: it shows what the server said and does not guess which half was wrong.
 *
 * Tenant branding (display name, logo, primary colour, welcome message) is loaded from the
 * pre-authentication `/api/tenants/current/branding` endpoint, which resolves by the host the
 * request arrives at — never by client input. The branding fetches anonymously before the user
 * signs in, so the sign-in screen can personalize itself for the correct workspace.
 */
export function LoginPage() {
  useDocumentTitle('Sign in')

  const { status, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { branding, isLoading: brandingLoading, error: brandingError } = useTenantBranding()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<ApiError | null>(null)
  const [submitting, setSubmitting] = useState(false)

  // Workspace-level styling derived from branding — applied as inline style on the page root
  // so the custom property is available to all descendant selectors.
  const workspaceStyle = useMemo<CSSProperties | undefined>(() => {
    if (!branding?.primaryColor) return undefined
    return { '--ws-accent': branding.primaryColor } as CSSProperties
  }, [branding?.primaryColor])

  // A stored refresh token is still being exchanged; showing the form now would flash it at someone who
  // turns out to be signed in.
  if (status === 'restoring') {
    return <FullPageSpinner label="Restoring your session…" />
  }

  if (status === 'authenticated') {
    return <Navigate to={redirectTarget(location.state)} replace />
  }

  // Branding is still loading — show a spinner instead of a flickering unbranded card.
  if (brandingLoading) {
    return (
      <div className="login-page" style={workspaceStyle}>
        <FullPageSpinner label="Loading workspace…" />
      </div>
    )
  }

  // Branding request failed — the workspace may be unreachable or misconfigured.
  if (brandingError && !branding) {
    return (
      <div className="login-page" style={workspaceStyle}>
        <div className="login-card">
          <div className="login-brand">
            <span className="sidebar-mark login-mark" aria-hidden="true">
              HR
            </span>
            <div>
              <h1 className="login-title">Workspace unavailable</h1>
              <p className="login-subtitle">We couldn't load this workspace's sign-in page.</p>
            </div>
          </div>
          <div className="form-error" role="alert">
            {brandingError.message || 'Unable to connect to the server. Please try again later.'}
          </div>
        </div>
      </div>
    )
  }

  // Branding loaded but the address belongs to no organization (or the org has no branding).
  // The API returns a neutral (all-null) payload for unknown, inactive, and opted-out orgs alike,
  // so a caller cannot probe which addresses belong to somebody.
  const isUnavailable = branding !== null && !branding.displayName && !branding.logoUrl

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return

    setSubmitting(true)
    setError(null)
    try {
      await login({ email: email.trim(), password })
      // Straight to wherever the guard interrupted, replacing this entry so Back does not return here.
      navigate(redirectTarget(location.state), { replace: true })
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSubmitting(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  if (isUnavailable) {
    return (
      <div className="login-page" style={workspaceStyle}>
        <div className="login-card">
          <div className="login-brand">
            <span className="sidebar-mark login-mark" aria-hidden="true">
              HR
            </span>
            <div>
              <h1 className="login-title">Workspace not found</h1>
              <p className="login-subtitle">There is no organization at this address.</p>
            </div>
          </div>
          <p className="form-error" role="alert">
            Check the address you used, or ask your administrator for the correct workspace URL.
          </p>
        </div>
      </div>
    )
  }

  const displayName = branding?.displayName || 'Sign in'
  const welcomeMessage = branding?.welcomeMessage
  const logoUrl = branding?.logoUrl

  return (
    <div className="login-page" data-testid="login-page" style={workspaceStyle}>
      <div className="login-card">
        <div className="login-brand">
          {logoUrl ? (
            <img
              className="login-logo"
              src={logoUrl}
              alt={displayName}
              width={48}
              height={48}
            />
          ) : (
            <span className="sidebar-mark login-mark" aria-hidden="true">
              HR
            </span>
          )}
          <div>
            <h1 className="login-title">{displayName}</h1>
            {welcomeMessage && <p className="login-subtitle">{welcomeMessage}</p>}
          </div>
        </div>

        {error && !hasFieldErrors(error) && (
          <p className="form-error" role="alert">
            {error.message}
          </p>
        )}

        <form onSubmit={onSubmit} noValidate>
          <TextField
            id="email"
            label="Email"
            type="email"
            value={email}
            onChange={setEmail}
            autoComplete="username"
            error={fieldError('email')}
          />
          <TextField
            id="password"
            label="Password"
            type="password"
            value={password}
            onChange={setPassword}
            autoComplete="current-password"
            error={fieldError('password')}
          />

          <button type="submit" className="button button-primary button-block" disabled={submitting}>
            {submitting ? <Spinner size={16} label="Signing in…" /> : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}

/**
 * Where to land after signing in.
 *
 * Only in-app absolute paths are honoured. A protocol-relative value (`//evil.example`) is a valid
 * router path but navigates off-site, so it is refused — the route state comes from `RequireAuth`, but
 * a link can put anything in history state and this is the one place it is trusted.
 */
function redirectTarget(state: unknown): string {
  const from = (state as { from?: { pathname?: unknown } } | null)?.from?.pathname
  if (typeof from !== 'string') return '/dashboard'
  if (!from.startsWith('/') || from.startsWith('//')) return '/dashboard'
  if (from === '/login') return '/dashboard'
  return from
}
