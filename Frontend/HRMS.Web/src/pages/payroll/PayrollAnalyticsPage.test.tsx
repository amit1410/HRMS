import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollAnalyticsPage } from './PayrollAnalyticsPage.tsx'

vi.mock('../../hooks/useApiQuery.ts', () => ({ useApiQuery: () => ({ data: { employeeCount: 2, grossTotal: 100, deductionTotal: 10, netPayTotal: 90, openFindings: 1, criticalFindings: 0, anomalyCount: 0 } }) }))
describe('PayrollAnalyticsPage', () => { it('renders persisted run controls summary', () => { render(<PayrollAnalyticsPage />); expect(screen.getByText('Payroll Analytics & Controls')).toBeInTheDocument(); expect(screen.getByText('Open findings')).toBeInTheDocument() }) })
