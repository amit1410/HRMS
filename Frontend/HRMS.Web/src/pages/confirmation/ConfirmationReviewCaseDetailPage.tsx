import { useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  getConfirmationDecisionHistory,
  getConfirmationReviewCase,
  getConfirmationReviewCaseHistory,
  previewConfirmationDecision,
  saveConfirmationReviewerDraft,
  submitConfirmationDecision,
  submitConfirmationReviewerReview,
  type ConfirmationDecisionPreview,
  type ConfirmationOutcome,
  type ConfirmationReviewCaseQuestion,
  type ConfirmationReviewerReview,
  type ConfirmationReviewerType,
  type ConfirmationReviewResponseInput,
} from '../../api/confirmationReviewCase.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

type ResponseState = Record<string, ConfirmationReviewResponseInput>

export function ConfirmationReviewCaseDetailPage() {
  const { caseId = '' } = useParams()
  const { can } = useAuth()
  const [reload, setReload] = useState(0)

  const canManager = can(Permissions.confirmation.reviewManagerSubmit)
  const canHrbp = can(Permissions.confirmation.reviewHrbpSubmit)
  const canDecide = can(Permissions.confirmation.decide)
  const [selectedReviewer, setSelectedReviewer] = useState<ConfirmationReviewerType>(canManager ? 'ReportingManager' : 'Hrbp')

  const detail = useApiQuery((signal) => getConfirmationReviewCase(caseId, signal), [caseId, reload])
  const history = useApiQuery((signal) => getConfirmationReviewCaseHistory(caseId, signal), [caseId, reload])

  const reviewer: ConfirmationReviewerType | null = canManager && canHrbp ? selectedReviewer : canManager ? 'ReportingManager' : canHrbp ? 'Hrbp' : null

  if (detail.isLoading) {
    return (
      <div className="leave-admin-page">
        <div className="state-block">
          <Spinner label="Loading review case" />
        </div>
      </div>
    )
  }
  if (detail.error || !detail.data) {
    return (
      <div className="leave-admin-page">
        <Notice tone="error">{detail.error?.message ?? 'Review case not found.'}</Notice>
      </div>
    )
  }

  const { case: reviewCase, questions, reviews } = detail.data

  return (
    <div className="leave-admin-page">
      <PageHeader title="Confirmation Review" subtitle={reviewCase.employeeName ?? reviewCase.employeeCode ?? reviewCase.employeeId} />

      <Card title="Case">
        <dl className="summary-grid">
          <div><dt>Status</dt><dd>{reviewCase.status}</dd></div>
          <div><dt>Probation</dt><dd>{reviewCase.probationStartDate} → {reviewCase.probationEndDate}</dd></div>
          <div><dt>Due</dt><dd>{reviewCase.dueDate}</dd></div>
          <div><dt>Questions</dt><dd>{questions.length}</dd></div>
        </dl>
      </Card>

      {canManager && canHrbp ? (
        <label>
          Review as{' '}
          <select value={selectedReviewer} onChange={(event) => setSelectedReviewer(event.target.value as ConfirmationReviewerType)}>
            <option value="ReportingManager">Reporting Manager</option>
            <option value="Hrbp">HR / HRBP</option>
          </select>
        </label>
      ) : null}

      {reviewer ? (
        <ReviewerForm
          caseId={caseId}
          reviewerType={reviewer}
          questions={questions}
          existing={reviews.find((r) => r.reviewRound === reviewCase.reviewRound && r.reviewerType === reviewer && r.status !== 'Superseded') ?? null}
          onChanged={() => setReload((value) => value + 1)}
        />
      ) : (
        <Notice tone="info">You do not have permission to submit a review for this case.</Notice>
      )}

      {canDecide ? <DecisionPanel caseId={caseId} onChanged={() => setReload((value) => value + 1)} /> : null}

      <Card title="Submitted reviews">
        {reviews.length === 0 ? (
          <p className="muted">No reviews yet.</p>
        ) : (
          <ul className="plain-list">
            {reviews.map((r) => (
              <li key={r.id}>
                <strong>{r.reviewerType === 'ReportingManager' ? 'Reporting Manager' : 'HR / HRBP'}</strong> — v{r.versionNumber}{' '}
                <span className="muted">({r.status}{r.submittedAtUtc ? `, ${new Date(r.submittedAtUtc).toLocaleString()}` : ''})</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title="History">
        {history.isLoading ? (
          <Spinner label="Loading history" />
        ) : history.data && history.data.length > 0 ? (
          <ul className="plain-list">
            {history.data.map((event) => (
              <li key={event.id}>
                <code>{event.eventType}</code> <span className="muted">{new Date(event.occurredAtUtc).toLocaleString()}</span>
                {event.details ? <div className="muted">{event.details}</div> : null}
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted">No history.</p>
        )}
      </Card>
    </div>
  )
}

const OUTCOME_LABELS: Record<ConfirmationOutcome, string> = {
  Pending: 'Pending',
  Confirmed: 'Confirm',
  ProbationExtended: 'Extend probation',
  NotConfirmed: 'Not confirmed',
  AdditionalReview: 'Additional review',
}

function DecisionPanel({ caseId, onChanged }: { caseId: string; onChanged: () => void }) {
  const [preview, setPreview] = useState<ConfirmationDecisionPreview | null>(null)
  const [outcome, setOutcome] = useState<ConfirmationOutcome | ''>('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const decisionHistory = useApiQuery((signal) => getConfirmationDecisionHistory(caseId, signal), [caseId, message])

  async function calculate() {
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = await previewConfirmationDecision(caseId)
      setPreview(result)
      if (result.permittedOutcomes.length > 0) setOutcome((current) => current || result.calculatedOutcome)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to calculate decision.')
    } finally {
      setBusy(false)
    }
  }

  async function submit() {
    if (!outcome) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = await submitConfirmationDecision(caseId, { outcome, decisionReason: reason || null })
      setMessage(result.message)
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit decision.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card title="Decision" subtitle="Calculate the recommended outcome, then submit the final decision.">
      {error ? <Notice tone="error">{error}</Notice> : null}
      {message ? <Notice tone="success">{message}</Notice> : null}

      <div className="form-actions">
        <button className="button" type="button" disabled={busy} onClick={calculate}>Calculate / preview</button>
      </div>

      {preview ? (
        <>
          <dl className="summary-grid">
            <div><dt>Approval required</dt><dd>{preview.requiredApproval}{preview.approvalSequence === 'ManagerThenHrbp' ? ' (Manager → HRBP)' : ''}</dd></div>
            <div><dt>Overall rating</dt><dd>{preview.overallRating ?? '—'} <span className="muted">({preview.overallRatingMethod})</span></dd></div>
            <div><dt>Calculated outcome</dt><dd>{OUTCOME_LABELS[preview.calculatedOutcome]}{preview.appliedRuleName ? ` — ${preview.appliedRuleName}` : ''}</dd></div>
            <div><dt>Ready for decision</dt><dd>{preview.isReadyForDecision ? 'Yes' : 'No'}</dd></div>
          </dl>

          <ul className="plain-list">
            {preview.reviewers.map((r) => (
              <li key={r.reviewerType}>
                <strong>{r.reviewerType === 'ReportingManager' ? 'Reporting Manager' : 'HR / HRBP'}</strong>{' '}
                <span className="muted">{r.isRequired ? 'required' : 'optional'} · {r.isSubmitted ? 'submitted' : 'not submitted'}{r.hasComments ? ' · commented' : ''}</span>
              </li>
            ))}
          </ul>

          {preview.explanation ? <p className="muted">{preview.explanation}</p> : null}
          {preview.blockingReasons.length > 0 ? (
            <Notice tone="info">
              <ul className="plain-list">{preview.blockingReasons.map((b) => <li key={b}>{b}</li>)}</ul>
            </Notice>
          ) : null}

          {preview.isReadyForDecision ? (
            <div className="form-grid">
              <div className="form-field">
                <label className="form-label" htmlFor="decision-outcome">Outcome</label>
                <select id="decision-outcome" className="input" value={outcome} onChange={(e) => setOutcome(e.target.value as ConfirmationOutcome)}>
                  <option value="">—</option>
                  {preview.permittedOutcomes.map((o) => <option key={o} value={o}>{OUTCOME_LABELS[o]}</option>)}
                </select>
              </div>
              <div className="form-field">
                <label className="form-label" htmlFor="decision-reason">Decision comments</label>
                <textarea id="decision-reason" className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />
              </div>
              <div className="form-actions">
                <button className="button button-primary" type="button" disabled={busy || !outcome} onClick={submit}>Submit decision</button>
              </div>
            </div>
          ) : null}
        </>
      ) : null}

      <h4>Decision history</h4>
      {decisionHistory.data && decisionHistory.data.decisions.length > 0 ? (
        <ul className="plain-list">
          {decisionHistory.data.decisions.map((d) => (
            <li key={d.id}>
              <strong>{OUTCOME_LABELS[d.outcome]}</strong>{' '}
              <span className="muted">{new Date(d.decidedAtUtc).toLocaleString()}{d.calculatedOverallRating != null ? ` · rating ${d.calculatedOverallRating}` : ''}</span>
              {d.decisionReason ? <div className="muted">{d.decisionReason}</div> : null}
            </li>
          ))}
        </ul>
      ) : (
        <p className="muted">No decisions yet.</p>
      )}
    </Card>
  )
}

function ReviewerForm({
  caseId,
  reviewerType,
  questions,
  existing,
  onChanged,
}: {
  caseId: string
  reviewerType: ConfirmationReviewerType
  questions: ConfirmationReviewCaseQuestion[]
  existing: ConfirmationReviewerReview | null
  onChanged: () => void
}) {
  const submitted = existing?.status === 'Submitted'
  const [responses, setResponses] = useState<ResponseState>(() => seed(existing))
  const [comments, setComments] = useState(existing?.overallComments ?? '')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  function update(questionId: string, patch: Partial<ConfirmationReviewResponseInput>) {
    setResponses((current) => ({
      ...current,
      [questionId]: { confirmationReviewCaseQuestionId: questionId, ...current[questionId], ...patch },
    }))
  }

  async function persist(mode: 'draft' | 'submit') {
    setBusy(true)
    setError(null)
    setMessage(null)
    const body = { overallComments: comments, responses: Object.values(responses) }
    try {
      if (mode === 'draft') {
        await saveConfirmationReviewerDraft(caseId, reviewerType, body)
        setMessage('Draft saved.')
      } else {
        await submitConfirmationReviewerReview(caseId, reviewerType, body)
        setMessage('Review submitted.')
      }
      onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save review.')
    } finally {
      setBusy(false)
    }
  }

  const title = reviewerType === 'ReportingManager' ? 'Reporting Manager review' : 'HR / HRBP review'

  return (
    <Card title={title} subtitle={submitted ? 'A new submission will supersede the current version (the prior one is preserved).' : undefined}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      {message ? <Notice tone="success">{message}</Notice> : null}
      <div className="form-grid">
        {questions.map((question) => (
          <div key={question.id} className="form-field">
            <label className="form-label">
              {question.questionText}
              {question.isMandatory ? <span aria-hidden="true"> *</span> : null}
            </label>
            {question.helpText ? <p className="muted">{question.helpText}</p> : null}
            {renderInput(question, responses[question.id], (patch) => update(question.id, patch))}
          </div>
        ))}
        <div className="form-field">
          <label className="form-label" htmlFor="overall-comments">Overall comments</label>
          <textarea
            id="overall-comments"
            className="input"
            rows={3}
            value={comments}
            onChange={(event) => setComments(event.target.value)}
          />
        </div>
      </div>
      <div className="form-actions">
        <button className="button" type="button" disabled={busy} onClick={() => persist('draft')}>
          Save draft
        </button>
        <button className="button button-primary" type="button" disabled={busy} onClick={() => persist('submit')}>
          Submit review
        </button>
      </div>
    </Card>
  )
}

function renderInput(
  question: ConfirmationReviewCaseQuestion,
  value: ConfirmationReviewResponseInput | undefined,
  update: (patch: Partial<ConfirmationReviewResponseInput>) => void,
) {
  const label = question.questionText
  switch (question.questionType) {
    case 'Rating':
      return (
        <select
          aria-label={label}
          className="input"
          value={value?.ratingValue ?? ''}
          onChange={(event) => update({ ratingValue: event.target.value === '' ? null : Number(event.target.value) })}
        >
          <option value="">—</option>
          {question.ratingValues.map((rv) => (
            <option key={rv.ratingValue} value={rv.ratingValue}>
              {rv.ratingValue} — {rv.label}
            </option>
          ))}
        </select>
      )
    case 'YesNo':
      return (
        <select
          aria-label={label}
          className="input"
          value={value?.boolValue === true ? 'yes' : value?.boolValue === false ? 'no' : ''}
          onChange={(event) => update({ boolValue: event.target.value === '' ? null : event.target.value === 'yes' })}
        >
          <option value="">—</option>
          <option value="yes">Yes</option>
          <option value="no">No</option>
        </select>
      )
    case 'Text':
      return (
        <textarea
          aria-label={label}
          className="input"
          rows={2}
          value={value?.textValue ?? ''}
          onChange={(event) => update({ textValue: event.target.value })}
        />
      )
    case 'Dropdown':
      return (
        <select
          aria-label={label}
          className="input"
          value={value?.selectedOptionCodes?.[0] ?? ''}
          onChange={(event) => update({ selectedOptionCodes: event.target.value === '' ? [] : [event.target.value] })}
        >
          <option value="">—</option>
          {question.options.map((option) => (
            <option key={option.code} value={option.code}>
              {option.label}
            </option>
          ))}
        </select>
      )
    case 'MultiSelect': {
      const selected = value?.selectedOptionCodes ?? []
      return (
        <fieldset className="checkbox-group" aria-label={label}>
          {question.options.map((option) => (
            <label key={option.code} className="checkbox-row">
              <input
                type="checkbox"
                checked={selected.includes(option.code)}
                onChange={(event) =>
                  update({
                    selectedOptionCodes: event.target.checked
                      ? [...selected, option.code]
                      : selected.filter((code) => code !== option.code),
                  })
                }
              />
              {option.label}
            </label>
          ))}
        </fieldset>
      )
    }
    default:
      return null
  }
}

function seed(existing: ConfirmationReviewerReview | null): ResponseState {
  const state: ResponseState = {}
  for (const response of existing?.responses ?? []) {
    state[response.confirmationReviewCaseQuestionId] = {
      confirmationReviewCaseQuestionId: response.confirmationReviewCaseQuestionId,
      ratingValue: response.ratingValue,
      boolValue: response.boolValue,
      textValue: response.textValue,
      selectedOptionCodes: response.selectedOptionCodes,
    }
  }
  return state
}
