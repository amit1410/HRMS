import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createSalaryComponent, createSalaryStructure, getSalaryComponentHistory, listSalaryComponents, listSalaryStructures, setSalaryComponentActive, setSalaryStructureActive, updateSalaryComponent, updateSalaryStructure } from './payroll.ts'
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
})
