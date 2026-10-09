import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { listConfirmationPolicies, type ConfirmationPolicy } from '../../api/confirmation.ts'
import {
  createConfirmationReviewQuestion,
  getConfirmationReviewQuestion,
  listConfirmationRatingScales,
  updateConfirmationReviewQuestion,
  type ConfirmationRatingScale,
  type ConfirmationReviewQuestionApplicability,
  type ConfirmationReviewQuestionOption,
  type ConfirmationReviewQuestionRequest,
  type ReviewQuestionType,
} from '../../api/confirmationReview.ts'
import { listDepartments, listDesignations, listEmployeeTypes, listGrades, listWorkLocations } from '../../api/masterData.ts'
import type { MasterLookup } from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'

const questionTypes: ReviewQuestionType[] = ['Rating', 'YesNo', 'Text', 'Dropdown', 'MultiSelect']

const emptyForm: ConfirmationReviewQuestionRequest = {
  code: '',
  questionText: '',
  helpText: '',
  questionType: 'Rating',
  isMandatory: false,
  isActive: true,
  displayOrder: 0,
  effectiveFrom: null,
  effectiveTo: null,
  confirmationRatingScaleId: null,
  options: [],
  applicability: [],
}

const emptyApplicability: ConfirmationReviewQuestionApplicability = {
  confirmationPolicyId: null,
  departmentId: null,
  gradeId: null,
  designationId: null,
  employeeTypeId: null,
  workLocationId: null,
}

