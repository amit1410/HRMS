import { useState } from 'react'
import { createPortalAccount, getPortalAccount, resendPortalInvite, revokePortalInvite, type PortalAccount } from '../../api/employees.ts'
import { toApiError } from '../../api/errors.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { Card } from '../../components/Card.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

export function PortalAccessCard({ employeeId }: { employeeId: string }) {
  const { can } = useAuth()
  const query = useApiQuery((signal) => getPortalAccount(employeeId, signal), [employeeId])
  const [inviteUrl, setInviteUrl] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  if (!can(Permissions.user.view) && !can(Permissions.user.create)) return null

  async function create() {
    setError(''); setBusy(true)
    try { const result = await createPortalAccount(employeeId); query.refetch(); setInviteUrl(result.developmentInviteUrl ?? null) } catch (reason) { setError(toApiError(reason).message) } finally { setBusy(false) }
  }
  async function resend() {
    setError(''); setBusy(true)
    try { const result = await resendPortalInvite(employeeId); query.refetch(); setInviteUrl(result.developmentInviteUrl ?? null) } catch (reason) { setError(toApiError(reason).message) } finally { setBusy(false) }
  }
  async function revoke() {
    if (!window.confirm('Revoke this pending welcome invitation?')) return
    setError(''); setBusy(true)
    try { const result = await revokePortalInvite(employeeId); query.refetch(); setInviteUrl(null); void result } catch (reason) { setError(toApiError(reason).message) } finally { setBusy(false) }
  }

  const account: PortalAccount | null = query.data
  return <Card title="Portal Access"><p>Portal Account: <strong>{query.isLoading ? 'Loading…' : account?.state === 'InvitationPending' ? 'Invitation Pending' : account?.state ?? 'NotCreated'}</strong></p>{account?.email && <p>Email: {account.email}</p>}{account?.invitationExpiresAtUtc && <p>Expires: {new Date(account.invitationExpiresAtUtc).toLocaleString()}</p>}{error && <p role="alert">{error}</p>}{can(Permissions.user.create) && account?.state === 'NotCreated' && <button type="button" onClick={() => void create()} disabled={busy}>Create User Account &amp; Send Welcome Invite</button>}{can(Permissions.user.create) && account?.state === 'InvitationPending' && <><button type="button" onClick={() => void resend()} disabled={busy}>Resend invite</button>{' '}<button type="button" onClick={() => void revoke()} disabled={busy}>Revoke invite</button></>}{inviteUrl && <div role="status"><p><strong>Development invite URL — copy it now. It will not be shown again.</strong></p><code>{inviteUrl}</code><br /><button type="button" onClick={() => void navigator.clipboard?.writeText(inviteUrl)}>Copy invite URL</button>{' '}<button type="button" onClick={() => setInviteUrl(null)}>Close</button></div>}</Card>
}
