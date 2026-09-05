import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { approveLeaveRequest, cancelLeaveRequest, getLeaveApproval, listLeaveApprovals, rejectLeaveRequest, withdrawLeaveRequest } from './leaveRequests.ts'
import { installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'

describe('leave approval API client', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('uses the paged inbox endpoint', async () => {
    stub.on('get', '/api/leave-approvals', () => ({ data: ok({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0, hasPreviousPage: true, hasNextPage: false }) }))
    const result = await listLeaveApprovals(2, 10)
    expect(result.page).toBe(2)
    expect(stub.calls[0]?.params).toMatchObject({ page: 2, pageSize: 10 })
  })

  it('gets approval detail and posts empty approve/reject commands', async () => {
    stub.on('get', '/api/leave-approvals/request-1', () => ({ data: ok({ requestId: 'request-1' }) }))
    stub.on('post', '/api/leave-requests/request-1/approve', () => ({ data: ok({ requestId: 'request-1', status: 'Approved', eventType: 'Approved', occurredAtUtc: '2026-01-01T00:00:00Z' }) }))
    stub.on('post', '/api/leave-requests/request-1/reject', () => ({ data: ok({ requestId: 'request-1', status: 'Rejected', eventType: 'Rejected', occurredAtUtc: '2026-01-01T00:00:00Z' }) }))
    await getLeaveApproval('request-1')
    await approveLeaveRequest('request-1')
    await rejectLeaveRequest('request-1')
    expect(stub.calls.map(call => call.url)).toEqual(['/api/leave-approvals/request-1', '/api/leave-requests/request-1/approve', '/api/leave-requests/request-1/reject'])
    expect(stub.calls[1]?.body).toBeUndefined()
    expect(stub.calls[2]?.body).toBeUndefined()
  })

  it('posts an empty withdraw command and parses the authoritative response', async () => {
    stub.on('post', '/api/leave-requests/request-1/withdraw', () => ({ data: ok({ requestId: 'request-1', status: 'Withdrawn', eventType: 'Withdrawn', occurredAtUtc: '2026-01-01T00:00:00Z' }) }))
    const result = await withdrawLeaveRequest('request-1')
    expect(result).toEqual({ requestId: 'request-1', status: 'Withdrawn', eventType: 'Withdrawn', occurredAtUtc: '2026-01-01T00:00:00Z' })
    expect(stub.calls[0]?.method).toBe('post')
    expect(stub.calls[0]?.url).toBe('/api/leave-requests/request-1/withdraw')
    expect(stub.calls[0]?.body).toBeUndefined()
  })

  it('posts an empty cancel command and parses the authoritative response', async () => {
    stub.on('post', '/api/leave-requests/request-1/cancel', () => ({ data: ok({ requestId: 'request-1', status: 'Cancelled', eventType: 'Cancelled', occurredAtUtc: '2026-01-01T00:00:00Z' }) }))
    const result = await cancelLeaveRequest('request-1')
    expect(result).toEqual({ requestId: 'request-1', status: 'Cancelled', eventType: 'Cancelled', occurredAtUtc: '2026-01-01T00:00:00Z' })
    expect(stub.calls[0]?.method).toBe('post')
    expect(stub.calls[0]?.url).toBe('/api/leave-requests/request-1/cancel')
    expect(stub.calls[0]?.body).toBeUndefined()
  })
})
