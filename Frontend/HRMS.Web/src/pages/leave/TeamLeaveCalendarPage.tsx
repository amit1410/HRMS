import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { listLeaveCalendar, type LeaveCalendarEvent } from '../../api/leaveCalendar.ts'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate } from '../../lib/format.ts'

function pad(value: number) { return String(value).padStart(2, '0') }
function dateValue(date: Date) { return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` }
function monthStart(value: Date) { return new Date(value.getFullYear(), value.getMonth(), 1) }
function monthEnd(value: Date) { return new Date(value.getFullYear(), value.getMonth() + 1, 0) }
function statusLabel(status: LeaveCalendarEvent['status']) { return status === 'PendingApproval' ? 'Pending Approval' : 'Approved' }
function eventName(event: LeaveCalendarEvent) { return event.employeeName || event.employeeCode }

export function TeamLeaveCalendarPage() {
  const [month, setMonth] = useState(() => monthStart(new Date()))
  const [selected, setSelected] = useState<LeaveCalendarEvent | null>(null)
  const from = dateValue(monthStart(month)); const to = dateValue(monthEnd(month))
  const query = useApiQuery(signal => listLeaveCalendar(from, to, signal), [from, to])
  const events = query.data ?? []
  const cells = useMemo(() => {
    const first = monthStart(month)
    const last = monthEnd(month)
    const start = new Date(first.getFullYear(), first.getMonth(), 1 - first.getDay())
    const count = Math.ceil((last.getDate() + first.getDay()) / 7) * 7
    return Array.from({ length: count }, (_, index) => { const date = new Date(start); date.setDate(start.getDate() + index); return date })
  }, [month])
  const eventsForDay = (value: string) => events.filter(event => event.startDate <= value && event.endDate >= value)
  const upcoming = useMemo(() => [...events].sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5), [events])
  const employeeCount = new Set(events.map(event => event.employeeId || event.employeeCode)).size
  const leaveTypes = useMemo(() => [...new Map(events.map(event => [event.leaveTypeCode, event.leaveTypeName])).entries()].map(([code, name]) => ({ code, name })), [events])
  const monthLabel = month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  const today = dateValue(new Date())

  return <div className="leave-admin-page team-calendar-page">
    <div className="team-calendar-breadcrumb"><Link to="/">Home</Link><span aria-hidden="true">/</span><Link to="/leave-management">Leave</Link><span aria-hidden="true">/</span><strong>Team Leave Calendar</strong></div>
    <PageHeader title="Team Leave Calendar" subtitle="View authorized team absences without exposing private request details." actions={<Link className="button button-secondary" to="/leave-management/my-requests">My Leave Requests</Link>} />
    <div className="team-calendar-layout">
      <section className="card team-calendar-main">
        <header className="card-header"><div><h2 className="card-title team-calendar-card-title"><CalendarIcon />{monthLabel}</h2><p className="card-subtitle">Authorized team absences for the selected month</p></div><div className="card-actions"><div className="team-calendar-toolbar"><button className="icon-button" type="button" aria-label="Previous month" onClick={() => setMonth(value => new Date(value.getFullYear(), value.getMonth() - 1, 1))}><ArrowIcon direction="left" /></button><button className="button button-secondary team-calendar-today" type="button" onClick={() => setMonth(monthStart(new Date()))}>Today</button><button className="icon-button" type="button" aria-label="Next month" onClick={() => setMonth(value => new Date(value.getFullYear(), value.getMonth() + 1, 1))}><ArrowIcon direction="right" /></button></div></div></header>
        <div className={query.isRefreshing ? 'card-body is-refreshing' : 'card-body'}>
          {query.isLoading ? <CalendarSkeleton /> : query.error ? <div className="team-calendar-error"><div className="team-calendar-error-icon"><CalendarIcon /></div><div><h3>Unable to load team leave calendar.</h3><p>{query.error.message}</p><button className="button button-secondary" type="button" onClick={query.refetch}>Retry</button></div></div> : <><div className="calendar-weekdays" aria-hidden="true">{['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map(day => <span key={day}>{day}</span>)}</div><div className="leave-calendar-grid" aria-label={`${monthLabel} team leave calendar`}>{cells.map(date => { const value = dateValue(date); const dayEvents = eventsForDay(value); const isToday = value === today; const inMonth = date.getMonth() === month.getMonth(); return <div className={`leave-calendar-day${inMonth ? '' : ' is-overflow'}${isToday ? ' is-today' : ''}`} key={value}><span className="calendar-day-number" aria-label={isToday ? `${value}, Today` : value}>{date.getDate()}</span>{isToday ? <span className="calendar-today-label">Today</span> : null}{dayEvents.map(event => <button type="button" className={`calendar-event calendar-event-${event.status.toLowerCase()}`} key={`${event.requestId}-${value}`} onClick={() => setSelected(event)} aria-label={`${eventName(event)}, ${event.leaveTypeName}, ${statusLabel(event.status)}, ${formatDate(event.startDate)} to ${formatDate(event.endDate)}`}><span className="calendar-event-marker" aria-hidden="true" /><strong>{eventName(event)}</strong><small>{event.leaveTypeName}</small></button>)}</div> })}</div>{events.length === 0 ? <div className="team-calendar-empty"><div className="team-calendar-empty-icon"><PeopleIcon /></div><div><h3>No team leave this month</h3><p>There are no authorized team absences in the selected date range.</p></div></div> : null}<div className="team-calendar-privacy"><InfoIcon /><span>Showing leave for your authorized team members only. Private leave request details are not exposed.</span></div></>}
        </div>
      </section>
      <aside className="team-calendar-rail">
        <section className="card team-calendar-side-card"><header className="card-header"><div><h2 className="card-title team-calendar-side-title"><LayersIcon />Leave Type Legend</h2><p className="card-subtitle">Types available in this month</p></div></header><div className="card-body">{leaveTypes.length === 0 ? <p className="muted">Leave type details are not available for this range.</p> : <ul className="team-calendar-legend">{leaveTypes.map(type => <li key={type.code}><span className="legend-dot" aria-hidden="true" />{type.name}</li>)}</ul>}</div></section>
        <section className="card team-calendar-side-card"><header className="card-header"><div><h2 className="card-title team-calendar-side-title"><PeopleIcon />Team Summary</h2><p className="card-subtitle">This month</p></div></header><div className="card-body"><div className="team-summary-grid"><div><strong>{employeeCount}</strong><span>Employees with leave</span></div><div><strong>{events.length}</strong><span>Leave events</span></div></div></div></section>
        <section className="card team-calendar-side-card"><header className="card-header"><div><h2 className="card-title team-calendar-side-title"><CalendarIcon />Upcoming Team Leave</h2><p className="card-subtitle">Authorized absences in this month</p></div>{events.length > 5 ? <div className="card-actions"><span className="team-calendar-count">{events.length} events</span></div> : null}</header><div className="card-body">{upcoming.length === 0 ? <p className="muted">No upcoming authorized team leave.</p> : <ul className="team-calendar-upcoming">{upcoming.map(event => <li key={event.requestId}><span className="upcoming-avatar">{initials(eventName(event))}</span><div><strong>{eventName(event)}</strong><span>{event.leaveTypeName}</span><small>{formatDate(event.startDate)} – {formatDate(event.endDate)}</small></div></li>)}</ul>}</div></section>
      </aside>
    </div>
    {selected ? <div className="calendar-event-dialog" role="dialog" aria-modal="true" aria-labelledby="calendar-event-title"><div className="calendar-event-dialog-card"><button type="button" className="dialog-close" aria-label="Close event details" onClick={() => setSelected(null)}>×</button><h2 id="calendar-event-title">Leave details</h2><dl className="detail-list"><div><dt>Employee</dt><dd>{eventName(selected)}</dd></div><div><dt>Leave Type</dt><dd>{selected.leaveTypeName} ({selected.leaveTypeCode})</dd></div><div><dt>Dates</dt><dd>{formatDate(selected.startDate)} → {formatDate(selected.endDate)}</dd></div><div><dt>Duration</dt><dd>{selected.chargeableQuantity} days</dd></div><div><dt>Status</dt><dd>{statusLabel(selected.status)}</dd></div></dl></div></div> : null}
  </div>
}

function initials(value: string) { return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || '?' }
function CalendarSkeleton() { return <div className="team-calendar-skeleton" aria-label="Loading team leave calendar"><div className="team-calendar-skeleton-toolbar" /><div className="team-calendar-skeleton-grid">{Array.from({ length: 35 }, (_, index) => <span key={index} />)}</div></div> }
function CalendarIcon() { return <svg className="team-calendar-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M16 3v4M8 3v4M3 10h18" /></svg> }
function PeopleIcon() { return <svg className="team-calendar-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><circle cx="9" cy="8" r="3" /><path d="M3.5 20a5.5 5.5 0 0 1 11 0M16 5.5a3 3 0 0 1 0 5.8M16 14a5 5 0 0 1 4.5 6" /></svg> }
function LayersIcon() { return <svg className="team-calendar-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5M3 16l9 5 9-5" /></svg> }
function InfoIcon() { return <svg className="team-calendar-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" /></svg> }
function ArrowIcon({ direction }: { direction: 'left' | 'right' }) { return <svg className="team-calendar-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><path d={direction === 'left' ? 'm15 5-7 7 7 7' : 'm9 5 7 7-7 7'} /></svg> }
