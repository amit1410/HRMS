import { useMemo, useState, type ReactNode } from 'react'
import { getMyAttendanceCalendar, getMyAttendanceDay, type AttendanceDay, type AttendanceDayDetail } from '../../api/attendance.ts'
import { ApiError } from '../../api/errors.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const labels: Record<string, string> = { Present: 'Present', Absent: 'Absent', OnLeave: 'Leave', OnDuty: 'On Duty', Holiday: 'Holiday', WeeklyOff: 'Weekly Off', Incomplete: 'Incomplete', NotProcessed: 'Not Processed' }
function minutes(value?: number | null) { return value == null ? '—' : `${Math.floor(value / 60).toString().padStart(2, '0')}:${(value % 60).toString().padStart(2, '0')}` }
function time(value?: string | null) { return value ? new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '—' }
function dateLabel(value: Date) { return value.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' }) }

type IconName = 'calendar' | 'chevron' | 'clock' | 'check' | 'briefcase' | 'leave' | 'alert' | 'info' | 'close'
function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  const paths: Record<IconName, ReactNode> = {
    calendar: <><rect x="3" y="4" width="18" height="17" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></>,
    chevron: <path d="m9 6 6 6-6 6" />, clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    check: <><path d="m5 12 4 4L19 6" /><circle cx="12" cy="12" r="9" /></>, briefcase: <><rect x="3" y="7" width="18" height="13" rx="2" /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18" /></>,
    leave: <><path d="M6 3h9l3 3v15H6z" /><path d="M15 3v4h4M9 12h6M9 16h5" /></>, alert: <><path d="M12 3 2.8 20h18.4L12 3Z" /><path d="M12 9v5M12 17h.01" /></>,
    info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" /></>, close: <><path d="m6 6 12 12M18 6 6 18" /></>,
  }
  return <svg className="my-attendance-icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function SummaryCard({ label, value, tone, unavailable = false }: { label: string; value: number | string; tone: string; unavailable?: boolean }) {
  const icon = label === 'Present' ? 'check' : label === 'On Duty' ? 'briefcase' : label === 'On Leave' ? 'leave' : 'alert'
  return <div className={`my-attendance-kpi ${tone}`}><span className="my-attendance-kpi-icon"><Icon name={icon} size={21} /></span><span className="my-attendance-kpi-label">{label}</span><strong>{value}</strong><small>{unavailable ? 'Unavailable' : 'Days'}</small></div>
}

function statusTone(status: string) { return `my-attendance-status-${status.toLowerCase()}` }

