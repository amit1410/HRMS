import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { getEmployee } from '../../api/employees.ts'
import { Card } from '../../components/Card.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { StatusBadge } from '../../components/Badge.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { PersonalDetailsForm } from './PersonalDetailsForm.tsx'
import { ContactDetailsForm } from './ContactDetailsForm.tsx'
import { AddressDetailsForm } from './AddressDetailsForm.tsx'
import { BankDetailsForm } from './BankDetailsForm.tsx'
import { EmploymentSectionForm } from './EmploymentSectionForm.tsx'
import type { Employee } from '../../api/types.ts'
import { FamilyDetailsForm } from './FamilyDetailsForm.tsx'
import { PreviousEmploymentForm } from './PreviousEmploymentForm.tsx'
import { SupervisorSectionForm } from './SupervisorSectionForm.tsx'

type DetailTab = 'personal' | 'contact' | 'address' | 'family' | 'bank' | 'previousEmployment' | 'employment' | 'supervisor'

const TABS: { id: DetailTab; label: string }[] = [
  { id: 'personal', label: 'Personal Details' },
  { id: 'contact', label: 'Contact Details' },
  { id: 'address', label: 'Address Details' },
  { id: 'family', label: 'Family Details' },
  { id: 'bank', label: 'Bank Details' },
  { id: 'previousEmployment', label: 'Previous Employment' },
  { id: 'employment', label: 'Employment' },
  { id: 'supervisor', label: 'Supervisors' },
]

const TAB_ICONS: Record<DetailTab, string> = { personal: '◉', contact: '✉', address: '⌂', family: '♧', bank: '▣', previousEmployment: '◫', employment: '▤', supervisor: '↕' }

/**
 * An employee's record, split into tabbed sections: Personal Details, Contact Details and Address Details.
 *
 * Each tab is its own self-managing form (existing values populated, ready to update) with its own
 * Save/Update button. The Contact Details tab owns the phone numbers and emails; the Address Details tab
 * owns the current and permanent address blocks and the "Permanent Address same as Current Address"
 * checkbox, keeping the permanent block in step with the current one while it is set. Other employee
 * sections (family, employment, pay …) are separate concerns captured on their own screens and are
 * intentionally not surfaced here.
 */
export function EmployeeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [tab, setTab] = useState<DetailTab>('personal')
  const goNext = (current: DetailTab) => {
    const index = TABS.findIndex((item) => item.id === current)
    const next = TABS[index + 1]
    if (next) setTab(next.id)
  }

  const { data: employee, error, isLoading, refetch } = useApiQuery(
    (signal) => getEmployee(id!, signal),
    [id],
  )

  useDocumentTitle(employee?.fullName ?? 'Employee')

  if (error) {
    return (
      <>
        <PageHeader title="Employee" />
        <Card>
          <ErrorState error={error} onRetry={refetch} />
        </Card>
      </>
    )
  }

  if (isLoading || !employee) {
    return (
      <>
        <PageHeader title="Employee" />
        <Card>
          <div className="table-loading">
            <Spinner label="Loading employee..." />
          </div>
        </Card>
      </>
    )
  }

  return (
    <>
      <PageHeader title="Employee details" subtitle="Review and maintain employee information" />

      <EmployeeHeader employee={employee} />


      <EmployeeSectionNavigation tab={tab} setTab={setTab} />

      <div className="employee-details-shell">
        <main className="employee-section-content" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
          {tab === 'personal' && <PersonalDetailsForm employeeId={employee.id} onSaved={() => goNext('personal')} />}
          {tab === 'contact' && <ContactDetailsForm employeeId={employee.id} onSaved={() => goNext('contact')} />}
        {tab === 'address' && <AddressDetailsForm employeeId={employee.id} onSaved={() => goNext('address')} />}
        {tab === 'family' && <FamilyDetailsForm employeeId={employee.id} onSaved={() => goNext('family')} />}
        {tab === 'bank' && <BankDetailsForm employeeId={employee.id} onSaved={() => goNext('bank')} />}
        {tab === 'previousEmployment' && <PreviousEmploymentForm employeeId={employee.id} onSaved={() => goNext('previousEmployment')} />}
          {tab === 'employment' && <EmploymentSectionForm employeeId={employee.id} employeeCode={employee.employeeCode} onSaved={async () => { await refetch(); goNext('employment') }} />}
          {tab === 'supervisor' && <SupervisorSectionForm employeeId={employee.id} onEmploymentChange={() => setTab('employment')} />}
        </main>
      </div>
    </>
  )
}

function EmployeeHeader({ employee }: { employee: Employee }) {
  const initials = employee.fullName.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase()
  return <section className="employee-compact-header" aria-label="Employee summary">
    {employee.profilePictureUrl ? <img className="employee-avatar" src={employee.profilePictureUrl} alt="" /> : <div className="employee-avatar employee-avatar-initials" aria-hidden="true">{initials}</div>}
    <div className="employee-compact-identity">
      <div className="employee-compact-name-row"><h2>{employee.fullName}</h2><StatusBadge status={employee.status} /></div>
      <p className="employee-profile-code">{employee.employeeCode || 'Employee code pending'}</p>
    </div>
    <dl className="employee-compact-facts">
      <div><dt>Designation</dt><dd>{employee.designationName || '—'}</dd></div>
      <div><dt>Department</dt><dd>{employee.departmentName || '—'}</dd></div>
      <div><dt>Location</dt><dd>{employee.payrollLocation || '—'}</dd></div>
    </dl>
  </section>
}

function EmployeeSectionNavigation({ tab, setTab }: { tab: DetailTab; setTab: (next: DetailTab) => void }) {
  return <nav className="employee-section-navigation" aria-label="Employee sections">
    <div className="tab-nav employee-desktop-tabs" role="tablist">
      {TABS.map((item) => <button key={item.id} type="button" role="tab" id={`tab-${item.id}`} className={`tab-nav-item${tab === item.id ? ' is-active' : ''}`} aria-selected={tab === item.id} aria-controls={`panel-${item.id}`} onClick={() => setTab(item.id)}><span className="employee-tab-icon" aria-hidden="true">{TAB_ICONS[item.id]}</span>{item.label}</button>)}
    </div>
    <label className="employee-mobile-tabs">Go to section<select value={tab} onChange={(event) => setTab(event.target.value as DetailTab)} aria-label="Employee section">
      {TABS.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
    </select></label>
  </nav>
}
