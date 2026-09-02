import { useState, type FormEvent } from 'react'
import {
  createBankDetail,
  deleteBankDetail,
  getBankDetailForEdit,
  getBankDetails,
  updateBankDetail,
} from '../../api/employeeSubsections.ts'
import { toApiError, type ApiError } from '../../api/errors.ts'
import {
  ACCOUNT_PURPOSES,
  ACCOUNT_TYPES,
  BANK_ACCOUNT_STATUSES,
  type AccountPurpose,
  type AccountType,
  type BankAccountStatus,
  type EmployeeBankDetail,
  type EmployeeBankDetailEdit,
  type EmployeeBankDetailRequest,
} from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { SelectField, TextField, type SelectOption } from '../../components/fields.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { loadBankOptions } from './referenceOptions.ts'

interface BankDetailsFormProps {
  /** The employee whose bank details are being viewed/edited. */
  employeeId: string
}

interface FormValues {
  bankId: string
  accountHolderName: string
  accountNumber: string
  accountType: AccountType
  accountPurpose: AccountPurpose
  status: BankAccountStatus
  ifscCode: string
  branchName: string
  effectiveFrom: string
}

interface EditorState {
  /** The record being edited, or null when adding a brand-new record. */
  record: EmployeeBankDetail | null
  values: FormValues
}

function emptyValues(): FormValues {
  return {
    bankId: '',
    accountHolderName: '',
    accountNumber: '',
    accountType: 'Savings',
    accountPurpose: 'Salary',
    status: 'Active',
    ifscCode: '',
    branchName: '',
    effectiveFrom: '',
  }
}

function valuesFrom(detail: EmployeeBankDetailEdit): FormValues {
  return {
    bankId: detail.bankId,
    accountHolderName: detail.accountHolderName,
    accountNumber: detail.accountNumber,
    accountType: detail.accountType,
    accountPurpose: detail.accountPurpose,
    status: detail.status,
    ifscCode: detail.ifscCode ?? '',
    branchName: detail.branchName ?? '',
    effectiveFrom: detail.effectiveFrom ?? '',
  }
}

function toRequest(values: FormValues): EmployeeBankDetailRequest {
  return {
    bankId: values.bankId,
    accountHolderName: values.accountHolderName,
    accountNumber: values.accountNumber,
    accountType: values.accountType,
    accountPurpose: values.accountPurpose,
    status: values.status,
    ifscCode: values.ifscCode || null,
    branchName: values.branchName || null,
    effectiveFrom: values.effectiveFrom || null,
  }
}

/**
 * Bank details for one employee. Records are a list (one per account purpose), each with its own
 * add / edit / update / delete. Delete is a soft delete — the record is deactivated, never removed —
 * and the bank itself is picked from the tenant-scoped bank master, never typed in.
 */
