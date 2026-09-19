import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createEmployeeSalaryAssignment, getEmployeeSalaryAssignmentHistory, listEmployeeSalaryAssignments, setEmployeeSalaryAssignmentActive, updateEmployeeSalaryAssignment, createPayrollPeriod, createPayrollRun, listPayrollPeriods, listPayrollRuns, preparePayrollRun, transitionPayrollPeriod, transitionPayrollRun, createSalaryComponent, createSalaryStructure, getSalaryComponentHistory, listSalaryComponents, listSalaryStructures, setSalaryComponentActive, setSalaryStructureActive, updateSalaryComponent, updateSalaryStructure } from './payroll.ts'
import { installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'
import { paged } from '../test/fixtures.ts'

describe('salary component API client', () => {
  let stub: StubAdapter

  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('serializes server-side search and component filters', async () => {
    stub.on('get', '/api/payroll/salary-components', () => ({ data: ok(paged([])) }))
    await listSalaryComponents({ search: 'basic', componentType: 'Earning', calculationType: 'FixedAmount', isActive: true, isStatutory: false, page: 2, pageSize: 20 })
    expect(stub.calls[0]?.params).toMatchObject({ search: 'basic', componentType: 'Earning', calculationType: 'FixedAmount', isActive: true, isStatutory: false, page: 2, pageSize: 20 })
  })

  it('supports create, update, activation and history endpoints', async () => {
    stub.on('post', '/api/payroll/salary-components', () => ({ data: ok({ id: 'component-1' }) }))
    stub.on('put', '/api/payroll/salary-components/component-1', () => ({ data: ok({ id: 'component-1' }) }))
    stub.on('post', '/api/payroll/salary-components/component-1/deactivate', () => ({ data: ok({ id: 'component-1' }) }))
    stub.on('get', '/api/payroll/salary-components/component-1/history', () => ({ data: ok([]) }))
    const request = { code: 'BASIC', name: 'Basic', componentType: 'Earning' as const, calculationType: 'FixedAmount' as const, statutoryType: 'None' as const, isTaxable: true, isStatutory: false, isRecurring: true, affectsGross: true, affectsNetPay: true, displayOrder: 0, effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true }
    await createSalaryComponent(request); await updateSalaryComponent('component-1', { ...request, expectedConcurrencyVersion: 1 }); await setSalaryComponentActive('component-1', false, 1); await getSalaryComponentHistory('component-1')
    expect(stub.calls.map(call => `${call.method}:${call.url}`)).toEqual(['post:/api/payroll/salary-components', 'put:/api/payroll/salary-components/component-1', 'post:/api/payroll/salary-components/component-1/deactivate', 'get:/api/payroll/salary-components/component-1/history'])
  })

  it('supports salary structure list, composition, update and activation endpoints', async () => {
    stub.on('get', '/api/payroll/salary-structures', () => ({ data: ok(paged([])) }))
    stub.on('post', '/api/payroll/salary-structures', () => ({ data: ok({ id: 'structure-1' }) }))
    stub.on('put', '/api/payroll/salary-structures/structure-1', () => ({ data: ok({ id: 'structure-1' }) }))
    stub.on('post', '/api/payroll/salary-structures/structure-1/deactivate', () => ({ data: ok({ id: 'structure-1' }) }))
    const request = { code: 'STAFF_MONTHLY', name: 'Staff Monthly', effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true, components: [{ salaryComponentId: 'component-1', sequence: 1, calculationType: 'FixedAmount' as const, value: 10000, percentageOfComponentId: null, formula: null, isProratable: true, isEditableAtEmployeeLevel: false, minimumAmount: null, maximumAmount: null, isActive: true }] }
    await listSalaryStructures({ search: 'staff', isActive: true, page: 2, pageSize: 20 }); await createSalaryStructure(request); await updateSalaryStructure('structure-1', { ...request, expectedConcurrencyVersion: 1 }); await setSalaryStructureActive('structure-1', false, 1)
    expect(stub.calls.map(call => `${call.method}:${call.url}`)).toEqual(['get:/api/payroll/salary-structures', 'post:/api/payroll/salary-structures', 'put:/api/payroll/salary-structures/structure-1', 'post:/api/payroll/salary-structures/structure-1/deactivate'])
    expect(stub.calls[0]?.params).toMatchObject({ search: 'staff', isActive: true, page: 2, pageSize: 20 })
  })

  it('supports employee salary assignment and history endpoints', async () => {
    stub.on('get', '/api/payroll/employee-salary-assignments', () => ({ data: ok(paged([])) }))
    stub.on('post', '/api/payroll/employee-salary-assignments', () => ({ data: ok({ id: 'assignment-1' }) }))
    stub.on('put', '/api/payroll/employee-salary-assignments/assignment-1', () => ({ data: ok({ id: 'assignment-1' }) }))
    stub.on('post', '/api/payroll/employee-salary-assignments/assignment-1/deactivate', () => ({ data: ok({ id: 'assignment-1' }) }))
    stub.on('get', '/api/payroll/employee-salary-assignments/assignment-1/history', () => ({ data: ok([]) }))
    const request = { employeeId: 'employee-1', salaryStructureId: 'structure-1', effectiveFrom: '2026-01-01', effectiveTo: null, annualCtc: 120000, monthlyCtc: 10000, currencyCode: 'INR', payFrequency: 'Monthly' as const, status: 'Active' as const, changeReason: 'NewHire' as const, remarks: '', components: [] }
    await listEmployeeSalaryAssignments({ search: 'E001', page: 2, pageSize: 20 }); await createEmployeeSalaryAssignment(request); await updateEmployeeSalaryAssignment('assignment-1', { ...request, expectedConcurrencyVersion: 1 }); await setEmployeeSalaryAssignmentActive('assignment-1', false, 1); await getEmployeeSalaryAssignmentHistory('assignment-1')
    expect(stub.calls.map(call => `${call.method}:${call.url}`)).toEqual(['get:/api/payroll/employee-salary-assignments', 'post:/api/payroll/employee-salary-assignments', 'put:/api/payroll/employee-salary-assignments/assignment-1', 'post:/api/payroll/employee-salary-assignments/assignment-1/deactivate', 'get:/api/payroll/employee-salary-assignments/assignment-1/history'])
  })

  it('supports payroll period and run setup endpoints', async () => {
    stub.on('get', '/api/payroll/periods', () => ({ data: ok(paged([])) }))
    stub.on('post', '/api/payroll/periods', () => ({ data: ok({ id: 'period-1' }) }))
    stub.on('post', '/api/payroll/periods/period-1/open', () => ({ data: ok({ id: 'period-1' }) }))
    stub.on('get', '/api/payroll/runs', () => ({ data: ok(paged([])) }))
    stub.on('post', '/api/payroll/runs', () => ({ data: ok({ id: 'run-1' }) }))
    stub.on('post', '/api/payroll/runs/run-1/prepare', () => ({ data: ok({ id: 'run-1' }) }))
    stub.on('post', '/api/payroll/runs/run-1/transition', () => ({ data: ok({ id: 'run-1' }) }))
    const period = { code: 'SEP-2026', name: 'September 2026', periodType: 'Monthly' as const, startDate: '2026-09-01', endDate: '2026-09-30', payDate: '2026-10-05', fiscalYear: 2026, periodNumber: 9, isActive: true }
    await listPayrollPeriods({ status: 'Draft', page: 1, pageSize: 10 }); await createPayrollPeriod(period); await transitionPayrollPeriod('period-1', 'open', 1)
    await listPayrollRuns({ payrollPeriodId: 'period-1' }); await createPayrollRun({ payrollPeriodId: 'period-1', runType: 'Regular' }); await preparePayrollRun('run-1'); await transitionPayrollRun('run-1', 'Processing')
    expect(stub.calls.map(call => `${call.method}:${call.url}`)).toEqual(['get:/api/payroll/periods', 'post:/api/payroll/periods', 'post:/api/payroll/periods/period-1/open', 'get:/api/payroll/runs', 'post:/api/payroll/runs', 'post:/api/payroll/runs/run-1/prepare', 'post:/api/payroll/runs/run-1/transition'])
  })
})
