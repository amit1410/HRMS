import { Link } from 'react-router-dom'
import { getAttendanceOperationsDashboard, getManagerAttendance, type AttendanceOperationsDashboard, type ManagerAttendanceResult } from '../../api/attendance.ts'
import { getHrLeaveDashboardSummary, type HrLeaveDashboardSummary } from '../../api/leaveDashboard.ts'
import { listMyLeaveBalances, type LeaveBalanceSummary } from '../../api/leaveBalances.ts'
import { listMyLeaveRequests, type LeaveRequestPage } from '../../api/leaveRequests.ts'
import { getMyProfile } from '../../api/myProfile.ts'
import type { MyEmployeeProfile } from '../../api/types.ts'
import { Card } from '../Card.tsx'
import { DashboardErrorState } from './DashboardErrorState.tsx'
import { DashboardSkeleton } from './DashboardSkeleton.tsx'
import { DashboardQuickActions, type DashboardAction } from './DashboardQuickActions.tsx'
import { DashboardWidget } from './DashboardWidget.tsx'
import { useApiQuery, type ApiQueryResult } from '../../hooks/useApiQuery.ts'
import type { ReactNode } from 'react'

function today(): string {
  const date = new Date()
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function WidgetBody<T>({ query, empty, children }: { query: Pick<ApiQueryResult<T>, 'data' | 'error' | 'isLoading' | 'refetch'>; empty: string; children: (data: T) => ReactNode }) {
  if (query.isLoading) return <DashboardSkeleton />
  if (query.error) return <DashboardErrorState error={query.error} onRetry={query.refetch} />
  return query.data ? children(query.data) : <p className="dashboard-muted">{empty}</p>
}

export function HrLeaveSummaryWidget() {
  const query = useApiQuery((signal) => getHrLeaveDashboardSummary(undefined, signal), [])
  return <DashboardWidget title="HR leave operations" className="dashboard-role-widget" ><WidgetBody query={query} empty="No HR leave data is available for the authorized scope.">{(data: HrLeaveDashboardSummary) => <div className="dashboard-summary-grid"><Metric label="On leave today" value={data.kpis.employeesOnLeaveToday} hint={`of ${data.kpis.activeEmployeeCount} active employees`} /><Metric label="Pending approvals" value={data.kpis.pendingApprovalRequests} hint="Authorized scope" /><Metric label="Upcoming approved" value={data.kpis.upcomingApprovedRequests} hint="Next 30 days" /><Metric label="Approved requests" value={data.kpis.approvedRequests} hint="Current period" /></div>}</WidgetBody></DashboardWidget>
}

export function AttendanceOperationsWidget() {
  const date = today()
  const query = useApiQuery(() => getAttendanceOperationsDashboard(date, date), [date])
  return <DashboardWidget title="Attendance operations" className="dashboard-role-widget"><WidgetBody query={query} empty="No attendance operations data is available for today.">{(data: AttendanceOperationsDashboard) => <div className="dashboard-summary-grid"><Metric label="Present" value={data.presentDays} hint="Processed today" /><Metric label="Exceptions" value={data.exceptionDays} hint="Requires attention" /><Metric label="Late arrivals" value={data.lateDays} hint="Today" /><Metric label="Pending corrections" value={data.pendingCorrections} hint="Open workflow items" /></div>}</WidgetBody></DashboardWidget>
}

export function ManagerAttendanceWidget() {
  const date = today()
  const query = useApiQuery(() => getManagerAttendance({ fromDate: date, toDate: date, page: 1, pageSize: 1 }), [date])
  return <DashboardWidget title="Team attendance" className="dashboard-role-widget"><WidgetBody query={query} empty="No team attendance is available for your authorized scope.">{(data: ManagerAttendanceResult) => <div className="dashboard-summary-grid"><Metric label="Present" value={data.summary.present} hint="Today" /><Metric label="Absent" value={data.summary.absent} hint="Today" /><Metric label="On leave" value={data.summary.onLeave} hint="Today" /><Metric label="Exceptions" value={data.summary.incomplete + data.summary.late + data.summary.earlyOut} hint="Incomplete, late or early out" /></div>}</WidgetBody></DashboardWidget>
}

export function SelfServiceWidgets({ showProfile, showBalances, showRequests }: { showProfile: boolean; showBalances: boolean; showRequests: boolean }) {
  const profile = useApiQuery((signal) => showProfile ? getMyProfile(signal) : Promise.resolve(null), [showProfile])
  const balances = useApiQuery((signal) => showBalances ? listMyLeaveBalances(signal) : Promise.resolve([]), [showBalances])
  const requests = useApiQuery(() => showRequests ? listMyLeaveRequests(1, 5) : Promise.resolve<LeaveRequestPage>({ items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }), [showRequests])
  if (!showProfile && !showBalances && !showRequests) return null
  return <div className="dashboard-self-service-grid">
    {showProfile && <DashboardWidget title="My profile" className="dashboard-role-widget"><WidgetBody query={profile} empty="Your linked employee profile is not available.">{(data: MyEmployeeProfile) => <dl className="dashboard-detail-list"><div><dt>Name</dt><dd>{data.fullName}</dd></div><div><dt>Employee code</dt><dd>{data.employeeCode ?? 'Not assigned'}</dd></div><div><dt>Department</dt><dd>{data.currentEmployment?.department ?? 'Not available'}</dd></div><div><dt>Status</dt><dd>{data.status}</dd></div></dl>}</WidgetBody></DashboardWidget>}
    {showBalances && <DashboardWidget title="My leave balances" className="dashboard-role-widget"><WidgetBody query={balances} empty="No leave balances are currently available.">{(data: LeaveBalanceSummary[]) => data.length === 0 ? <p className="dashboard-muted">No balances are currently available.</p> : <ul className="dashboard-compact-list">{data.slice(0, 4).map((item) => <li key={`${item.leaveTypeCode}-${item.leavePeriodName ?? 'current'}`}><span>{item.leaveTypeName}</span><strong>{item.entitlementMode === 'Unlimited' ? 'Unlimited' : `${item.availableQuantity ?? 0} available`}</strong></li>)}</ul>}</WidgetBody></DashboardWidget>}
    {showRequests && <DashboardWidget title="My leave requests" className="dashboard-role-widget"><WidgetBody query={requests} empty="No leave requests yet.">{(data: LeaveRequestPage) => data.items.length === 0 ? <p className="dashboard-muted">No leave requests yet.</p> : <ul className="dashboard-compact-list">{data.items.slice(0, 4).map((item) => <li key={item.requestId}><span>{item.leaveTypeName}<small>{item.startDate} → {item.endDate}</small></span><strong>{item.status}</strong></li>)}</ul>}</WidgetBody></DashboardWidget>}
  </div>
}

export function DashboardActions({ actions }: { actions: readonly DashboardAction[] }) {
  return <DashboardQuickActions actions={actions} />
}

function Metric({ label, value, hint }: { label: string; value: number; hint: string }) { return <div className="dashboard-metric"><span>{label}</span><strong>{value}</strong><small>{hint}</small></div> }

export function IdentityFallback() { return <Card title="Employee link required"><p className="dashboard-muted">Your account is not linked to a valid employee record, so personal HR data is unavailable. Contact HR to review the employee link.</p></Card> }

export function ActionLink({ to, label }: { to: string; label: string }) { return <Link className="dashboard-inline-link" to={to}>{label} →</Link> }
