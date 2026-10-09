import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import {
  createConfirmationRatingScale,
  getConfirmationRatingScale,
  updateConfirmationRatingScale,
  type ConfirmationRatingScaleRequest,
  type ConfirmationRatingScaleValue,
} from '../../api/confirmationReview.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'

const emptyValue = (ratingValue: number): ConfirmationRatingScaleValue => ({
  ratingValue,
  label: '',
  description: '',
  displayOrder: ratingValue,
  isActive: true,
})

const emptyForm: ConfirmationRatingScaleRequest = {
  code: '',
  name: '',
  description: '',
  minRating: 1,
  maxRating: 5,
  isActive: true,
  displayOrder: 0,
  effectiveFrom: null,
  effectiveTo: null,
  values: [emptyValue(1)],
}

export function RatingScaleEditorPage() {
  const { scaleId } = useParams()
  const isNew = !scaleId
  const navigate = useNavigate()
  const { can } = useAuth()
  const canManage = can(Permissions.confirmation.ratingScaleManage)

  const [form, setForm] = useState<ConfirmationRatingScaleRequest>(emptyForm)
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    if (isNew) return
    let active = true
    setLoading(true)
    getConfirmationRatingScale(scaleId!)
      .then((detail) => {
        if (!active) return
        setForm({
          code: detail.scale.code,
          name: detail.scale.name,
          description: detail.scale.description ?? '',
          minRating: detail.scale.minRating,
          maxRating: detail.scale.maxRating,
          isActive: detail.scale.isActive,
          displayOrder: detail.scale.displayOrder,
          effectiveFrom: detail.scale.effectiveFrom ?? null,
          effectiveTo: detail.scale.effectiveTo ?? null,
          values: detail.values.length > 0 ? detail.values : [emptyValue(detail.scale.minRating)],
        })
      })
      .catch((caught) => active && setError(caught instanceof ApiError ? caught : new ApiError('Unable to load rating scale.')))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [isNew, scaleId])

  function setValue(index: number, patch: Partial<ConfirmationRatingScaleValue>) {
    setForm((previous) => ({
      ...previous,
      values: previous.values.map((value, i) => (i === index ? { ...value, ...patch } : value)),
    }))
  }

  function addValue() {
    setForm((previous) => ({ ...previous, values: [...previous.values, emptyValue(previous.values.length + previous.minRating)] }))
  }

  function removeValue(index: number) {
    setForm((previous) => ({ ...previous, values: previous.values.filter((_, i) => i !== index) }))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const payload: ConfirmationRatingScaleRequest = {
        ...form,
        description: form.description || null,
        effectiveFrom: form.effectiveFrom || null,
        effectiveTo: form.effectiveTo || null,
      }
      if (isNew) {
        const created = await createConfirmationRatingScale(payload)
        navigate(`/confirmation/rating-scales/${created.scale.id}`)
        return
      }
      await updateConfirmationRatingScale(scaleId!, payload)
      setNotice('Rating scale saved.')
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to save rating scale.'))
    } finally {
      setSaving(false)
    }
  }

  const fieldErrors = error?.fieldErrors ?? {}

  if (loading) {
    return (
      <div className="leave-admin-page">
        <div className="state-block">
          <Spinner label="Loading rating scale" />
        </div>
      </div>
    )
  }

  return (
    <div className="leave-admin-page">
      <PageHeader
        title={isNew ? 'New Rating Scale' : `Rating Scale · ${form.code}`}
        subtitle="Define the numeric range and the labels shown for each rating point."
        actions={
          <Link className="button button-secondary" to="/confirmation/rating-scales">
            Back to list
          </Link>
        }
      />
      {notice ? (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      ) : null}
      {error ? <Notice tone="error">{error.message}</Notice> : null}
      <Card title="Scale details">
        <form className="form-stack" onSubmit={submit} aria-busy={saving}>
          <div className="form-grid">
            <label className="field">
              <span>Code *</span>
              <input
                className={fieldErrors.code ? 'input has-error' : 'input'}
                required
                maxLength={40}
                value={form.code}
                readOnly={!isNew}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, code: event.target.value }))}
              />
              {fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}
            </label>
            <label className="field">
              <span>Name *</span>
              <input
                className={fieldErrors.name ? 'input has-error' : 'input'}
                required
                maxLength={150}
                value={form.name}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, name: event.target.value }))}
              />
              {fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}
            </label>
            <label className="field">
              <span>Minimum rating *</span>
              <input
                className="input"
                type="number"
                value={form.minRating}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, minRating: Number(event.target.value) }))}
              />
            </label>
            <label className="field">
              <span>Maximum rating *</span>
              <input
                className={fieldErrors.maxRating ? 'input has-error' : 'input'}
                type="number"
                value={form.maxRating}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, maxRating: Number(event.target.value) }))}
              />
              {fieldErrors.maxRating ? <small className="field-error">{fieldErrors.maxRating}</small> : null}
            </label>
            <label className="field">
              <span>Display order</span>
              <input
                className="input"
                type="number"
                value={form.displayOrder}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, displayOrder: Number(event.target.value) }))}
              />
            </label>
          </div>
          <label className="field">
            <span>Description</span>
            <textarea
              className="input"
              maxLength={1000}
              value={form.description ?? ''}
              disabled={!canManage}
              onChange={(event) => setForm((previous) => ({ ...previous, description: event.target.value }))}
            />
          </label>
          <label className="checkbox-field">
            <input
              type="checkbox"
              checked={form.isActive}
              disabled={!canManage}
              onChange={(event) => setForm((previous) => ({ ...previous, isActive: event.target.checked }))}
            />
            <span>Active</span>
          </label>

          <h3>Rating values</h3>
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Rating scale values</caption>
              <thead>
                <tr>
                  <th scope="col">Value</th>
                  <th scope="col">Label</th>
                  <th scope="col">Description</th>
                  <th scope="col">Order</th>
                  <th scope="col">Active</th>
                  {canManage ? <th scope="col">Remove</th> : null}
                </tr>
              </thead>
              <tbody>
                {form.values.map((value, index) => (
                  <tr key={index}>
                    <td>
                      <input
                        className="input"
                        type="number"
                        value={value.ratingValue}
                        disabled={!canManage}
                        onChange={(event) => setValue(index, { ratingValue: Number(event.target.value) })}
                      />
                    </td>
                    <td>
                      <input
                        className="input"
                        value={value.label}
                        disabled={!canManage}
                        onChange={(event) => setValue(index, { label: event.target.value })}
                      />
                    </td>
                    <td>
                      <input
                        className="input"
                        value={value.description ?? ''}
                        disabled={!canManage}
                        onChange={(event) => setValue(index, { description: event.target.value })}
                      />
                    </td>
                    <td>
                      <input
                        className="input"
                        type="number"
                        value={value.displayOrder}
                        disabled={!canManage}
                        onChange={(event) => setValue(index, { displayOrder: Number(event.target.value) })}
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={value.isActive}
                        disabled={!canManage}
                        onChange={(event) => setValue(index, { isActive: event.target.checked })}
                      />
                    </td>
                    {canManage ? (
                      <td>
                        <button className="row-action" type="button" onClick={() => removeValue(index)}>
                          Remove
                        </button>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {canManage ? (
            <div className="form-actions">
              <button className="button button-secondary" type="button" onClick={addValue}>
                Add value
              </button>
              <button className="button button-primary" type="submit" disabled={saving}>
                {saving ? <Spinner size={14} label="Saving…" /> : isNew ? 'Create Rating Scale' : 'Save Changes'}
              </button>
            </div>
          ) : null}
        </form>
      </Card>
    </div>
  )
}