export function BankDetailsForm({ employeeId }: BankDetailsFormProps) {
  const { can } = useAuth()
  const canEditSensitive = can(Permissions.employee.edit) && can(Permissions.employeeSensitive.edit)
  const canViewSensitive = can(Permissions.employeeSensitive.view)
  const [editor, setEditor] = useState<EditorState | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirming, setConfirming] = useState<EmployeeBankDetail | null>(null)
  const [loadingEditId, setLoadingEditId] = useState<string | null>(null)

  const banks = useApiQuery((signal) => loadBankOptions({ activeOnly: true }, signal), [])
  const records = useApiQuery((signal) => getBankDetails(employeeId, signal), [employeeId])

  const bankOptions: SelectOption[] = [
    { value: '', label: '— Select a bank —' },
    ...(banks.data?.options ?? []),
  ]
  const editingRecord = editor?.record
  if (editingRecord && !bankOptions.some((option) => option.value === editingRecord.bankId)) {
    bankOptions.push({ value: editingRecord.bankId, label: editingRecord.bankName })
  }
  const bankNameFor = (detail: EmployeeBankDetail): string => {
    const option = banks.data?.options.find((o) => o.value === detail.bankId)
    return option ? option.label : detail.bankName
  }

  // Active purposes already in use, so the one-active-per-purpose rule is visible before the server
  // refuses it. When editing a record its own purpose stays selectable.
  const usedActivePurposes = new Set<AccountPurpose>(
    (records.data ?? [])
      .filter((r) => r.isActive && r.id !== editor?.record?.id)
      .map((r) => r.accountPurpose),
  )
  const purposeOptions: SelectOption[] = ACCOUNT_PURPOSES.filter(
    (p) => p === 'Unspecified' || !usedActivePurposes.has(p),
  ).map((p) => ({ value: p, label: p }))
  const statusOptions = (editor?.record ? BANK_ACCOUNT_STATUSES : ['Active']).map((status) => ({
    value: status,
    label: status,
  }))

  const fieldError = (field: string) => error?.fieldErrors[field]

  function beginAdd(): void {
    setError(null)
    setSuccess(null)
    setEditor({ record: null, values: emptyValues() })
  }

  async function beginEdit(detail: EmployeeBankDetail): Promise<void> {
    setError(null)
    setSuccess(null)
    setLoadingEditId(detail.id)
    try {
      const editable = await getBankDetailForEdit(employeeId, detail.id)
      setEditor({ record: detail, values: valuesFrom(editable) })
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setLoadingEditId(null)
    }
  }

  function cancelEdit(): void {
    setError(null)
    setEditor(null)
  }

  function update<F extends keyof FormValues>(field: F, value: FormValues[F]): void {
    setEditor((current) => (current ? { ...current, values: { ...current.values, [field]: value } } : current))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving || !editor) return

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      if (editor.record) {
        await updateBankDetail(employeeId, editor.record.id, toRequest(editor.values))
        setSuccess('Bank Details updated successfully.')
      } else {
        await createBankDetail(employeeId, toRequest(editor.values))
        setSuccess('Bank Details added successfully.')
      }
      setEditor(null)
      await records.refetch()
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  function requestDeactivate(detail: EmployeeBankDetail): void {
    setError(null)
    setSuccess(null)
    setConfirming(detail)
  }

  /** Returns normally on success; throws so the ConfirmDialog can show the server's own message. */
  async function confirmDeactivate(): Promise<void> {
    const detail = confirming
    if (!detail) return
    await deleteBankDetail(employeeId, detail.id)
    if (editor?.record?.id === detail.id) setEditor(null)
    setSuccess('Bank account deactivated.')
    setConfirming(null)
    await records.refetch()
  }

  if (records.error && !records.data) {
    return (
      <Card className="form-card employee-detail-form-card">
        <ErrorState error={records.error} onRetry={records.refetch} />
      </Card>
    )
  }

  return (
    <>
      <Card className="form-card employee-detail-form-card">
      {error && <Notice tone="error">{error.message}</Notice>}
      {success && <Notice tone="success">{success}</Notice>}

      {records.isLoading && !records.data ? (
        <div className="table-loading">
          <Spinner label="Loading bank details…" />
        </div>
      ) : (
        <>
          {records.data && records.data.length > 0 ? (
            <ul className="bank-list">
              {records.data.map((detail) => (
                <li key={detail.id} className={`bank-row${detail.isActive ? '' : ' is-inactive'}`}>
                  <div className="bank-row-main">
                    <span className="bank-row-title">
                      {bankNameFor(detail)}
                      <span className="badge badge-neutral" role="status">
                        {detail.isActive ? 'Current' : `Historical (${detail.status})`}
                      </span>
                    </span>
                    <span className="bank-row-sub">
                      {detail.accountPurpose} · {detail.accountType} · {detail.accountHolderName}
                    </span>
                    <span className="bank-row-meta">
                      {detail.branchName ? `${detail.branchName} · ` : ''}
                      {detail.maskedAccountNumber}{' / '}{detail.maskedIfscCode}
                      {detail.effectiveFrom ? ` · Effective ${detail.effectiveFrom}` : ''}
                    </span>
                  </div>
                  <div className="bank-row-actions">
                    {detail.isActive && (
                      <button
                        type="button"
                        className="button button-secondary"
                        onClick={() => void beginEdit(detail)}
                        disabled={!canEditSensitive || !canViewSensitive || loadingEditId === detail.id}
                      >
                      {loadingEditId === detail.id ? 'Loadingâ€¦' : 'Edit'}
                      </button>
                    )}
                    {detail.isActive && canEditSensitive && (
                      <button
                        type="button"
                        className="button button-danger"
                        onClick={() => requestDeactivate(detail)}
                      >
                        Deactivate
                      </button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <Notice tone="info">No bank accounts have been added yet.</Notice>
          )}

          {!editor ? (
            <div className="form-actions">
              <button
                type="button"
                className="button button-primary"
                onClick={beginAdd}
                disabled={!canEditSensitive}
              >
                Add Bank
              </button>
            </div>
          ) : (
            <form className="bank-editor" onSubmit={submit} noValidate>
              <fieldset className="form-section">
                <legend>{editor.record ? 'Edit Bank Account' : 'Add Bank Account'}</legend>
                <div className="form-grid">
                  <SelectField
                    id="bankId"
                    label="Bank"
                    value={editor.values.bankId}
                    onChange={(value) => update('bankId', value)}
                    options={bankOptions}
                    required
                    error={fieldError('bankId')}
                  />
                  <TextField
                    id="accountHolderName"
                    label="Account holder name"
                    value={editor.values.accountHolderName}
                    onChange={(value) => update('accountHolderName', value)}
                    maxLength={200}
                    required
                    error={fieldError('accountHolderName')}
                  />
                  <TextField
                    id="accountNumber"
                    label="Account number"
                    value={editor.values.accountNumber}
                    onChange={(value) => update('accountNumber', value)}
                    maxLength={30}
                    required
                    error={fieldError('accountNumber')}
                  />
                  <SelectField
                    id="accountType"
                    label="Account type"
                    value={editor.values.accountType}
                    onChange={(value) => update('accountType', value as AccountType)}
                    options={ACCOUNT_TYPES.map((t) => ({ value: t, label: t }))}
                    error={fieldError('accountType')}
                  />
                  <SelectField
                    id="accountPurpose"
                    label="Account purpose"
                    value={editor.values.accountPurpose}
                    onChange={(value) => update('accountPurpose', value as AccountPurpose)}
                    options={purposeOptions}
                    hint="One active account per purpose."
                    error={fieldError('accountPurpose')}
                  />
                  <SelectField
                    id="status"
                    label="Status"
                    value={editor.values.status}
                    onChange={(value) => update('status', value as BankAccountStatus)}
                    options={statusOptions}
                    hint={editor.record ? 'Frozen or Closed records become immutable history.' : 'New records must be Active.'}
                    error={fieldError('status')}
                  />
                  <TextField
                    id="ifscCode"
                    label="IFSC code"
                    value={editor.values.ifscCode}
                    onChange={(value) => update('ifscCode', value)}
                    maxLength={20}
                    hint="Optional."
                    error={fieldError('ifscCode')}
                  />
                  <TextField
                    id="branchName"
                    label="Branch name"
                    value={editor.values.branchName}
                    onChange={(value) => update('branchName', value)}
                    maxLength={200}
                    hint="Optional."
                    error={fieldError('branchName')}
                  />
                  <TextField
                    id="effectiveFrom"
                    label="Effective from"
                    type="date"
                    value={editor.values.effectiveFrom}
                    onChange={(value) => update('effectiveFrom', value)}
                    hint="Optional."
                    error={fieldError('effectiveFrom')}
                  />
                </div>
              </fieldset>

              <div className="form-actions">
                <button type="submit" className="button button-primary" disabled={saving}>
                  {saving ? <Spinner size={14} label="Saving…" /> : editor.record ? 'UPDATE' : 'SAVE'}
                </button>
                <button type="button" className="button button-secondary" onClick={cancelEdit}>
                  Cancel
                </button>
              </div>
            </form>
          )}
        </>
      )}
      </Card>

      {confirming && (
        <ConfirmDialog
          title="Deactivate bank account"
          message={<>Are you sure you want to deactivate this bank account?<br />{bankNameFor(confirming)} ({confirming.accountPurpose}) — it will be closed and retained as immutable history. Add a new account to replace it.</>}
          confirmLabel="Deactivate"
          onConfirm={confirmDeactivate}
          onClose={() => setConfirming(null)}
        />
      )}
    </>
  )
}
