import { describe, expect, it } from 'vitest'
import { isApexHost } from './isApexHost.ts'

describe('isApexHost', () => {
  const env = { originTemplate: 'http://{workspace}.localhost:5080' }

  it('returns true for the bare base domain', () => {
    expect(isApexHost('localhost', env)).toBe(true)
  })

  it('returns true when the hostname has a trailing dot', () => {
    expect(isApexHost('localhost.', env)).toBe(true)
  })

  it('returns false for a workspace host', () => {
    expect(isApexHost('demo01.localhost', env)).toBe(false)
  })

  it('returns false for an unrelated host', () => {
    expect(isApexHost('example.com', env)).toBe(false)
  })

  it('returns false when no template is configured', () => {
    expect(isApexHost('localhost', { originTemplate: undefined })).toBe(false)
  })

  it('returns false for an unparseable template', () => {
    expect(isApexHost('localhost', { originTemplate: 'not a template' })).toBe(false)
  })

  it('returns false for an empty hostname', () => {
    expect(isApexHost('', env)).toBe(false)
  })

  it('returns true case-insensitively', () => {
    expect(isApexHost('LOCALHOST', env)).toBe(true)
  })
})
