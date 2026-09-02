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
import { EmployeeProfileSidebar, type EmployeeSection } from './EmployeeProfileSidebar.tsx'
import { FamilyDetailsForm } from './FamilyDetailsForm.tsx'
import { PreviousEmploymentForm } from './PreviousEmploymentForm.tsx'

type DetailTab = EmployeeSection

const TABS: { id: DetailTab; label: string }[] = [
  { id: 'personal', label: 'Personal Details' },
  { id: 'contact', label: 'Contact Details' },
  { id: 'address', label: 'Address Details' },
  { id: 'family', label: 'Family Details' },
  { id: 'bank', label: 'Bank Details' },
  { id: 'previousEmployment', label: 'Previous Employment' },
  { id: 'employment', label: 'Employment' },
]

const TAB_ICONS: Record<DetailTab, string> = { personal: '◉', contact: '✉', address: '⌂', family: '♧', bank: '▣', previousEmployment: '◫', employment: '▤' }

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
      <PageHeader
        title={employee.fullName}
        subtitle={
          <>
            {employee.employeeCode} &middot; Employee Details
            <StatusBadge status={employee.status} />
          </>
        }
      />

      <div className="tab-nav employee-top-tabs" role="tablist" aria-label="Employee sections">
        {TABS.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            id={`tab-${item.id}`}
            className={`tab-nav-item${tab === item.id ? ' is-active' : ''}`}
            aria-selected={tab === item.id}
            aria-controls={`panel-${item.id}`}
            onClick={() => setTab(item.id)}
          >
            <span className="employee-tab-icon" aria-hidden="true">{TAB_ICONS[item.id]}</span>{item.label}
          </button>
        ))}
      </div>

      <div className="employee-details-shell">
        <EmployeeProfileSidebar employee={employee} activeSection={tab} onSectionChange={setTab} />
        <main className="employee-section-content" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
          {tab === 'personal' && <PersonalDetailsForm employeeId={employee.id} />}
          {tab === 'contact' && <ContactDetailsForm employeeId={employee.id} />}
        {tab === 'address' && <AddressDetailsForm employeeId={employee.id} />}
        {tab === 'family' && <FamilyDetailsForm employeeId={employee.id} />}
        {tab === 'bank' && <BankDetailsForm employeeId={employee.id} />}
        {tab === 'previousEmployment' && <PreviousEmploymentForm employeeId={employee.id} />}
          {tab === 'employment' && <EmploymentSectionForm employeeId={employee.id} />}
        </main>
      </div>
    </>
  )
}
