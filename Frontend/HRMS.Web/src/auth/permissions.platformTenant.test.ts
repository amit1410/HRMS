import { describe, expect, it } from 'vitest'
import { Permissions } from './permissions.ts'
import { PlatformPermissions } from './platformPermissions.ts'

describe('platform tenant permissions', () => {
  it('uses a separate platform authorization boundary', () => {
    expect(PlatformPermissions).toEqual({
      view: 'PlatformTenant.View',
      create: 'PlatformTenant.Create',
      updateStatus: 'PlatformTenant.UpdateStatus',
    })
    expect(Permissions).not.toHaveProperty('platformTenant')
  })
})
