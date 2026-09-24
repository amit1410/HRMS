import { useEffect, useState } from 'react'
import { getMyExitInterview, saveMyExitInterview, submitMyExitInterview, type ExitInterviewEmployee, type ExitInterviewResponse } from '../../api/separation.ts'

export function MyExitInterviewPage() {
  const [interview, setInterview] = useState<ExitInterviewEmployee | null>(null)
  const [responses, setResponses] = useState<Record<string, ExitInterviewResponse>>({})
  const [message, setMessage] = useState('')
  useEffect(() => { void getMyExitInterview().then(value => { setInterview(value); setResponses(Object.fromEntries(value.responses.map(response => [response.questionId, response]))) }).catch(error => setMessage(error instanceof Error ? error.message : 'Unable to load exit interview.')) }, [])
  if (message) return <section><h1>Exit interview</h1><p role="alert">{message}</p></section>
  if (!interview) return <section><h1>Exit interview</h1><p>Loading…</p></section>
  const readOnly = interview.status === 'EmployeeSubmitted' || interview.status === 'Completed' || interview.status === 'CompletedWithoutEmployeeResponse'
  const update = (questionId: string, response: Partial<ExitInterviewResponse>) => setResponses(current => ({ ...current, [questionId]: { questionId, selectedOptionCodes: [], ...current[questionId], ...response } }))
  const save = async () => { setInterview(await saveMyExitInterview(interview.employeeSeparationId, Object.values(responses))) ; setMessage('Draft saved.') }
  const submit = async () => { setInterview(await submitMyExitInterview(interview.employeeSeparationId)); setMessage('Response submitted.') }
  return <section><h1>Exit interview</h1><p>Status: {interview.status}</p>{interview.questions.map(question => <div key={question.id}><label htmlFor={question.id}>{question.questionText}{question.isRequired ? ' *' : ''}</label>{question.questionType === 'LongText' ? <textarea id={question.id} disabled={readOnly} value={responses[question.id]?.responseText ?? ''} onChange={event => update(question.id, { responseText: event.target.value })} /> : question.questionType === 'SingleChoice' ? <select id={question.id} disabled={readOnly} value={responses[question.id]?.selectedOptionCodes[0] ?? ''} onChange={event => update(question.id, { selectedOptionCodes: event.target.value ? [event.target.value] : [] })}><option value="">Select…</option>{question.options.map(option => <option key={option.code} value={option.code}>{option.label}</option>)}</select> : <input id={question.id} disabled={readOnly} value={responses[question.id]?.responseText ?? ''} onChange={event => update(question.id, { responseText: event.target.value })} />}</div>)}{!readOnly && <p><button type="button" onClick={() => void save()}>Save draft</button> <button type="button" onClick={() => void submit()}>Submit</button></p>}{message && <p role="status">{message}</p>}</section>
}
