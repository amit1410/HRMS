import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ApiError } from './errors.ts'
import { createLeaveType, listLeavePeriods, listLeaveTypes, updateLeavePeriod } from './leaveConfiguration.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'
import { paged } from '../test/fixtures.ts'

describe('leave configuration API client', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('serializes typed Leave Type filters and unwraps the page', async () => {
    stub.on('get', '/api/leave-types', () => ({ data: ok(paged([], { totalCount: 0 })) }))
    const result = await listLeaveTypes({ search: 'casual', isActive: true, page: 2, pageSize: 20 })
    expect(result.totalCount).toBe(0); expect(stub.calls[0]?.params).toMatchObject({ search: 'casual', isActive: true, page: 2, pageSize: 20 })
  })

  it('uses the date-only period query contract', async () => {
    stub.on('get', '/api/leave-periods', () => ({ data: ok(paged([])) }))
    await listLeavePeriods({ onDate: '2027-07-01' })
    expect(stub.calls[0]?.params).toMatchObject({ onDate: '2027-07-01' })
  })

  it('maps a 409 API response to ApiError without hiding its message', async () => {
    stub.on('post', '/api/leave-types', () => ({ status: 409, data: fail('A Leave Type with this code already exists.') }))
    await expect(createLeaveType({ code: 'CL', name: 'Casual Leave', defaultUnit: 'Day', isPaid: true, isActive: true })).rejects.toMatchObject({ status: 409, message: 'A Leave Type with this code already exists.' })
  })

  it('retains field errors from a 409 update', async () => {
    stub.on('put', '/api/leave-periods/p1', () => ({ status: 409, data: fail('Conflict', [{ field: 'dates', message: 'Overlap.' }]) }))
    try { await updateLeavePeriod('p1', { code: '2027', name: '2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true, concurrencyToken: 'old' }); throw new Error('expected rejection') } catch (error) { expect(error).toBeInstanceOf(ApiError); expect((error as ApiError).fieldErrors.dates).toBe('Overlap.') }
  })
})
