import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { visibleNavItems } from './navigation.ts'

describe('Leave Policy navigation permissions', () => {
  it('shows Policy navigation to PolicyView users', () => {
    expect(visibleNavItems(permission => permission === Permissions.leave.policyView).some(item => item.to === '/leave-management/policies')).toBe(true)
  })

  it('hides Policy navigation without PolicyView', () => {
    expect(visibleNavItems(() => false).some(item => item.to === '/leave-management/policies')).toBe(false)
  })
})
