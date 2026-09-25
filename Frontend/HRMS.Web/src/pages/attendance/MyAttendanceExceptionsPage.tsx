import { useState } from 'react'
import { listMyAttendanceExceptions, type AttendanceOperationalException } from '../../api/attendance.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

export function MyAttendanceExceptionsPage() {
  useDocumentTitle('My Attendance Exceptions')
  const [page, setPage] = useState(1)
  const query = useApiQuery(() => listMyAttendanceExceptions({ page, pageSize: 25 }), [page])
  const rows = query.data?.items ?? []
  return <div className="attendance-exceptions-page"><PageHeader title="My Attendance Exceptions" subtitle="Actionable exceptions derived from your authoritative daily Attendance." /><Card title="Exceptions" subtitle={query.data ? `${query.data.totalCount} matching day(s)` : undefined} isRefreshing={query.isRefreshing}>{query.isLoading ? <Spinner label="Loading my exceptions" /> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title="No attendance exceptions" message="Your current Attendance data has no actionable exceptions." /> : <ExceptionTable rows={rows} />}{query.data && query.data.totalPages > 1 ? <div className="pagination"><button className="button button-secondary" type="button" disabled={!query.data.hasPreviousPage} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {query.data.page} of {query.data.totalPages}</span><button className="button button-secondary" type="button" disabled={!query.data.hasNextPage} onClick={() => setPage(value => value + 1)}>Next</button></div> : null}</Card></div>
}

function ExceptionTable({ rows }: { rows: AttendanceOperationalException[] }) {
  return <div className="table-wrap"><table className="data-table"><caption className="sr-only">My Attendance exceptions</caption><thead><tr><th>Date</th><th>Shift</th><th>Exception</th><th>Attendance</th><th>IN</th><th>OUT</th><th>Worked</th><th>Late</th><th>Early</th><th>Workflow</th><th>Message</th></tr></thead><tbody>{rows.map(row => <tr key={row.id}><td>{row.businessDate}</td><td>{row.shiftCode ?? '—'}</td><td>{row.exceptionType}</td><td>{row.attendanceStatus}</td><td>{row.firstPunchAtUtc?.slice(11, 19) ?? '—'}</td><td>{row.lastPunchAtUtc?.slice(11, 19) ?? '—'}</td><td>{row.workedMinutes ?? '—'} min</td><td>{row.lateMinutes} min</td><td>{row.earlyDepartureMinutes} min</td><td>{row.relatedRequestId ? 'Correction pending' : 'No pending request'}</td><td>{row.message}</td></tr>)}</tbody></table></div>
}
