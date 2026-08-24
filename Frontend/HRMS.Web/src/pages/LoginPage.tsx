import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { hasFieldErrors, toApiError, type ApiError } from '../api/errors.ts'
import { useAuth } from '../auth/useAuth.ts'
import { TextField } from '../components/fields.tsx'
import { FullPageSpinner, Spinner } from '../components/Spinner.tsx'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { getLastTenantCode, setLastTenantCode } from '../lib/preferences.ts'

/**
 * Sign-in.
 *
 * Three fields, because the tenant code is what tells the API *whose* directory of users to check —
 * two tenants may each have an `anna@example.com`, and they are different people. The code is part of
 * the credential check server-side; it is remembered locally only to save typing.
 *
 * The API answers a bad tenant, a bad email and a bad password identically ("Invalid credentials"), so
 * this page has nothing to add: it shows what the server said and does not guess which field was wrong.
 */
export function LoginPage() {
  useDocumentTitle('Sign in')

  const { status, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [tenantCode, setTenantCode] = useState(getLastTenantCode)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<ApiError | null>(null)
  const [submitting, setSubmitting] = useState(false)

  // A stored refresh token is still being exchanged; showing the form now would flash it at someone who
  // turns out to be signed in.
  if (status === 'restoring') {
    return <FullPageSpinner label="Restoring your session…" />
  }

  if (status === 'authenticated') {
    return <Navigate to={redirectTarget(location.state)} replace />
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return

    setSubmitting(true)
    setError(null)
    try {
      await login({ tenantCode: tenantCode.trim(), email: email.trim(), password })
      setLastTenantCode(tenantCode)
      // Straight to wherever the guard interrupted, replacing this entry so Back does not return here.
      navigate(redirectTarget(location.state), { replace: true })
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSubmitting(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <span className="sidebar-mark" aria-hidden="true">
            HR
          </span>
          <div>
            <h1 className="login-title">Sign in</h1>
            <p className="login-subtitle">Employee management</p>
          </div>
        </div>

        {error && !hasFieldErrors(error) && (
          <p className="form-error" role="alert">
            {error.message}
          </p>
        )}

        <form onSubmit={onSubmit} noValidate>
          <TextField
            id="tenantCode"
            label="Tenant code"
            value={tenantCode}
            onChange={setTenantCode}
            autoComplete="organization"
            autoCapitalize="characters"
            error={fieldError('tenantCode')}
            hint="The short code for your organization, e.g. DEMO01."
          />
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
