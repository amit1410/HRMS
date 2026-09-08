import { useState, type ReactNode } from 'react'
import { Card } from '../components/Card.tsx'
import { Spinner } from '../components/Spinner.tsx'
import { useApiQuery } from '../hooks/useApiQuery.ts'
import { getMyProfile } from '../api/myProfile.ts'
import type { MyEmployeeAddress, MyEmployeeBank, MyEmployeeProfile } from '../api/types.ts'
import { formatDate, initials } from '../lib/format.ts'

type ProfileTab = 'overview' | 'personal' | 'contact' | 'employment' | 'bank'

const tabs: { id: ProfileTab; label: string; icon: IconName }[] = [
  { id: 'overview', label: 'Overview', icon: 'grid' },
  { id: 'personal', label: 'Personal', icon: 'user' },
  { id: 'contact', label: 'Contact & Address', icon: 'pin' },
  { id: 'employment', label: 'Employment', icon: 'briefcase' },
  { id: 'bank', label: 'Bank Details', icon: 'bank' },
]

export function MyProfilePage() {
  const query = useApiQuery(getMyProfile, [])
  const [activeTab, setActiveTab] = useState<ProfileTab>('overview')

  if (query.isLoading && !query.data) return <div className="state-block"><Spinner label="Loading your profile..." /></div>
  if (query.error) return <div className="state-block"><h1>My Profile</h1><p className="state-error">{query.error.status === 404 ? 'Profile not linked' : query.error.message}</p><p className="muted">{query.error.status === 404 ? 'Your account is not linked to an employee record. Please contact HR.' : 'Your profile could not be loaded.'}</p></div>
  if (!query.data) return null

  const profile = query.data
  const scrollTo = (tab: ProfileTab) => {
    setActiveTab(tab)
    document.getElementById(`profile-${tab}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  return <main className="my-profile-page">
    <div id="profile-overview" className="profile-section-anchor"><ProfileHero profile={profile} /></div>
    <nav className="profile-tabs" aria-label="Profile sections" role="tablist">
      {tabs.map(tab => <button key={tab.id} type="button" role="tab" aria-selected={activeTab === tab.id} className={activeTab === tab.id ? 'profile-tab is-active' : 'profile-tab'} onClick={() => scrollTo(tab.id)}>
        <Icon name={tab.icon} />{tab.label}
      </button>)}
    </nav>

    <div className="my-profile-grid">
      <div id="profile-personal" className="profile-section-anchor">
        <ProfileCard title="Personal Details" icon="user"><PersonalFields profile={profile} /></ProfileCard>
      </div>
      <div id="profile-contact" className="profile-section-anchor">
        <ProfileCard title="Contact & Address" icon="pin"><ContactAndAddress profile={profile} /></ProfileCard>
      </div>
      <div id="profile-employment" className="profile-section-anchor">
        <ProfileCard title="Employment Details" icon="briefcase"><EmploymentFields profile={profile} /></ProfileCard>
      </div>
      <div id="profile-bank" className="profile-section-anchor">
        <ProfileCard title="Bank Details" icon="bank"><BankDetails banks={profile.bankDetails} /></ProfileCard>
      </div>
    </div>
  </main>
}

function ProfileHero({ profile }: { profile: MyEmployeeProfile }) {
  const employment = profile.currentEmployment
  return <section className="profile-hero" aria-labelledby="my-profile-title">
    <div className="profile-hero-art" aria-hidden="true"><span /><span /><span /></div>
    <div className="profile-hero-content">
      <div className="profile-hero-identity">
        <div className="profile-hero-avatar" aria-hidden="true">{initials(profile.fullName)}</div>
        <div className="profile-hero-copy">
          <p className="profile-hero-kicker">Employee profile</p>
          <h1 id="my-profile-title">{profile.fullName}</h1>
          <div className="profile-hero-code-row">
            {profile.employeeCode && <span>{profile.employeeCode}</span>}
            <span className={isActive(profile.status) ? 'profile-status is-active' : 'profile-status'}>{profile.status}</span>
          </div>
          <div className="profile-hero-details">
            {employment?.designation && <span>{employment.designation}</span>}
            {employment?.organization && <span>{employment.organization}</span>}
            {employment?.department && <span>{employment.department}</span>}
          </div>
        </div>
      </div>
    </div>
  </section>
}

function ProfileCard({ title, icon, children }: { title: string; icon: IconName; children: ReactNode }) {
  return <Card title={<span className="profile-card-heading"><Icon name={icon} /><span>{title}</span></span>} className="my-profile-card">{children}</Card>
}

function PersonalFields({ profile }: { profile: MyEmployeeProfile }) {
  return <Fields items={[
    ['Employee Code', profile.employeeCode], ['Full Name', profile.fullName], ['Salutation', profile.salutation],
    ['Date of Birth', formatDate(profile.dateOfBirth)], ['Gender', profile.gender], ['Blood Group', profile.bloodGroup],
    ['Marital Status', profile.maritalStatus], ['Citizenship', profile.citizenship], ['Aadhaar', profile.maskedAadhaar],
    ['PAN', profile.maskedPan], ['UAN', profile.maskedUan], ['PF Number', profile.maskedPf],
    ['ESIC', profile.esicApplicable ? (profile.maskedEsic ?? 'Applicable') : 'Not applicable'],
    ['Religion', profile.religion], ['Gratuity', yesNo(profile.gratuity)], ['Pension', yesNo(profile.pension)],
  ]} />
}

function ContactAndAddress({ profile }: { profile: MyEmployeeProfile }) {
  return <div className="profile-contact-content">
    <div className="profile-contact-row">
      {profile.contact.email && <ContactItem icon="mail" label="Email" value={profile.contact.email} />}
      {profile.contact.phone && <ContactItem icon="phone" label="Phone / Mobile" value={profile.contact.phone} />}
    </div>
    <Address title="Current Address" value={profile.currentAddress} />
    <Address title="Permanent Address" value={profile.permanentAddress} />
  </div>
}

function ContactItem({ icon, label, value }: { icon: IconName; label: string; value: string }) {
  return <div className="profile-contact-item"><span className="profile-contact-icon"><Icon name={icon} /></span><span><small>{label}</small><strong>{value}</strong></span></div>
}

function Address({ title, value }: { title: string; value?: MyEmployeeAddress | null }) {
  const items = value ? Object.entries(value).map(([key, item]) => [addressLabel(key), item] as [string, unknown]) : []
  return <div className="profile-address"><h3><Icon name="pin" />{title}</h3>{items.length ? <Fields items={items} /> : <p className="muted">No address recorded.</p>}</div>
}

function EmploymentFields({ profile }: { profile: MyEmployeeProfile }) {
  const employment = profile.currentEmployment
  if (!employment) return <p className="muted">No current employment position is available.</p>
  return <Fields items={[
    ['Holding Company', employment.holdingCompany], ['LOB', employment.lob], ['Organization', employment.organization],
    ['Department', employment.department], ['Sub Department', employment.subDepartment], ['Section', employment.section],
    ['Sub Section', employment.subSection], ['Function', employment.function], ['Sub Function', employment.subFunction],
    ['Designation', employment.designation], ['Grade', employment.grade], ['Employee Type', employment.employeeType],
    ['Job Status', profile.jobStatus], ['Employment Type', employment.employmentType], ['Employment Status', employment.employmentStatus],
    ['Country', employment.country], ['Work Location', employment.workLocation], ['Cost Center', employment.costCenter],
    ['Effective From', formatDate(employment.effectiveFrom)], ['Date of Joining', formatDate(profile.dateOfJoining)],
    ['Group Date of Joining', formatDate(profile.groupDateOfJoining)], ['Reporting Manager', employment.reportingManager],
  ]} />
}

function BankDetails({ banks }: { banks: MyEmployeeBank[] }) {
  if (!banks.length) return <p className="muted">No active bank details are available.</p>
  return <div className="profile-bank-content">
    <div className="profile-bank-table-wrap"><table className="profile-bank-table"><caption className="sr-only">Active bank details</caption><thead><tr><th>Bank Name</th><th>Account Number</th><th>IFSC</th><th>Type</th><th>Status</th></tr></thead><tbody>{banks.map((bank, index) => <tr key={`${bank.bankName}-${bank.maskedAccountNumber}-${index}`}><td data-label="Bank Name"><strong>{bank.bankName || 'Bank'}</strong>{bank.branch && <small>{bank.branch}</small>}</td><td data-label="Account Number">{bank.maskedAccountNumber}</td><td data-label="IFSC">{bank.maskedIfsc || '-'}</td><td data-label="Type">{bank.accountType}</td><td data-label="Status"><span className="profile-status is-active">Active</span></td></tr>)}</tbody></table></div>
    <p className="profile-security-note"><Icon name="lock" />Sensitive information such as account number and IFSC is partially masked for your security.</p>
  </div>
}

function Fields({ items }: { items: [string, unknown][] }) {
  return <dl className="profile-fields">{items.filter(([, value]) => value !== null && value !== undefined && value !== '').map(([name, value]) => <div key={name}><dt>{name}</dt><dd>{String(value)}</dd></div>)}</dl>
}

function isActive(value: string) { return value.toLowerCase() === 'active' }
function yesNo(value: boolean) { return value ? 'Yes' : 'No' }
function addressLabel(value: string) { return ({ addressLine1: 'Address Line 1', addressLine2: 'Address Line 2', zipCode: 'Zip Code' } as Record<string, string>)[value] ?? value.replace(/[A-Z]/g, c => ` ${c}`).replace(/^./, c => c.toUpperCase()) }

type IconName = 'bank' | 'briefcase' | 'grid' | 'lock' | 'mail' | 'phone' | 'pin' | 'user'
function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, string> = {
    bank: 'M3 10h18M5 10v8m4-8v8m6-8v8m4-8v8M3 20h18M12 3l9 5H3l9-5Z',
    briefcase: 'M9 7V5.8A1.8 1.8 0 0 1 10.8 4h2.4A1.8 1.8 0 0 1 15 5.8V7M4 7h16a1 1 0 0 1 1 1v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a1 1 0 0 1 1-1Zm-1 5h18',
    grid: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
    lock: 'M6 10V7a6 6 0 0 1 12 0v3m-14 0h16v10H4V10Z',
    mail: 'M4 6h16v12H4zM4 7l8 6 8-6',
    phone: 'M7 4h3l1 4-2 1.5a14 14 0 0 0 5.5 5.5L16 13l4 1v3a2 2 0 0 1-2 2C10.3 19 5 13.7 5 6a2 2 0 0 1 2-2Z',
    pin: 'M12 21s7-6.2 7-12A7 7 0 0 0 5 9c0 5.8 7 12 7 12Zm0-9a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z',
    user: 'M20 21a8 8 0 0 0-16 0M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z',
  }
  return <svg className="profile-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={paths[name]} /></svg>
}
