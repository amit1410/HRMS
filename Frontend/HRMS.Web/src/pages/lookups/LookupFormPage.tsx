import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { hasFieldErrors, toApiError, type ApiError } from '../../api/errors.ts'
import { Card } from '../../components/Card.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { CheckboxField, TextAreaField, TextField } from '../../components/fields.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import type { FlashState } from '../../hooks/useFlash.ts'
import { returnPath } from '../../lib/returnTo.ts'
import type { LookupModule, LookupRecord, LookupRequest } from './lookupModules.ts'

/**
 * Create and edit for a {@link LookupModule} — one form serving departments and designations, and both
 * modes of each.
 *
 * There is no client-side validation. The rules live in `DepartmentRequestValidator` and in the service
 * that checks the code is unique *within the tenant*, and a copy of them here would be a second
 * definition to keep in step — one that could not check uniqueness anyway. So the form submits, and the
 * API's `errors[]` are rendered under the inputs they name. The user learns about the format rules from
 * the hint under the code field before they submit, not from a duplicate implementation of them.
 *
 * The one thing the form does decide for itself is what an empty description means: `null`, not `''`. The
 * write is a full replacement, so clearing the box has to clear the stored value.
 */
export function LookupFormPage({ module }: { module: LookupModule }) {
  const { id } = useParams()

  // Split rather than branched inside one component, because the edit mode has a fetch that the create
  // mode must not make — and a hook cannot be called conditionally. The `key` restarts the form when the
  // route moves from one record to another.
  return id === undefined ? (
    <CreateForm module={module} />
  ) : (
    <EditForm key={id} module={module} id={id} />
  )
}

function CreateForm({ module }: { module: LookupModule }) {
  useDocumentTitle(`New ${module.noun}`)

  return (
    <LookupForm
      module={module}
      heading={`New ${module.noun}`}
      subtitle={`Add a ${module.noun} to ${module.title.toLowerCase()}`}
      // New records start active: a department nobody can be assigned to is not what "add" means.
      initial={{ code: '', name: '', description: '', isActive: true }}
      submitLabel={`Create ${module.noun}`}
      onSubmit={(body) => module.create(body)}
      successMessage={(record) => `${record.name} was created.`}
    />
  )
}

function EditForm({ module, id }: { module: LookupModule; id: string }) {
  useDocumentTitle(`Edit ${module.noun}`)

  const { data, error, isLoading, refetch } = useApiQuery(
    (signal) => module.get(id, signal),
    [module.key, id],
  )

  if (error) {
    return (
      <>
        <PageHeader title={`Edit ${module.noun}`} />
        <Card>
          <ErrorState error={error} onRetry={refetch} />
        </Card>
      </>
    )
  }

  if (isLoading || !data) {
    return (
      <>
        <PageHeader title={`Edit ${module.noun}`} />
        <Card>
          <div className="table-loading">
            <Spinner label={`Loading ${module.noun}…`} />
          </div>
        </Card>
      </>
    )
  }

  return (
    <LookupForm
      module={module}
      heading={`Edit ${data.name}`}
      subtitle={`${data.code} · ${data.employeeCount} ${module.countHeader.toLowerCase()}`}
      initial={{
        code: data.code,
        name: data.name,
        description: data.description ?? '',
        isActive: data.isActive,
      }}
      submitLabel="Save changes"
      onSubmit={(body) => module.update(id, body)}
      successMessage={(record) => `${record.name} was updated.`}
    />
  )
}

interface FormValues {
  code: string
  name: string
  description: string
  isActive: boolean
}

interface LookupFormProps {
  module: LookupModule
  heading: string
  subtitle: string
  initial: FormValues
  submitLabel: string
  onSubmit: (body: LookupRequest) => Promise<LookupRecord>
  successMessage: (record: LookupRecord) => string
}

function LookupForm({
  module,
  heading,
  subtitle,
  initial,
  submitLabel,
  onSubmit,
  successMessage,
}: LookupFormProps) {
  const navigate = useNavigate()
  const location = useLocation()

  const [values, setValues] = useState<FormValues>(initial)
  const [error, setError] = useState<ApiError | null>(null)
  const [saving, setSaving] = useState(false)

  const returnTo = returnPath(location.state, module.basePath)

  function set<K extends keyof FormValues>(field: K, value: FormValues[K]): void {
    setValues((current) => ({ ...current, [field]: value }))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    setSaving(true)
    setError(null)
    try {
      const record = await onSubmit({
        code: values.code.trim(),
        name: values.name.trim(),
        // Empty means "no description", and the write replaces the whole record — `''` would store a
        // blank string where the column is meant to be null.
        description: values.description.trim() || null,
        isActive: values.isActive,
      })
      const state: FlashState = { flash: successMessage(record) }
      navigate(returnTo, { replace: true, state })
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  return (
    <>
      <PageHeader title={heading} subtitle={subtitle} />

      <Card className="form-card">
        {error && !hasFieldErrors(error) && (
          <Notice tone="error">{error.message}</Notice>
        )}

        <form onSubmit={submit} noValidate>
          <div className="form-grid">
            <TextField
              id="code"
              label="Code"
              value={values.code}
              onChange={(value) => set('code', value)}
              maxLength={20}
              required
              autoCapitalize="characters"
              error={fieldError('code')}
              hint={module.codeHint}
            />
            <TextField
              id="name"
              label="Name"
              value={values.name}
              onChange={(value) => set('name', value)}
              maxLength={100}
              required
              error={fieldError('name')}
            />
          </div>

          <TextAreaField
            id="description"
            label="Description"
            value={values.description}
            onChange={(value) => set('description', value)}
            maxLength={500}
            rows={3}
            error={fieldError('description')}
            hint="Optional. Up to 500 characters."
          />

          <CheckboxField
            id="isActive"
            label="Active"
            checked={values.isActive}
            onChange={(checked) => set('isActive', checked)}
            error={fieldError('isActive')}
            hint={`An inactive ${module.noun} keeps its existing employees but cannot be chosen for new ones.`}
          />

          <div className="form-actions">
            <Link className="button button-secondary" to={returnTo}>
              Cancel
            </Link>
            <button type="submit" className="button button-primary" disabled={saving}>
              {saving ? <Spinner size={14} label="Saving…" /> : submitLabel}
            </button>
          </div>
        </form>
      </Card>
    </>
  )
}
