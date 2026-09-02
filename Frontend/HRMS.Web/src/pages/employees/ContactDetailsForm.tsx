import { useEffect, useState, type FormEvent } from 'react'
import { getContact, upsertContact } from '../../api/employeeSubsections.ts'
import { hasFieldErrors, toApiError, type ApiError } from '../../api/errors.ts'
import type { EmployeeContact } from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { TextField } from '../../components/fields.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

interface ContactDetailsFormProps {
  /** The employee whose contact details are being viewed/edited. */
  employeeId: string
}

/**
 * The values of the Contact Details form, keyed to the `EmployeeContact`/`EmployeeContactRequest` field
 * names so the move between form state and the API is a straight copy. `officialPhone` renders under the
 * "Mobile Number" label and `personalPhone` under "Alternate Mobile Number".
 *
 * `sameAsCurrentAddress` is loaded from the record and echoed back unchanged on every save. The checkbox
 * itself lives on the Address Details tab; its sole job here is to make sure a Contact Details save never
 * wipes the flag that the Address tab owns.
 */
interface ContactValues {
  officialEmail: string
  officialPhone: string
  personalPhone: string
  personalEmail: string
  alternateEmail: string
  sameAsCurrentAddress: boolean
}

function emptyContactValues(): ContactValues {
  return {
    officialEmail: '',
    officialPhone: '',
    personalPhone: '',
    personalEmail: '',
    alternateEmail: '',
    sameAsCurrentAddress: false,
  }
}

function toContactValues(contact: EmployeeContact): ContactValues {
  return {
    officialEmail: contact.officialEmail ?? '',
    officialPhone: contact.officialPhone ?? '',
    personalPhone: contact.personalPhone ?? '',
    personalEmail: contact.personalEmail ?? '',
    alternateEmail: contact.alternateEmail ?? '',
    sameAsCurrentAddress: contact.sameAsCurrentAddress,
  }
}

function toContactRequest(values: ContactValues): {
  officialEmail?: string | null
  officialPhone?: string | null
  personalPhone?: string | null
  personalEmail?: string | null
  alternateEmail?: string | null
  sameAsCurrentAddress: boolean
} {
  return {
    officialEmail: values.officialEmail || null,
    officialPhone: values.officialPhone || null,
    personalPhone: values.personalPhone || null,
    personalEmail: values.personalEmail || null,
    alternateEmail: values.alternateEmail || null,
    sameAsCurrentAddress: values.sameAsCurrentAddress,
  }
}

/**
 * The Employee → Contact Details tab, shown on the employee detail page.
 *
 * It is self-managing the same way the Personal Details form is: it loads the existing contact on mount,
 * keeps every value in local form state, and has its own Save/Update button. It owns the phone numbers
 * and emails; the "Permanent Address same as Current Address" flag is edited on the Address Details tab,
 * so this form merely echoes the flag it loads back on save rather than re-deciding it.
 */
export function ContactDetailsForm({ employeeId }: ContactDetailsFormProps) {
  const [values, setValues] = useState<ContactValues>(emptyContactValues())
  const [hasSaved, setHasSaved] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const query = useApiQuery((signal) => getContact(employeeId, signal), [employeeId])

  useEffect(() => {
    if (query.data) {
      setValues(toContactValues(query.data))
      setHasSaved(
        query.data.officialEmail != null ||
          query.data.officialPhone != null ||
          query.data.personalPhone != null ||
          query.data.personalEmail != null ||
          query.data.alternateEmail != null,
      )
    }
  }, [query.data])

  function set<K extends keyof ContactValues>(field: K, value: ContactValues[K]): void {
    setValues((previous) => ({ ...previous, [field]: value }))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    const body = toContactRequest(values)

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      const saved = await upsertContact(employeeId, body)
      setValues(toContactValues(saved))
      setHasSaved(true)
      setSuccess('Contact Details updated successfully.')
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  const noContactYet = query.error?.status === 404

  if (!query.data && !noContactYet && query.isLoading) {
    return (
      <Card className="form-card employee-detail-form-card">
        <div className="table-loading">
          <Spinner label="Loading contact details…" />
        </div>
      </Card>
    )
  }

  if (!query.data && !noContactYet && query.error) {
    return (
      <Card className="form-card employee-detail-form-card">
        <ErrorState error={query.error} onRetry={query.refetch} />
      </Card>
    )
  }

  return (
    <Card className="form-card employee-detail-form-card">
      {error && !hasFieldErrors(error) && <Notice tone="error">{error.message}</Notice>}
      {success && <Notice tone="success">{success}</Notice>}

      <form onSubmit={submit} noValidate>
        <fieldset className="form-section">
          <legend>Contact Details</legend>
          <div className="form-grid">
            <TextField
              id="officialEmail"
              label="Official Email"
              type="email"
              value={values.officialEmail}
              onChange={(value) => set('officialEmail', value)}
              maxLength={256}
              autoComplete="email"
              error={fieldError('officialEmail')}
            />
            <TextField
              id="officialPhone"
              label="Mobile Number"
              type="tel"
              value={values.officialPhone}
              onChange={(value) => set('officialPhone', value)}
              maxLength={30}
              autoComplete="tel"
              error={fieldError('officialPhone')}
            />
            <TextField
              id="personalPhone"
              label="Alternate Mobile Number"
              type="tel"
              value={values.personalPhone}
              onChange={(value) => set('personalPhone', value)}
              maxLength={30}
              autoComplete="tel"
              hint="Optional."
              error={fieldError('personalPhone')}
            />
            <TextField
              id="personalEmail"
              label="Personal Email"
              type="email"
              value={values.personalEmail}
              onChange={(value) => set('personalEmail', value)}
              maxLength={256}
              autoComplete="email"
              error={fieldError('personalEmail')}
            />
            <TextField
              id="alternateEmail"
              label="Alternate Email"
              type="email"
              value={values.alternateEmail}
              onChange={(value) => set('alternateEmail', value)}
              maxLength={256}
              autoComplete="email"
              hint="Optional."
              error={fieldError('alternateEmail')}
            />
          </div>
        </fieldset>

        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? <Spinner size={14} label="Saving…" /> : hasSaved ? 'UPDATE' : 'SAVE'}
          </button>
        </div>
      </form>
    </Card>
  )
}
