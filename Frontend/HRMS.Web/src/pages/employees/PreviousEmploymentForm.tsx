import { useState, type FormEvent } from 'react'
import { createPreviousEmployment, getPreviousEmployments, updatePreviousEmployment } from '../../api/employeeSubsections.ts'
import { ApiError, toApiError } from '../../api/errors.ts'
import type { EmployeePreviousEmployment, EmployeePreviousEmploymentRequest, EmploymentType } from '../../api/types.ts'
import { EMPLOYMENT_TYPES } from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { SelectField, TextField } from '../../components/fields.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
const EMPTY: EmployeePreviousEmploymentRequest = { company: '', designation: null, location: null, employmentType: 'FullTime', tenureFrom: null, tenureTill: null, documentOfProof: null }

type PreviousEmploymentFormProps = { employeeId: string; onSaved?: () => void | Promise<void> }

export function PreviousEmploymentForm({ employeeId, onSaved }: PreviousEmploymentFormProps) {
  const records = useApiQuery((signal) => getPreviousEmployments(employeeId, signal), [employeeId])
  const [editing, setEditing] = useState<EmployeePreviousEmployment | null>(null)
  const [open, setOpen] = useState(false)
  const [values, setValues] = useState(EMPTY)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const update = (key: keyof EmployeePreviousEmploymentRequest, value: unknown) => setValues((current) => ({ ...current, [key]: value }))
  const begin = (record?: EmployeePreviousEmployment) => {
    setError(null)
    setEditing(record ?? null)
    setOpen(true)
    setValues(record ? { company: record.company, designation: record.designation, location: record.location, employmentType: record.employmentType, tenureFrom: record.tenureFrom, tenureTill: record.tenureTill, documentOfProof: record.documentOfProof } : EMPTY)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (saving) return
    if (values.tenureFrom && values.tenureTill && values.tenureFrom > values.tenureTill) {
      setError(new ApiError('Start date must be before end date.', { status: 400, fieldErrors: { tenureTill: 'End date must be after start date.' } }))
      return
    }
    setSaving(true)
    setError(null)
    try {
      if (editing) {
        await updatePreviousEmployment(employeeId, editing.id, values)
        setSuccess('Previous employment updated successfully.')
      } else {
        await createPreviousEmployment(employeeId, values)
        setSuccess('Previous employment added successfully.')
      }
      setOpen(false)
      setEditing(null)
      await records.refetch()
      await onSaved?.()
    } catch (cause) {
      setError(toApiError(cause))
    } finally {
      setSaving(false)
    }
  }

  return <Card className="form-card employee-detail-form-card"><div className="employee-section-heading"><div><p className="eyebrow">Career history</p><h2>Previous Employment</h2><p>Record employment experience before joining the organization.</p></div><button className="button button-primary" type="button" onClick={() => begin()}>+ Add Previous Employment</button></div>{error && <Notice tone="error">{error.message}</Notice>}{success && <Notice tone="success">{success}</Notice>}{records.isLoading && !records.data ? <Spinner label="Loading previous employment…" /> : records.data?.length ? <div className="previous-employment-timeline">{[...records.data].sort((a, b) => (b.tenureFrom ?? '').localeCompare(a.tenureFrom ?? '')).map((record) => <article className="previous-employment-card" key={record.id}><span className="change-reason-badge">{record.employmentType}</span><h3>{record.company}</h3><p>{record.designation || '—'} · {record.location || '—'}</p><p>{record.tenureFrom || '—'} – {record.tenureTill || 'Present'}</p><button className="button button-secondary" type="button" onClick={() => begin(record)}>Edit</button></article>)}</div> : <Notice tone="info">No previous employment has been recorded.</Notice>}{open && <form className="previous-employment-editor" onSubmit={submit} noValidate><fieldset className="form-section"><legend>{editing ? 'Edit Previous Employment' : 'Add Previous Employment'}</legend><div className="form-grid"><TextField id="previousCompany" label="Company" value={values.company} onChange={(value) => update('company', value)} required /><TextField id="previousDesignation" label="Designation" value={values.designation ?? ''} onChange={(value) => update('designation', value)} /><TextField id="previousLocation" label="Location" value={values.location ?? ''} onChange={(value) => update('location', value)} /><SelectField id="previousEmploymentType" label="Employment type" value={values.employmentType} onChange={(value) => update('employmentType', value as EmploymentType)} options={EMPLOYMENT_TYPES.map((value) => ({ value, label: value }))} /><TextField id="previousFrom" label="Start date" type="date" value={values.tenureFrom ?? ''} onChange={(value) => update('tenureFrom', value || null)} required /><TextField id="previousTill" label="End date" type="date" value={values.tenureTill ?? ''} onChange={(value) => update('tenureTill', value || null)} required /></div></fieldset><div className="form-actions"><button className="button button-secondary" type="button" onClick={() => setOpen(false)} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save Previous Employment'}</button></div></form>}</Card>
}
