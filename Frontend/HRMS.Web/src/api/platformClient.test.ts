import { describe, expect, it } from 'vitest'
import { PLATFORM_PROVISIONING_TIMEOUT_MS, platformApi } from './platformClient.ts'

describe('platform API client', () => {
  it('allows tenant provisioning to outlive the normal API request timeout', () => {
    expect(PLATFORM_PROVISIONING_TIMEOUT_MS).toBe(300_000)
    expect(platformApi.defaults.timeout).toBe(PLATFORM_PROVISIONING_TIMEOUT_MS)
  })
})
