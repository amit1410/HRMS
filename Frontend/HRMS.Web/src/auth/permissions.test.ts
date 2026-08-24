import { describe, expect, it } from 'vitest'
import { hasAnyPermission, hasPermission, Permissions } from './permissions.ts'
import { makeUser, MANAGER_PERMISSIONS } from '../test/fixtures.ts'

describe('permission checks', () => {
  it('grants what the user holds', () => {
    const user = makeUser({ permissions: MANAGER_PERMISSIONS })

    expect(hasPermission(user, Permissions.employee.view)).toBe(true)
    expect(hasPermission(user, Permissions.employee.delete)).toBe(false)
  })

  it('ignores case, because the API authorization policies do', () => {
    const user = makeUser({ permissions: ['employee.view'] })

    expect(hasPermission(user, 'Employee.View')).toBe(true)
  })

  it('grants nothing to an absent user', () => {
    expect(hasPermission(null, Permissions.employee.view)).toBe(false)
    expect(hasAnyPermission(undefined, [Permissions.employee.view])).toBe(false)
  })

  it('needs only one of several', () => {
    const user = makeUser({ permissions: [Permissions.department.view] })

    expect(hasAnyPermission(user, [Permissions.employee.view, Permissions.department.view])).toBe(
      true,
    )
    expect(hasAnyPermission(user, [Permissions.employee.view, Permissions.user.view])).toBe(false)
  })

  it('grants nothing on an empty list', () => {
    expect(hasAnyPermission(makeUser(), [])).toBe(false)
  })
})
