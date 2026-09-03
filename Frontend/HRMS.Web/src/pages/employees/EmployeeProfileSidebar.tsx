import { getEmploymentHistory } from '../../api/employeeSubsections.ts'
import type { Employee, EmployeeEmploymentHistory } from '../../api/types.ts'
import { Badge, StatusBadge } from '../../components/Badge.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

export type EmployeeSection = 'personal' | 'contact' | 'address' | 'family' | 'bank' | 'previousEmployment' | 'employment'

const SECTIONS: { id: EmployeeSection; label: string; icon: string }[] = [
  { id: 'personal', label: 'Personal Details', icon: '◉' },
  { id: 'contact', label: 'Contact Details', icon: '✉' },
  { id: 'address', label: 'Address Details', icon: '⌂' },
  { id: 'family', label: 'Family Details', icon: '♧' },
  { id: 'bank', label: 'Bank Details', icon: '▣' },
  { id: 'previousEmployment', label: 'Previous Employment', icon: '◫' },
  { id: 'employment', label: 'Employment Details', icon: '▤' },
]

export function EmployeeProfileSidebar({ employee, activeSection, onSectionChange }: {
  employee: Employee | null
  activeSection: EmployeeSection
  onSectionChange: (section: EmployeeSection) => void
}) {
  const history = useApiQuery((signal) => employee ? getEmploymentHistory(employee.id, signal) : Promise.resolve([]), [employee?.id])
  const current = history.data?.find((item: EmployeeEmploymentHistory) => isEffectiveToday(item))

  return <aside className="employee-profile-sidebar" aria-label="Employee profile">
    <div className="employee-profile-summary">
      {employee?.profilePictureUrl ? <img className="employee-avatar" src={employee.profilePictureUrl} alt="" /> : <div className="employee-avatar employee-avatar-initials" aria-hidden="true">{employee ? initials(employee.fullName) : 'NE'}</div>}
      {employee ? current ? <StatusBadge status={employee.status} /> : history.data?.some(isScheduled) ? <Badge tone="info">Scheduled</Badge> : <StatusBadge status={employee.status} /> : <span className="employee-draft-badge">DRAFT</span>}
      <h2>{employee?.fullName ?? 'New Employee'}</h2>
      <p className="employee-profile-code">{employee?.employeeCode ?? 'Employee Code: Pending'}</p>
      <p className="employee-profile-role">{current?.designationName ?? employee?.designationName ?? '—'}</p>
      <p className="employee-profile-department">{current?.departmentName ?? employee?.departmentName ?? '—'}</p>
    </div>
    <nav className="employee-profile-nav" aria-label="Employee sections">
      {SECTIONS.map((section) => <button key={section.id} type="button" className={activeSection === section.id ? 'is-active' : ''} onClick={() => onSectionChange(section.id)} disabled={!employee && section.id !== 'personal'} aria-current={activeSection === section.id ? 'page' : undefined}>
        <span aria-hidden="true">{section.icon}</span>{section.label}
      </button>)}
    </nav>
  </aside>
}

function initials(name: string): string { return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase() }

function isEffectiveToday(record: EmployeeEmploymentHistory): boolean {
  const today = new Date()
  const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  return record.effectiveFrom <= date && (record.effectiveTo == null || record.effectiveTo >= date)
}

function isScheduled(record: EmployeeEmploymentHistory): boolean {
  const today = new Date()
  const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  return record.effectiveFrom > date
}