export function MyAttendancePage() {
  const today = new Date()
  const [month, setMonth] = useState(new Date(today.getFullYear(), today.getMonth(), 1))
  const [selected, setSelected] = useState<AttendanceDayDetail | null>(null)
  const [detailError, setDetailError] = useState('')
  const query = useApiQuery(() => getMyAttendanceCalendar(month.getFullYear(), month.getMonth() + 1), [month.getFullYear(), month.getMonth() + 1])
  const rows = query.data ?? []
  const monthLabel = month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  const periodLabel = `${dateLabel(month)} – ${dateLabel(new Date(month.getFullYear(), month.getMonth() + 1, 0))}`
  const isForbidden = query.error instanceof ApiError && query.error.isForbidden
  const isUnavailable = Boolean(query.error)
  const counts = useMemo(() => ({ present: rows.filter(row => row.attendanceStatus === 'Present').length, onDuty: rows.filter(row => row.attendanceStatus === 'OnDuty').length, onLeave: rows.filter(row => row.attendanceStatus === 'OnLeave').length, incomplete: rows.filter(row => row.attendanceStatus === 'Incomplete').length }), [rows])

  async function openDay(row: AttendanceDay) {
    try { setDetailError(''); setSelected(await getMyAttendanceDay(row.date)) } catch (error) { setDetailError(error instanceof ApiError ? error.message : 'Unable to load attendance detail.') }
  }
  function moveMonth(offset: number) { setMonth(new Date(month.getFullYear(), month.getMonth() + offset, 1)) }

  return <div className="leave-admin-page attendance-read-page my-attendance-page">
    <section className="my-attendance-hero card"><PageHeader title="My Attendance" subtitle="Your attendance calendar and daily punch summary." /><div className="my-attendance-period" aria-label={`Selected attendance period: ${periodLabel}`}><span className="my-attendance-period-icon"><Icon name="calendar" size={21} /></span><span><strong>{monthLabel}</strong><small>{periodLabel}</small></span><button className="my-attendance-period-chevron" type="button" aria-label="Next month" onClick={() => moveMonth(1)}><Icon name="chevron" size={18} /></button></div></section>
    {isForbidden ? <div className="my-attendance-alert" role="alert"><Icon name="alert" size={20} /><span>You do not have permission to perform this action.</span></div> : null}
    {query.error && !isForbidden ? <Notice tone="error">{query.error.message}</Notice> : null}
    {detailError ? <Notice tone="error">{detailError}</Notice> : null}
    <section className="my-attendance-kpi-grid" aria-label="Attendance summary"><SummaryCard label="Present" value={query.isLoading ? '…' : isUnavailable ? '—' : counts.present} tone="is-present" unavailable={isUnavailable} /><SummaryCard label="On Duty" value={query.isLoading ? '…' : isUnavailable ? '—' : counts.onDuty} tone="is-duty" unavailable={isUnavailable} /><SummaryCard label="On Leave" value={query.isLoading ? '…' : isUnavailable ? '—' : counts.onLeave} tone="is-leave" unavailable={isUnavailable} /><SummaryCard label="Incomplete" value={query.isLoading ? '…' : isUnavailable ? '—' : counts.incomplete} tone="is-incomplete" unavailable={isUnavailable} /></section>
    <Card className="my-attendance-calendar-card" title={<span className="my-attendance-card-title"><span className="my-attendance-card-icon"><Icon name="calendar" size={20} /></span><span>{monthLabel}<small>{periodLabel}</small></span></span>} actions={<div className="my-attendance-calendar-actions"><button className="button button-secondary" type="button" aria-label="Previous month" onClick={() => moveMonth(-1)}><span aria-hidden="true">‹</span> Previous</button><button className="button button-secondary" type="button" onClick={() => setMonth(new Date(today.getFullYear(), today.getMonth(), 1))}>Current month</button><button className="button button-secondary" type="button" aria-label="Next month" onClick={() => moveMonth(1)}>Next <span aria-hidden="true">›</span></button></div>}>
      {isForbidden ? <div className="my-attendance-unavailable"><span className="my-attendance-empty-icon"><Icon name="info" size={28} /></span><div><h3>Attendance data unavailable</h3><p>You do not have permission to view attendance for this account.</p></div></div> : query.isLoading ? <div className="my-attendance-loading" aria-label="Loading attendance"><span /><span /><span /><span /></div> : rows.length === 0 ? <div className="my-attendance-empty"><span className="my-attendance-empty-icon"><Icon name="calendar" size={30} /></span><h3>No attendance records available</h3><p>No attendance rows are available for this month.</p></div> : <div className="table-wrap my-attendance-table-wrap"><table className="data-table my-attendance-table"><thead><tr><th>Date</th><th>Day Type</th><th>Status</th><th>Shift</th><th>Effective in</th><th>Effective out</th><th>Worked</th><th>Source</th><th>Flags</th><th /></tr></thead><tbody>{rows.map(row => <tr key={row.date}><td><strong>{row.date}</strong><small>{row.dayOfWeek}</small></td><td>{row.dayType}</td><td><span className={`my-attendance-status ${statusTone(row.attendanceStatus)}`}>{labels[row.attendanceStatus] ?? row.attendanceStatus}</span></td><td>{row.shiftCode ?? '—'}</td><td>{time(row.firstPunchAtUtc)}</td><td>{time(row.lastPunchAtUtc)}</td><td>{minutes(row.workedMinutes)}</td><td>{row.assignmentSource}</td><td>{[row.isLateIn && 'Late', row.isEarlyOut && 'Early Out', row.leaveConflict && 'Leave Conflict'].filter(Boolean).join(', ') || '—'}</td><td><button className="button button-link" type="button" onClick={() => void openDay(row)}>Details</button></td></tr>)}</tbody></table></div>}
    </Card>
    {selected ? <div className="calendar-event-dialog" role="dialog" aria-modal="true" aria-labelledby="attendance-detail-title"><div className="calendar-event-dialog-card"><button type="button" className="dialog-close" aria-label="Close details" onClick={() => setSelected(null)}><Icon name="close" size={18} /></button><h2 id="attendance-detail-title">Attendance details — {selected.day.date}</h2><dl className="detail-list"><div><dt>Status</dt><dd>{labels[selected.day.attendanceStatus] ?? selected.day.attendanceStatus}</dd></div><div><dt>Shift</dt><dd>{selected.day.shiftName ?? selected.day.shiftCode ?? '—'}</dd></div><div><dt>Effective In / Out</dt><dd>{time(selected.day.firstPunchAtUtc)} / {time(selected.day.lastPunchAtUtc)}</dd></div><div><dt>Worked / Expected</dt><dd>{minutes(selected.day.workedMinutes)} / {minutes(selected.day.expectedWorkMinutes)}</dd></div><div><dt>Punches / Sessions</dt><dd>{selected.punches.length} / {selected.sessions.length}</dd></div><div><dt>Source / Calendar</dt><dd>{selected.day.assignmentSource} / {selected.day.calendarSource}</dd></div></dl>{selected.day.processingMessage ? <Notice tone="info">{selected.day.processingMessage}</Notice> : null}{selected.day.leaveConflict ? <Notice tone="error">Leave Conflict</Notice> : null}<h3>Raw punches</h3>{selected.punches.length ? <ul>{selected.punches.map(punch => <li key={punch.id}>{time(punch.punchAtUtc)} · {punch.direction} · {punch.source}</li>)}</ul> : <p className="field-help">No raw punches recorded.</p>}<h3>Sessions</h3>{selected.sessions.length ? <ul>{selected.sessions.map(session => <li key={`${session.inAtUtc}-${session.outAtUtc}`}>{time(session.inAtUtc)} – {time(session.outAtUtc)} ({minutes(session.workedMinutes)})</li>)}</ul> : <p className="field-help">No complete sessions.</p>}</div></div> : null}
  </div>
}