export function ReviewQuestionEditorPage() {
  const { questionId } = useParams()
  const isNew = !questionId
  const navigate = useNavigate()
  const { can } = useAuth()
  const canManage = can(Permissions.confirmation.reviewQuestionManage)

  const [form, setForm] = useState<ConfirmationReviewQuestionRequest>(emptyForm)
  const [scales, setScales] = useState<ConfirmationRatingScale[]>([])
  const [policies, setPolicies] = useState<ConfirmationPolicy[]>([])
  const [departments, setDepartments] = useState<MasterLookup[]>([])
  const [grades, setGrades] = useState<MasterLookup[]>([])
  const [designations, setDesignations] = useState<MasterLookup[]>([])
  const [employeeTypes, setEmployeeTypes] = useState<MasterLookup[]>([])
  const [workLocations, setWorkLocations] = useState<MasterLookup[]>([])
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    Promise.all([
      listConfirmationRatingScales({ isActive: true, pageSize: 100 }),
      listConfirmationPolicies({ isActive: true, pageSize: 100 }),
      listDepartments(),
      listGrades(),
      listDesignations(),
      listEmployeeTypes(),
      listWorkLocations(),
    ])
      .then(([s, p, d, g, des, et, wl]) => {
        if (!active) return
        setScales(s.items)
        setPolicies(p.items)
        setDepartments(d)
        setGrades(g)
        setDesignations(des)
        setEmployeeTypes(et)
        setWorkLocations(wl)
      })
      .catch(() => {
        /* lookups are best-effort; the form still works without them */
      })
    return () => {
      active = false
    }
  }, [])

  useEffect(() => {
    if (isNew) return
    let active = true
    setLoading(true)
    getConfirmationReviewQuestion(questionId!)
      .then((detail) => {
        if (!active) return
        setForm({
          code: detail.question.code,
          questionText: detail.question.questionText,
          helpText: detail.question.helpText ?? '',
          questionType: detail.question.questionType,
          isMandatory: detail.question.isMandatory,
          isActive: detail.question.isActive,
          displayOrder: detail.question.displayOrder,
          effectiveFrom: detail.question.effectiveFrom ?? null,
          effectiveTo: detail.question.effectiveTo ?? null,
          confirmationRatingScaleId: detail.question.confirmationRatingScaleId ?? null,
          options: detail.options,
          applicability: detail.applicability,
        })
      })
      .catch((caught) => active && setError(caught instanceof ApiError ? caught : new ApiError('Unable to load review question.')))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [isNew, questionId])

  const needsOptions = form.questionType === 'Dropdown' || form.questionType === 'MultiSelect'
  const needsScale = form.questionType === 'Rating'

  function setOption(index: number, patch: Partial<ConfirmationReviewQuestionOption>) {
    setForm((previous) => ({
      ...previous,
      options: (previous.options ?? []).map((option, i) => (i === index ? { ...option, ...patch } : option)),
    }))
  }

  function setApplicability(index: number, patch: Partial<ConfirmationReviewQuestionApplicability>) {
    setForm((previous) => ({
      ...previous,
      applicability: (previous.applicability ?? []).map((row, i) => (i === index ? { ...row, ...patch } : row)),
    }))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const payload: ConfirmationReviewQuestionRequest = {
        ...form,
        helpText: form.helpText || null,
        effectiveFrom: form.effectiveFrom || null,
        effectiveTo: form.effectiveTo || null,
        confirmationRatingScaleId: needsScale ? form.confirmationRatingScaleId || null : null,
        options: needsOptions ? form.options ?? [] : [],
      }
      if (isNew) {
        const created = await createConfirmationReviewQuestion(payload)
        navigate(`/confirmation/review-questions/${created.question.id}`)
        return
      }
      await updateConfirmationReviewQuestion(questionId!, payload)
      setNotice('Review question saved.')
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to save review question.'))
    } finally {
      setSaving(false)
    }
  }

  const fieldErrors = error?.fieldErrors ?? {}

  if (loading) {
    return (
      <div className="leave-admin-page">
        <div className="state-block">
          <Spinner label="Loading review question" />
        </div>
      </div>
    )
  }

  return (
    <div className="leave-admin-page">
      <PageHeader
        title={isNew ? 'New Review Question' : `Review Question · ${form.code}`}
        subtitle="Configure a confirmation review question, its answer type and where it applies."
        actions={
          <Link className="button button-secondary" to="/confirmation/review-questions">
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

      <form className="form-stack" onSubmit={submit} aria-busy={saving}>
        <Card title="Question">
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
              <span>Type *</span>
              <select
                className="input"
                value={form.questionType}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, questionType: event.target.value as ReviewQuestionType }))}
              >
                {questionTypes.map((type) => (
                  <option key={type} value={type}>
                    {type}
                  </option>
                ))}
              </select>
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
            {needsScale ? (
              <label className="field">
                <span>Rating scale *</span>
                <select
                  className={fieldErrors.confirmationRatingScaleId ? 'input has-error' : 'input'}
                  value={form.confirmationRatingScaleId ?? ''}
                  disabled={!canManage}
                  onChange={(event) => setForm((previous) => ({ ...previous, confirmationRatingScaleId: event.target.value || null }))}
                >
                  <option value="">Select a rating scale…</option>
                  {scales.map((scale) => (
                    <option key={scale.id} value={scale.id}>
                      {scale.name} ({scale.minRating}–{scale.maxRating})
                    </option>
                  ))}
                </select>
                {fieldErrors.confirmationRatingScaleId ? <small className="field-error">{fieldErrors.confirmationRatingScaleId}</small> : null}
              </label>
            ) : null}
          </div>
          <label className="field">
            <span>Question text *</span>
            <input
              className={fieldErrors.questionText ? 'input has-error' : 'input'}
              required
              maxLength={500}
              value={form.questionText}
              disabled={!canManage}
              onChange={(event) => setForm((previous) => ({ ...previous, questionText: event.target.value }))}
            />
            {fieldErrors.questionText ? <small className="field-error">{fieldErrors.questionText}</small> : null}
          </label>
          <label className="field">
            <span>Help text</span>
            <textarea
              className="input"
              maxLength={1000}
              value={form.helpText ?? ''}
              disabled={!canManage}
              onChange={(event) => setForm((previous) => ({ ...previous, helpText: event.target.value }))}
            />
          </label>
          <div className="form-grid">
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={form.isMandatory}
                disabled={!canManage}
                onChange={(event) => setForm((previous) => ({ ...previous, isMandatory: event.target.checked }))}
              />
              <span>Mandatory</span>
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
          </div>
        </Card>

        {needsOptions ? (
          <Card title="Options" subtitle="Each selectable choice for this question.">
            <div className="table-wrap">
              <table className="data-table">
                <caption className="sr-only">Question options</caption>
                <thead>
                  <tr>
                    <th scope="col">Code</th>
                    <th scope="col">Label</th>
                    <th scope="col">Order</th>
                    <th scope="col">Active</th>
                    {canManage ? <th scope="col">Remove</th> : null}
                  </tr>
                </thead>
                <tbody>
                  {(form.options ?? []).map((option, index) => (
                    <tr key={index}>
                      <td>
                        <input className="input" value={option.code} disabled={!canManage} onChange={(event) => setOption(index, { code: event.target.value })} />
                      </td>
                      <td>
                        <input className="input" value={option.label} disabled={!canManage} onChange={(event) => setOption(index, { label: event.target.value })} />
                      </td>
                      <td>
                        <input className="input" type="number" value={option.displayOrder} disabled={!canManage} onChange={(event) => setOption(index, { displayOrder: Number(event.target.value) })} />
                      </td>
                      <td>
                        <input type="checkbox" checked={option.isActive} disabled={!canManage} onChange={(event) => setOption(index, { isActive: event.target.checked })} />
                      </td>
                      {canManage ? (
                        <td>
                          <button className="row-action" type="button" onClick={() => setForm((previous) => ({ ...previous, options: (previous.options ?? []).filter((_, i) => i !== index) }))}>
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
              <button
                className="button button-secondary"
                type="button"
                onClick={() => setForm((previous) => ({ ...previous, options: [...(previous.options ?? []), { code: '', label: '', displayOrder: (previous.options?.length ?? 0) + 1, isActive: true }] }))}
              >
                Add option
              </button>
            ) : null}
          </Card>
        ) : null}

        <Card title="Applicability" subtitle="Optionally scope this question to a policy and/or employment dimensions. Leave empty to apply to everyone.">
          {(form.applicability ?? []).map((row, index) => (
            <div className="form-grid" key={index}>
              <label className="field">
                <span>Confirmation policy</span>
                <select className="input" value={row.confirmationPolicyId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { confirmationPolicyId: event.target.value || null })}>
                  <option value="">Any</option>
                  {policies.map((policy) => (
                    <option key={policy.id} value={policy.id}>
                      {policy.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Department</span>
                <select className="input" value={row.departmentId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { departmentId: event.target.value || null })}>
                  <option value="">Any</option>
                  {departments.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Grade</span>
                <select className="input" value={row.gradeId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { gradeId: event.target.value || null })}>
                  <option value="">Any</option>
                  {grades.map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Designation</span>
                <select className="input" value={row.designationId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { designationId: event.target.value || null })}>
                  <option value="">Any</option>
                  {designations.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Employee type</span>
                <select className="input" value={row.employeeTypeId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { employeeTypeId: event.target.value || null })}>
                  <option value="">Any</option>
                  {employeeTypes.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Work location</span>
                <select className="input" value={row.workLocationId ?? ''} disabled={!canManage} onChange={(event) => setApplicability(index, { workLocationId: event.target.value || null })}>
                  <option value="">Any</option>
                  {workLocations.map((l) => (
                    <option key={l.id} value={l.id}>
                      {l.name}
                    </option>
                  ))}
                </select>
              </label>
              {canManage ? (
                <div className="field">
                  <span>&nbsp;</span>
                  <button className="button button-secondary" type="button" onClick={() => setForm((previous) => ({ ...previous, applicability: (previous.applicability ?? []).filter((_, i) => i !== index) }))}>
                    Remove row
                  </button>
                </div>
              ) : null}
            </div>
          ))}
          {canManage ? (
            <button className="button button-secondary" type="button" onClick={() => setForm((previous) => ({ ...previous, applicability: [...(previous.applicability ?? []), { ...emptyApplicability }] }))}>
              Add applicability row
            </button>
          ) : null}
        </Card>

        {canManage ? (
          <div className="form-actions">
            <button className="button button-primary" type="submit" disabled={saving}>
              {saving ? <Spinner size={14} label="Saving…" /> : isNew ? 'Create Question' : 'Save Changes'}
            </button>
          </div>
        ) : null}
      </form>
    </div>
  )
}
