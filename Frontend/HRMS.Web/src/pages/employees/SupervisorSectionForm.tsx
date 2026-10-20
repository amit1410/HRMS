import { useEffect, useState, type FormEvent } from 'react'
import { getEmploymentHistory, getSupervisor, upsertSupervisor } from '../../api/employeeSubsections.ts'
import type { EmployeeEmploymentHistory, EmployeeSupervisor, EmployeeSupervisorRequest, SupervisorOption, SupervisorType } from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { SupervisorField } from '../../components/fields.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { toApiError, type ApiError } from '../../api/errors.ts'

interface SupervisorSectionFormProps {
  employeeId: string
  onEmploymentChange?: () => void
  onSaved?: () => void | Promise<void>
}

type Role = {
  label: string
  type: SupervisorType
  idKey: keyof EmployeeSupervisorRequest
  codeKey: keyof EmployeeSupervisorRequest
  nameKey: keyof EmployeeSupervisorRequest
}

const ROLES: Role[] = [
  { label: 'L1 manager', type: 'L1', idKey: 'l1ManagerId', codeKey: 'l1ManagerCode', nameKey: 'l1ManagerName' },
  { label: 'L2 manager', type: 'L2', idKey: 'l2ManagerId', codeKey: 'l2ManagerCode', nameKey: 'l2ManagerName' },
  { label: 'L3 manager', type: 'L3', idKey: 'l3ManagerId', codeKey: 'l3ManagerCode', nameKey: 'l3ManagerName' },
  { label: 'L4 manager', type: 'Other', idKey: 'l4ManagerId', codeKey: 'l4ManagerCode', nameKey: 'l4ManagerName' },
  { label: 'L5 manager', type: 'Other', idKey: 'l5ManagerId', codeKey: 'l5ManagerCode', nameKey: 'l5ManagerName' },
  { label: 'Time manager', type: 'Time', idKey: 'timeManagerId', codeKey: 'timeManagerCode', nameKey: 'timeManagerName' },
  { label: 'ERO', type: 'Other', idKey: 'eroId', codeKey: 'eroCode', nameKey: 'eroName' },
  { label: 'CHRO manager', type: 'HR', idKey: 'chroManagerId', codeKey: 'chroManagerCode', nameKey: 'chroManagerName' },
]

export function SupervisorSectionForm({ employeeId, onSaved }: SupervisorSectionFormProps) {
  const { can } = useAuth()
  const [values, setValues] = useState<EmployeeSupervisorRequest>({})
  const [initialValues, setInitialValues] = useState<EmployeeSupervisorRequest>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const supervisor = useApiQuery((signal) => getSupervisor(employeeId, signal), [employeeId])
  const history = useApiQuery((signal) => getEmploymentHistory(employeeId, signal), [employeeId])

  useEffect(() => {
    const current = history.data?.find(isEffectiveToday)
    if (supervisor.data) {
      const next = supervisorRequest(supervisor.data)
      if (!next.l1ManagerId && current?.managerId) {
        next.l1ManagerId = current.managerId
        next.l1ManagerCode = current.managerEmployeeCode
        next.l1ManagerName = current.managerFullName
      }
      setValues(next)
      setInitialValues(next)
      return
    }

    if (current) {
      const next = { ...values, l1ManagerId: current.managerId ?? null, l1ManagerCode: current.managerEmployeeCode ?? null, l1ManagerName: current.managerFullName ?? null }
      setValues(next)
      setInitialValues(next)
    }
  }, [supervisor.data, history.data])

  function updateRole(role: Role, managerId: string, option: SupervisorOption | null): void {
    setError(null)
    setValues((previous) => ({
      ...previous,
      [role.idKey]: managerId || null,
      [role.codeKey]: option?.employeeCode ?? null,
      [role.nameKey]: option?.fullName ?? null,
    }))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return
    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      await upsertSupervisor(employeeId, values)
      setSuccess('Additional supervisor assignments saved.')
      await supervisor.refetch()
      await onSaved?.()
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  if (supervisor.isLoading && !supervisor.data && history.isLoading) return <Card><div className="table-loading"><Spinner label="Loading supervisor assignments..." /></div></Card>
  if (supervisor.error && supervisor.error.status !== 404 && !supervisor.data) return <Card><ErrorState error={supervisor.error} onRetry={supervisor.refetch} /></Card>

  const canEdit = can(Permissions.employee.edit)

  return (
    <Card className="form-card supervisor-form-card">
      {error && <Notice tone="error">{error.message}</Notice>}
      {success && <Notice tone="success">{success}</Notice>}
      <div className="employment-card-heading supervisor-card-heading">
        <div><p className="eyebrow">Employee details</p><h2>Supervisor assignments</h2><p>Assign the reporting relationships used across HRMS workflows.</p></div>
        {supervisor.data?.l1ResolutionStatus && <Badge tone={supervisor.data.l1ResolutionStatus === 'Resolved' ? 'success' : 'warning'}>{supervisor.data.l1ResolutionStatus}</Badge>}
      </div>
      {canEdit ? <form onSubmit={submit} noValidate>
        <fieldset className="form-section supervisor-form-section">
          <legend>Reporting relationships</legend>
          <div className="form-grid">
            {ROLES.map((role) => <SupervisorField
              key={role.idKey}
              id={String(role.idKey)}
              label={role.label}
              employeeId={employeeId}
              supervisorType={role.type}
              value={String(values[role.idKey] ?? '')}
              selectedCode={values[role.codeKey] as string | null | undefined}
              selectedName={values[role.nameKey] as string | null | undefined}
              onChange={(managerId, option) => updateRole(role, managerId, option)}
            />)}
          </div>
        </fieldset>
        <div className="form-actions">
          <button type="button" className="button button-secondary" onClick={() => { setValues(initialValues); setError(null); setSuccess(null) }} disabled={saving}>Cancel</button>
          <button type="submit" className="button button-primary" disabled={saving}>{saving ? 'Saving...' : 'Save supervisor assignments'}</button>
        </div>
      </form> : <Notice tone="info">You do not have permission to edit supervisor assignments.</Notice>}
    </Card>
  )
}

function supervisorRequest(value: EmployeeSupervisor): EmployeeSupervisorRequest {
  return {
    l1ManagerCode: value.l1ManagerCode,
    l1ManagerName: value.l1ManagerName,
    l1ManagerId: value.l1ManagerId,
    l2ManagerCode: value.l2ManagerCode,
    l2ManagerName: value.l2ManagerName,
    l2ManagerId: value.l2ManagerId,
    l3ManagerCode: value.l3ManagerCode,
    l3ManagerName: value.l3ManagerName,
    l3ManagerId: value.l3ManagerId,
    l4ManagerCode: value.l4ManagerCode,
    l4ManagerName: value.l4ManagerName,
    l4ManagerId: value.l4ManagerId,
    l5ManagerCode: value.l5ManagerCode,
    l5ManagerName: value.l5ManagerName,
    l5ManagerId: value.l5ManagerId,
    timeManagerCode: value.timeManagerCode,
    timeManagerName: value.timeManagerName,
    timeManagerId: value.timeManagerId,
    eroCode: value.eroCode,
    eroName: value.eroName,
    eroId: value.eroId,
    chroManagerCode: value.chroManagerCode,
    chroManagerName: value.chroManagerName,
    chroManagerId: value.chroManagerId,
  }
}

function isEffectiveToday(record: EmployeeEmploymentHistory): boolean {
  const now = new Date()
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  return record.effectiveFrom <= today && (record.effectiveTo == null || record.effectiveTo >= today)
}
