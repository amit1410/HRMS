import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { getEmployee } from '../../api/employees.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { AddressDetailsForm } from './AddressDetailsForm.tsx'
import { BankDetailsForm } from './BankDetailsForm.tsx'
import { ContactDetailsForm } from './ContactDetailsForm.tsx'
import { EmploymentSectionForm } from './EmploymentSectionForm.tsx'
import { PersonalDetailsForm } from './PersonalDetailsForm.tsx'
import { EmployeeProfileSidebar, type EmployeeSection } from './EmployeeProfileSidebar.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { FamilyDetailsForm } from './FamilyDetailsForm.tsx'
import { PreviousEmploymentForm } from './PreviousEmploymentForm.tsx'

type FormTab = EmployeeSection

const TABS: { id: FormTab; label: string }[] = [
  { id: 'personal', label: 'Personal Details' },
  { id: 'contact', label: 'Contact Details' },
  { id: 'address', label: 'Address Details' },
  { id: 'family', label: 'Family Details' },
  { id: 'bank', label: 'Bank Details' },
  { id: 'previousEmployment', label: 'Previous Employment' },
  { id: 'employment', label: 'Employment' },
]
const TAB_ICONS: Record<FormTab, string> = { personal:'◉', contact:'✉', address:'⌂', family:'♧', bank:'▣', previousEmployment:'◫', employment:'▤' }

/**
 * Create and edit an employee, split into tabbed sections just like the detail page.
 *
 * One reusable component handles both paths. `/employees/new` renders it in create mode ("New Hire",
 * SAVE button) and `/employees/:id/edit` in edit mode (loaded record, UPDATE button). After a create the
 * Personal Details form flips to edit mode on the page, replacing "New Hire" with the generated code.
 *
 * The Contact and Address sub-resources live behind their own endpoints that need an existing employee, so
 * on the create path those tabs stay inert (with a short explanation) until the employee row is created via
 * the Personal Details SAVE button; on the edit path they are always available. There is deliberately no
 * department, designation or reporting manager here — those belong to later screens.
 */
export function EmployeeFormPage() {
  const { id } = useParams()

  if (id !== undefined) {
    return <EditPage key={id} id={id} />
  }
  return <NewPage />
}

function TabNav({ tab, setTab }: { tab: FormTab; setTab: (next: FormTab) => void }) {
  return (
    <div className="tab-nav" role="tablist" aria-label="Employee sections">
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
  )
}

function NewPage() {
  useDocumentTitle('New employee')
  const [tab, setTab] = useState<FormTab>('personal')
  const [createdId, setCreatedId] = useState<string | undefined>(undefined)
  const employeeId = createdId
  const employee = useApiQuery((signal) => employeeId ? getEmployee(employeeId, signal) : Promise.resolve(null), [employeeId])

  return (
    <>
      <PageHeader title="New employee" subtitle="Add someone to the directory" />
      <TabNav tab={tab} setTab={setTab} />
      <div className="employee-details-shell">
        <EmployeeProfileSidebar employee={employee.data} activeSection={tab as EmployeeSection} onSectionChange={setTab} />
        <main className="employee-section-content" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
        {tab === 'personal' && <PersonalDetailsForm onCreated={setCreatedId} />}
        {tab === 'contact' &&
          (employeeId ? (
            <ContactDetailsForm employeeId={employeeId} />
          ) : (
            <NeedsEmployeeNotice />
          ))}
        {tab === 'address' &&
          (employeeId ? (
            <AddressDetailsForm employeeId={employeeId} />
          ) : (
            <NeedsEmployeeNotice />
          ))}
        {tab === 'family' && (employeeId ? <FamilyDetailsForm employeeId={employeeId} /> : <NeedsEmployeeNotice />)}
        {tab === 'bank' &&
          (employeeId ? (
            <BankDetailsForm employeeId={employeeId} />
          ) : (
            <NeedsEmployeeNotice />
          ))}
        {tab === 'previousEmployment' && (employeeId ? <PreviousEmploymentForm employeeId={employeeId} /> : <NeedsEmployeeNotice />)}
        {tab === 'employment' &&
          (employeeId ? (
            <EmploymentSectionForm employeeId={employeeId} />
          ) : (
            <NeedsEmployeeNotice />
          ))}
        </main>
      </div>
    </>
  )
}

function EditPage({ id }: { id: string }) {
  useDocumentTitle('Edit employee')
  const [tab, setTab] = useState<FormTab>('personal')
  const employee = useApiQuery((signal) => getEmployee(id, signal), [id])

  return (
    <>
      <PageHeader title="Edit employee" subtitle="Update their personal details" />
      <TabNav tab={tab} setTab={setTab} />
      <div className="employee-details-shell">
        <EmployeeProfileSidebar employee={employee.data} activeSection={tab as EmployeeSection} onSectionChange={setTab} />
        <main className="employee-section-content" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
        {tab === 'personal' && <PersonalDetailsForm employeeId={id} />}
        {tab === 'contact' && <ContactDetailsForm employeeId={id} />}
        {tab === 'address' && <AddressDetailsForm employeeId={id} />}
        {tab === 'family' && <FamilyDetailsForm employeeId={id} />}
        {tab === 'bank' && <BankDetailsForm employeeId={id} />}
        {tab === 'previousEmployment' && <PreviousEmploymentForm employeeId={id} />}
        {tab === 'employment' && <EmploymentSectionForm employeeId={id} />}
        </main>
      </div>
    </>
  )
}

function NeedsEmployeeNotice() {
  return (
    <Card className="form-card">
      <Notice tone="info">
        Save the Personal Details section first to create the employee, then fill in the remaining details
        here.
      </Notice>
    </Card>
  )
}
