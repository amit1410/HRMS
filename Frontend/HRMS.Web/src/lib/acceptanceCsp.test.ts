import { describe, expect, it } from 'vitest'
import { applyAcceptanceConnectSrc } from './acceptanceCsp.ts'

const baseline = '<meta content="default-src \'self\'; connect-src \'self\' http://demo01.localhost:5080; img-src \'self\'">'

describe('disposable browser acceptance CSP', () => {
  it('keeps the normal policy unchanged when no acceptance origins are supplied', () => {
    expect(applyAcceptanceConnectSrc(baseline, [])).toBe(baseline)
  })

  it('allows only both run tenant API origins with the dynamically selected port', () => {
    const result = applyAcceptanceConnectSrc(baseline, [
      'http://tenant-a.localhost:35148',
      'http://tenant-b.localhost:35148',
    ])

    expect(result).toContain("connect-src 'self' http://tenant-a.localhost:35148 http://tenant-b.localhost:35148;")
    expect(result).not.toContain('demo01.localhost:5080')
    expect(result).not.toContain('*')
  })

  it('rejects non-origin destinations instead of widening connect-src', () => {
    expect(() => applyAcceptanceConnectSrc(baseline, ['*'])).toThrow(/absolute origin/)
    expect(() => applyAcceptanceConnectSrc(baseline, ['http://tenant-a.localhost:35148/api'])).toThrow(/absolute origin/)
  })
})
