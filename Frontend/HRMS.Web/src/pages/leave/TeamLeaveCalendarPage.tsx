import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { listLeaveCalendar, type LeaveCalendarEvent } from '../../api/leaveCalendar.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

function pad(value: number) { return String(value).padStart(2, '0') }
function dateValue(date: Date) { return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` }
function monthStart(value: Date) { return new Date(value.getFullYear(), value.getMonth(), 1) }
function monthEnd(value: Date) { return new Date(value.getFullYear(), value.getMonth() + 1, 0) }
function statusLabel(status: LeaveCalendarEvent['status']) { return status === 'PendingApproval' ? 'Pending Approval' : 'Approved' }

export function TeamLeaveCalendarPage() {
  const [month, setMonth] = useState(() => monthStart(new Date()))
  const [selected, setSelected] = useState<LeaveCalendarEvent | null>(null)
  const from = dateValue(monthStart(month)); const to = dateValue(monthEnd(month))
  const query = useApiQuery(signal => listLeaveCalendar(from, to, signal), [from, to])
  const cells = useMemo(() => [...Array(monthStart(month).getDay()).fill(null), ...Array.from({ length: monthEnd(month).getDate() }, (_, index) => index + 1)], [month])
  const eventsForDay = (day: number) => (query.data ?? []).filter(event => event.startDate <= `${from.slice(0, 8)}${pad(day)}` && event.endDate >= `${from.slice(0, 8)}${pad(day)}`)

  return <div className="leave-admin-page">
    <PageHeader title="Team Leave Calendar" subtitle="View authorized team absences without exposing private request details." actions={<Link className="button button-secondary" to="/leave-management/my-requests">My Leave Requests</Link>} />
    <Card title={month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })} actions={<div className="calendar-nav"><button className="button button-secondary" type="button" aria-label="Previous month" onClick={() => setMonth(value => new Date(value.getFullYear(), value.getMonth() - 1, 1))}>←</button><button className="button button-secondary" type="button" onClick={() => setMonth(monthStart(new Date()))}>Today</button><button className="button button-secondary" type="button" aria-label="Next month" onClick={() => setMonth(value => new Date(value.getFullYear(), value.getMonth() + 1, 1))}>→</button></div>} isRefreshing={query.isRefreshing}>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading team leave calendar" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : query.data?.length === 0 ? <EmptyState title="No leave in this month" message="There are no authorized Leave absences in the selected date range." /> : <><div className="calendar-weekdays" aria-hidden="true">{['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map(day => <span key={day}>{day}</span>)}</div><div className="leave-calendar-grid" aria-label="Team Leave calendar">{cells.map((day, index) => <div className="leave-calendar-day" key={`${month.toISOString()}-${index}`}>{day ? <><span className="calendar-day-number">{day}</span>{eventsForDay(day).map(event => <button type="button" className={`calendar-event calendar-event-${event.status.toLowerCase()}`} key={event.requestId} onClick={() => setSelected(event)}><strong>{event.employeeName || event.employeeCode}</strong><span>{event.leaveTypeName}</span><small>{statusLabel(event.status)}</small></button>)}</> : null}</div>)}</div></>}
    </Card>
    {selected ? <div className="calendar-event-dialog" role="dialog" aria-modal="true" aria-labelledby="calendar-event-title"><div className="calendar-event-dialog-card"><button type="button" className="dialog-close" aria-label="Close event details" onClick={() => setSelected(null)}>×</button><h2 id="calendar-event-title">Leave details</h2><dl className="detail-list"><div><dt>Employee</dt><dd>{selected.employeeName || selected.employeeCode}</dd></div><div><dt>Leave Type</dt><dd>{selected.leaveTypeName} ({selected.leaveTypeCode})</dd></div><div><dt>Dates</dt><dd>{selected.startDate} → {selected.endDate}</dd></div><div><dt>Duration</dt><dd>{selected.chargeableQuantity}</dd></div><div><dt>Status</dt><dd>{statusLabel(selected.status)}</dd></div></dl></div></div> : null}
  </div>
}
