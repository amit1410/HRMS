import { screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { MyEmployeeProfile } from '../api/types.ts'
import { ok, installStubAdapter, type StubAdapter } from '../test/stubAdapter.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { MyProfilePage } from './MyProfilePage.tsx'

const profile: MyEmployeeProfile = {
  employeeCode: 'ANV_1001', salutation: 'Mr.', firstName: 'Ragu', middleName: null, lastName: 'Kumar Agarwal', fullName: 'Ragu Kumar Agarwal',
  gender: 'Male', dateOfBirth: '1990-01-02', bloodGroup: 'OPositive', maritalStatus: 'Married', citizenship: 'Indian', birthCountry: null, birthState: null, birthCity: null,
  religion: 'Hindu', caste: null, maskedAadhaar: 'XXXX-XXXX-1234', maskedPan: 'A****F', maskedUan: '******5678', maskedPf: '******4321', maskedEsic: null, mediclaimNumber: null,
  esicApplicable: false, gratuity: true, pension: true, dateOfJoining: '2020-01-02', groupDateOfJoining: null, employeeType: 'Permanent', jobStatus: 'Active', status: 'Active', groupId: null, payrollLocation: null, costCenterCode: null, profilePictureUrl: null,
  contact: { email: 'ragu@example.test', phone: '+91 9000000000' },
  currentAddress: { country: 'India', state: 'Karnataka', district: 'Bengaluru', city: 'Bengaluru', zipCode: '560001', addressLine1: '1 Current Road', addressLine2: null, houseNumber: null },
  permanentAddress: { country: 'India', state: 'Kerala', district: 'Ernakulam', city: 'Kochi', zipCode: '682001', addressLine1: '2 Permanent Road', addressLine2: null, houseNumber: null },
  currentEmployment: { holdingCompany: null, lob: 'Technology', organization: 'SGS', department: 'Engineering', subDepartment: null, section: null, subSection: null, function: null, subFunction: null, grade: 'G5', designation: 'Engineer', employeeType: 'Permanent', country: 'India', workLocation: 'Bengaluru', costCenter: null, effectiveFrom: '2020-01-02', reportingManager: 'Manager Name', employmentType: 'FullTime', employmentStatus: 'Active' },
  bankDetails: [{ bankName: 'State Bank', maskedAccountNumber: '********1234', maskedIfsc: 'SBIN*****34', branch: 'Main Branch', accountType: 'Salary', effectiveFrom: '2020-01-02' }],
}

describe('MyProfilePage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    stub = installStubAdapter()
    stub.on('get', '/api/me/profile', () => ({ data: ok(profile) }))
  })

  afterEach(() => stub.restore())

  it('renders the hero, read-only cards, tabs, and masked values', async () => {
    renderAsUser(<MyProfilePage />)

    expect(await screen.findByRole('heading', { name: 'Ragu Kumar Agarwal' })).toBeInTheDocument()
    expect(screen.getAllByText('ANV_1001')).toHaveLength(2)
    expect(document.querySelector('.profile-hero .profile-status')).toHaveTextContent('Active')
    expect(screen.getByRole('heading', { name: 'Personal Details' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Contact & Address' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Employment Details' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Bank Details' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Overview/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('XXXX-XXXX-1234')).toBeInTheDocument()
    expect(screen.getByText('********1234')).toBeInTheDocument()
    expect(screen.queryByText('123456789012')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
    expect(document.querySelector('.my-profile-grid')).toBeInTheDocument()
    expect(document.querySelector('.profile-bank-table')).toBeInTheDocument()
  })

  it('omits missing optional sections without rendering null or undefined', async () => {
    stub.on('get', '/api/me/profile', () => ({ data: ok({ ...profile, currentAddress: null, permanentAddress: null, currentEmployment: null, bankDetails: [], contact: { email: null, phone: null } }) }))
    renderAsUser(<MyProfilePage />)

    expect(await screen.findByRole('heading', { name: 'Ragu Kumar Agarwal' })).toBeInTheDocument()
    expect(screen.getAllByText('No address recorded.')).toHaveLength(2)
    expect(screen.getByText('No current employment position is available.')).toBeInTheDocument()
    expect(screen.getByText('No active bank details are available.')).toBeInTheDocument()
    expect(screen.queryByText('undefined')).not.toBeInTheDocument()
    expect(screen.queryByText('null')).not.toBeInTheDocument()
  })
})
