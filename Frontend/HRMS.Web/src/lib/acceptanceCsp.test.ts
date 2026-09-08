import { describe, expect, it } from 'vitest'
import { applyAcceptanceConnectSrc } from './acceptanceCsp.ts'
import { cspConnectSources } from './cspConnectSources.ts'

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

describe('development CSP destinations', () => {
  it('allows the platform API, workspace APIs, and Vite HMR only in development', () => {
    const sources = cspConnectSources('development', [])
    expect(sources).toContain('http://platform.localhost:5080')
    expect(sources).toContain('http://*.localhost:5080')
    expect(sources).toContain('ws://platform.localhost:5173')
    expect(sources).toContain('ws://*.localhost:5173')
    expect(sources).not.toContain('*')
  })

  it('does not inherit localhost development destinations in production', () => {
    expect(cspConnectSources('production', [])).toEqual([])
  })

  it('uses explicit deployment or acceptance sources when configured', () => {
    expect(cspConnectSources('development', ['https://api.example.test'])).toEqual([
      'https://api.example.test',
      'ws://platform.localhost:5173',
      'ws://*.localhost:5173',
    ])
  })
})
