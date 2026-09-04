import { useEffect, useState, type ReactNode } from 'react'
import { getEmployeeCandidates, getLinkHistory, getLinkState, getUserCandidates, linkAccount, replaceAccount, unlinkAccount, type Candidate, type LinkEvent, type LinkState } from '../../api/accountEmployeeLinks.ts'
import { toApiError, type ApiError } from '../../api/errors.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { PageHeader } from '../../components/PageHeader.tsx'

type LoadState<T> = { status: 'idle' | 'loading' | 'ready' | 'forbidden' | 'error'; data: T | null; error: ApiError | null }

const idle = <T,>(): LoadState<T> => ({ status: 'idle', data: null, error: null })
const loading = <T,>(previous: LoadState<T>): LoadState<T> => ({ status: 'loading', data: previous.data, error: null })
const failed = <T,>(error: unknown): LoadState<T> => {
  const apiError = toApiError(error)
  return { status: apiError.isForbidden ? 'forbidden' : 'error', data: null, error: apiError }
}

function ResourceMessage<T>({ resource, empty, children }: { resource: LoadState<T>; empty?: string; children?: ReactNode }) {
  if (resource.status === 'loading' && !resource.data) return <p role="status">Loading…</p>
  if (resource.status === 'forbidden') return <p role="alert">You do not have permission to view this data.</p>
  if (resource.status === 'error') return <p role="alert">{resource.error?.message ?? 'The request failed.'}</p>
  if (resource.status === 'ready' && empty && Array.isArray(resource.data) && resource.data.length === 0) return <p>{empty}</p>
  if (resource.status === 'ready' && empty && resource.data === null) return <p>{empty}</p>
  return <>{children}</>
}

export function AccountEmployeeLinksPage() {
  const { user, can } = useAuth()
  const canManage = can(Permissions.accountEmployeeLink.manage)
  const canViewHistory = can(Permissions.accountEmployeeLink.viewHistory)
  const [users, setUsers] = useState<LoadState<Candidate[]>>(idle)
  const [employees, setEmployees] = useState<LoadState<Candidate[]>>(idle)
  const [selected, setSelected] = useState('')
  const [state, setState] = useState<LoadState<LinkState>>(idle)
  const [history, setHistory] = useState<LoadState<LinkEvent[]>>(idle)
  const [employeeId, setEmployeeId] = useState('')
  const [reason, setReason] = useState('')
  const [message, setMessage] = useState('')

  async function loadCandidates() {
    if (!canManage) {
      setUsers({ status: 'forbidden', data: null, error: null })
      setEmployees({ status: 'forbidden', data: null, error: null })
      return
    }
    setUsers((previous) => loading(previous)); setEmployees((previous) => loading(previous))
    const [userResult, employeeResult] = await Promise.allSettled([getUserCandidates(), getEmployeeCandidates()])
    if (userResult.status === 'fulfilled') setUsers({ status: 'ready', data: userResult.value.items, error: null })
    else setUsers(failed(userResult.reason))
    if (employeeResult.status === 'fulfilled') setEmployees({ status: 'ready', data: employeeResult.value.items, error: null })
    else setEmployees(failed(employeeResult.reason))
  }

  async function refreshSelected(userId: string) {
    setState((previous) => loading(previous))
    const currentRequest = getLinkState(userId)
      .then((data) => setState({ status: 'ready', data, error: null }))
      .catch((error) => setState(failed<LinkState>(error)))
    if (!canViewHistory) {
      setHistory({ status: 'forbidden', data: null, error: null })
    } else {
      setHistory((previous) => loading(previous))
      void getLinkHistory(userId)
        .then((data) => setHistory({ status: 'ready', data: data.items, error: null }))
        .catch((error) => setHistory(failed<LinkEvent[]>(error)))
    }
    await currentRequest
  }

  useEffect(() => { void loadCandidates() }, [canManage])
  useEffect(() => {
    if (selected) void refreshSelected(selected)
    else { setState(idle()); setHistory(canViewHistory ? idle() : { status: 'forbidden', data: null, error: null }) }
  }, [selected, canViewHistory])

  const submit = async () => {
    if (!selected || !employeeId || reason.trim().length === 0 || !canManage) return
    try {
      const next = state.data?.status === 'Linked' && state.data.currentLink
        ? await replaceAccount(selected, { expectedLinkId: state.data.currentLink.linkId, expectedEmployeeId: state.data.currentLink.employeeId, expectedRevision: state.data.revision!, newEmployeeId: employeeId, reason })
        : await linkAccount(selected, { employeeId, expectedRevision: state.data?.revision ?? null, reason })
      setState({ status: 'ready', data: next, error: null })
      setMessage('Saved')
      setReason('')
      setEmployeeId('')
      await refreshSelected(selected)
      await loadCandidates()
    } catch (error) { setMessage(toApiError(error).message) }
  }

  const unlink = async () => {
    if (!selected || !state.data?.currentLink || !state.data.revision || !reason.trim() || !canManage) return
    try {
      await unlinkAccount(selected, { expectedLinkId: state.data.currentLink.linkId, expectedEmployeeId: state.data.currentLink.employeeId, expectedRevision: state.data.revision, reason })
      setMessage('Unlinked'); setReason(''); setEmployeeId('')
      await refreshSelected(selected)
      await loadCandidates()
    } catch (error) { setMessage(toApiError(error).message) }
  }

  return <section>
    <PageHeader title="Account–Employee Links" subtitle="Manage verified identity links. Operators cannot change their own link." />
    <div className="card">
      <label>Account<select value={selected} onChange={(event) => setSelected(event.target.value)}><option value="">Select an account</option>{users.data?.filter((item) => item.id !== user?.id).map((item) => <option key={item.id} value={item.id}>{item.displayName} ({item.email})</option>)}</select></label>
      {selected && <section aria-label="Current link"><h2>Current link</h2><ResourceMessage resource={state}>{state.data?.status === 'Unlinked' && <p>No current employee link.</p>}{state.data?.currentLink && <p>{state.data.currentLink.displayName}{state.data.currentLink.employeeCode ? ` (${state.data.currentLink.employeeCode})` : ''}</p>}{state.data?.status === 'Invalid' && <p role="alert">The saved link points to an unavailable employee.</p>}</ResourceMessage></section>}
      {canManage && <section aria-label="Eligible employees"><h2>Eligible employees</h2><ResourceMessage resource={employees} empty="No eligible employee candidates."><select aria-label="Employee" value={employeeId} onChange={(event) => setEmployeeId(event.target.value)}><option value="">Select an eligible employee</option>{employees.data?.map((item) => <option key={item.id} value={item.id}>{item.displayName} ({item.employeeCode ?? 'no code'})</option>)}</select><label>Reason<textarea value={reason} maxLength={500} onChange={(event) => setReason(event.target.value)} /></label><button type="button" onClick={() => void submit()} disabled={!selected || !employeeId || !reason.trim()}>Link / replace</button>{state.data?.status === 'Linked' && <button type="button" onClick={() => void unlink()} disabled={!reason.trim()}>Unlink</button>}</ResourceMessage></section>}
      {selected && <section aria-label="Link history"><h2>Link history</h2>{!canViewHistory && <p role="alert">History is not available because your account lacks AccountEmployeeLink.ViewHistory.</p>}<ResourceMessage resource={history} empty="No link history recorded.">{history.data?.map((event) => <p key={event.id}>{event.operation}: {event.reason}</p>)}</ResourceMessage></section>}
      {message && <p role="status">{message}</p>}
    </div>
  </section>
}
