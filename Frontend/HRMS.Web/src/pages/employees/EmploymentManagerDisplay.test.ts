import { describe, expect, it } from 'vitest'
import { managerDisplay } from './EmploymentSectionForm.tsx'
import type { EmployeeEmploymentHistory } from '../../api/types.ts'

const record = (overrides: Partial<EmployeeEmploymentHistory> = {}): EmployeeEmploymentHistory => ({
  id: 'history-1',
  employeeId: 'employee-1',
  effectiveFrom: '2026-09-15',
  employmentType: 'FullTime',
  employmentStatus: 'Active',
  changeReason: 'ManagerChange',
  createdDate: '2026-09-15T00:00:00Z',
  managerId: 'df862957-cb44-4000-bbb7-4bc5dc441070',
  managerEmployeeCode: 'ANV_1004',
  managerFullName: 'Manoj Kumar',
  ...overrides,
})

describe('managerDisplay', () => {
  it('renders the manager code and name, never the internal GUID', () => {
    expect(managerDisplay(record())).toBe('ANV_1004 — Manoj Kumar')
    expect(managerDisplay(record())).not.toContain('df862957-cb44-4000-bbb7-4bc5dc441070')
  })

  it('renders a dash when manager display data is absent', () => {
    expect(managerDisplay(record({ managerId: 'df862957-cb44-4000-bbb7-4bc5dc441070', managerEmployeeCode: null, managerFullName: null }))).toBe('—')
  })
})
