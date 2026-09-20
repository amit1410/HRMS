import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollAccountingConfigurationPage } from './PayrollAccountingConfigurationPage.tsx'

vi.mock('../../api/payroll.ts', () => ({ listPayrollGLAccounts: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }), listPayrollAccountingConfigurations: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }), createPayrollGLAccount: vi.fn(), createPayrollAccountingConfiguration: vi.fn() }))

describe('PayrollAccountingConfigurationPage', () => {
  it('renders account and configuration management', async () => { render(<PayrollAccountingConfigurationPage />); expect(await screen.findByText('Accounting Configuration')).toBeInTheDocument(); expect(screen.getByText('GL Accounts')).toBeInTheDocument(); expect(screen.getByText('Configurations')).toBeInTheDocument() })
})
