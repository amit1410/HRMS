import { useMemo, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth.ts'
import { PageHeader } from '../components/PageHeader.tsx'
import { visibleConfigurationDestinations, type ConfigurationDestination } from '../layout/navigation.ts'

const categoryOrder = [
  'Employee & Onboarding',
  'Employee Masters',
  'Leave Policies',
  'Attendance & Work Schedules',
  'Payroll Configuration',
  'Confirmation & Separation Policies',
  'Security & Access Management',
] as const

function ConfigurationIcon({ kind }: { kind: ConfigurationDestination['icon'] }) {
  const paths: Record<ConfigurationDestination['icon'], string> = {
    employee: 'M16 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-3A3.5 3.5 0 0 0 6 18.5V20M11 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm5-5h3m-1.5-1.5V7',
    master: 'M4 19.5h16M6 19.5V8l6-3 6 3v11.5M9 19.5v-5h6v5M8 10h.01M12 10h.01M16 10h.01',
    leave: 'M7 3v3m10-3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm3 8 2 2 4-4',
    attendance: 'M4 7h16M7 4v6m10-6v6M6 20h12a2 2 0 0 0 2-2V7H4v11a2 2 0 0 0 2 2Zm3-8h2m2 0h2m-6 4h2m2 0h2',
    payroll: 'M4 7h16v10H4zM7 11h4m-4 3h7M7 4h10a2 2 0 0 1 2 2v1H5V6a2 2 0 0 1 2-2Z',
    policy: 'M6 3h9l3 3v15H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Zm9 0v4h3M8 11h8M8 15h6',
    security: 'M12 3 19 6v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3Zm-3 9 2 2 4-4',
  }
  return <svg className={`configuration-card-icon is-${kind}`} viewBox="0 0 24 24" aria-hidden="true"><path d={paths[kind]} /></svg>
}

export function ConfigurationCenterPage() {
  const { can } = useAuth()
  const [search, setSearch] = useState('')
  const destinations = visibleConfigurationDestinations(can)
  const normalizedSearch = search.trim().toLowerCase()
  const filtered = useMemo(() => destinations.filter(item =>
    !normalizedSearch || item.label.toLowerCase().includes(normalizedSearch) || item.category.toLowerCase().includes(normalizedSearch),
  ), [destinations, normalizedSearch])
  const sections = categoryOrder.map(category => ({ category, items: filtered.filter(item => item.category === category) })).filter(section => section.items.length > 0)

  if (destinations.length === 0) return <Navigate to="/forbidden" replace />

  return <div className="configuration-center-page">
    <PageHeader title="Configuration Center" subtitle="Manage the policies, setup, and reference data that shape your organization’s HRMS experience." />
    <div className="configuration-center-toolbar">
      <label className="configuration-search">
        <span className="sr-only">Search configuration destinations</span>
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4.5 4.5" /></svg>
        <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search configuration" />
      </label>
      <span className="configuration-count" aria-live="polite">{filtered.length} {filtered.length === 1 ? 'destination' : 'destinations'}</span>
    </div>
    {sections.length > 0 ? sections.map(section => <section className="configuration-section" key={section.category} aria-labelledby={`configuration-${section.category}`}>
      <div className="configuration-section-heading"><h2 id={`configuration-${section.category}`}>{section.category}</h2><span>{section.items.length}</span></div>
      <div className="configuration-card-grid">{section.items.map(item => <Link className="configuration-card" key={item.to} to={item.to}>
        <span className="configuration-card-icon-wrap"><ConfigurationIcon kind={item.icon} /></span>
        <span className="configuration-card-copy"><strong>{item.label}</strong><span>{item.description}</span></span>
        <svg className="configuration-card-chevron" viewBox="0 0 24 24" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
      </Link>)}</div>
    </section>) : <div className="configuration-empty" role="status"><strong>No configuration destinations found</strong><span>Try a different search term.</span></div>}
  </div>
}
