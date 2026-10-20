import { describe, expect, it } from 'vitest'
import { navigationSearchResults } from './navigationSearch.ts'
import type { NavModule } from './navigation.ts'

const modules: NavModule[] = [
  { id: 'people', label: 'People', items: [{ label: 'Employees', to: '/employees', available: true }] },
  { id: 'attendance', label: 'Attendance', items: [
    { label: 'Leave Approvals', to: '/leave-management/approvals', available: true },
    { label: 'Restricted Payroll', to: '/payroll/runs', available: false },
  ] },
]

describe('navigationSearchResults', () => {
  it('matches labels, modules, routes, aliases, and partial terms deterministically', () => {
    expect(navigationSearchResults(modules, 'leave approval').map(result => result.item.label)).toEqual(['Leave Approvals'])
    expect(navigationSearchResults(modules, 'employee')[0]?.id).toBe('people:/employees')
    expect(navigationSearchResults(modules, 'payroll')).toEqual([])
  })

  it('keeps result IDs stable and excludes unavailable navigation entries', () => {
    expect(navigationSearchResults(modules, '').map(result => result.id)).toEqual([
      'attendance:/leave-management/approvals',
      'people:/employees',
    ])
  })
})
