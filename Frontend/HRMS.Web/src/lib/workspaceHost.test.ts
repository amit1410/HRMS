import { describe, expect, it } from 'vitest'
import {
  apexOrigin,
  isWorkspaceLabel,
  originForWorkspace,
  parseOriginTemplate,
  toWorkspaceLabel,
  workspaceLabelFor,
  workspaceUrl,
} from './workspaceHost.ts'

/**
 * The tenancy boundary as the client sees it is string handling, so every rule in
 * `workspaceHost.ts` is pinned here: what counts as one label, what the apex looks like, which
 * pasted shapes reduce to a label and which are refused.
 */

describe('isWorkspaceLabel', () => {
  it('accepts a single lowercase label of letters, digits and inner hyphens', () => {
    expect(isWorkspaceLabel('demo01')).toBe(true)
    expect(isWorkspaceLabel('a')).toBe(true)
    expect(isWorkspaceLabel('engineering-west2')).toBe(true)
  })

  it('refuses a leading or trailing hyphen, a dot, and the empty string', () => {
    expect(isWorkspaceLabel('-demo01')).toBe(false)
    expect(isWorkspaceLabel('demo01-')).toBe(false)
    expect(isWorkspaceLabel('a.b')).toBe(false)
    expect(isWorkspaceLabel('')).toBe(false)
  })

  it('enforces the DNS label length limit at 63 characters', () => {
    expect(isWorkspaceLabel('a'.repeat(63))).toBe(true)
    expect(isWorkspaceLabel('a'.repeat(64))).toBe(false)
  })

  it('refuses uppercase — callers normalize before asking', () => {
    // The pattern is deliberately lowercase-only; `toWorkspaceLabel` is what lowercases input.
    expect(isWorkspaceLabel('DEMO01')).toBe(false)
  })
})

describe('workspaceLabelFor', () => {
  const base = 'hrms.example'

  it('extracts the single label in front of the base domain', () => {
    expect(workspaceLabelFor('demo01.hrms.example', base)).toBe('demo01')
  })

  it('answers null for the apex itself', () => {
    expect(workspaceLabelFor('hrms.example', base)).toBeNull()
  })

  it('answers null for a nested label — never two workspaces deep', () => {
    expect(workspaceLabelFor('a.b.hrms.example', base)).toBeNull()
  })

  it('treats a trailing root dot as the same host', () => {
    expect(workspaceLabelFor('demo01.hrms.example.', base)).toBe('demo01')
  })

  it('is case-insensitive on both sides', () => {
    expect(workspaceLabelFor('DEMO01.HRMS.Example', 'HRMS.example')).toBe('demo01')
  })

  it('answers null for another domain entirely', () => {
    expect(workspaceLabelFor('demo01.other.example', base)).toBeNull()
  })

  it('does not match when the base appears only as a prefix of a longer suffix', () => {
    // The dot-anchored comparison is what keeps this from resolving.
    expect(workspaceLabelFor('demo01.hrms.example.attacker.test', base)).toBeNull()
  })

  it('answers null for malformed labels under the base domain', () => {
    expect(workspaceLabelFor('-bad-.hrms.example', base)).toBeNull()
  })

  it('answers null for empty inputs', () => {
    expect(workspaceLabelFor('', base)).toBeNull()
    expect(workspaceLabelFor('demo01.hrms.example', '')).toBeNull()
  })
})

describe('parseOriginTemplate', () => {
  it('splits a template at the label, keeping the port with the suffix', () => {
    expect(parseOriginTemplate('http://{workspace}.localhost:5080')).toEqual({
      prefix: 'http://',
      suffix: 'localhost:5080',
      baseDomain: 'localhost',
    })
  })

  it('parses an https template', () => {
    expect(parseOriginTemplate('https://{workspace}.hrms.example')).toEqual({
      prefix: 'https://',
      suffix: 'hrms.example',
      baseDomain: 'hrms.example',
    })
  })

  it('trims trailing slashes from the suffix', () => {
    const parsed = parseOriginTemplate('https://{workspace}.hrms.example/')
    expect(parsed?.suffix).toBe('hrms.example')
  })

  it('normalizes the template to lowercase', () => {
    const parsed = parseOriginTemplate('HTTPS://{workspace}.HRMS.Example')
    expect(parsed?.prefix).toBe('https://')
    expect(parsed?.baseDomain).toBe('hrms.example')
  })

  it('refuses a template whose placeholder has no scheme in front of it', () => {
    expect(parseOriginTemplate('{workspace}.localhost')).toBeNull()
  })

  it('refuses a template with no dot after the placeholder', () => {
    expect(parseOriginTemplate('http://{workspace}')).toBeNull()
  })

  it('refuses a placeholder anywhere but the first label', () => {
    // A placeholder inside the base domain would resolve to hosts nobody serves.
    expect(parseOriginTemplate('http://api.{workspace}.example.com')).toBeNull()
  })

  it('refuses more than one placeholder', () => {
    expect(parseOriginTemplate('http://{workspace}.{workspace}.example.com')).toBeNull()
  })

  it('refuses a template with no placeholder at all', () => {
    expect(parseOriginTemplate('http://hrms.example')).toBeNull()
    expect(parseOriginTemplate('')).toBeNull()
  })
})

describe('originForWorkspace and apexOrigin', () => {
  const template = parseOriginTemplate('http://{workspace}.localhost:5080')!

  it('puts the label in front of the suffix', () => {
    expect(originForWorkspace(template, 'demo01')).toBe('http://demo01.localhost:5080')
  })

  it('builds the apex by dropping the label entirely', () => {
    expect(apexOrigin(template)).toBe('http://localhost:5080')
  })
})

describe('toWorkspaceLabel', () => {
  it('accepts a bare label and trims surrounding whitespace', () => {
    expect(toWorkspaceLabel(' demo01 ')).toBe('demo01')
  })

  it('lowercases what it accepts', () => {
    expect(toWorkspaceLabel('DEMO01')).toBe('demo01')
  })

  it('reduces a full host to its first label', () => {
    expect(toWorkspaceLabel('demo01.hrms.example')).toBe('demo01')
  })

  it('reduces a pasted URL — scheme, path, query and fragment stripped', () => {
    expect(toWorkspaceLabel('https://demo01.hrms.example/login')).toBe('demo01')
    expect(toWorkspaceLabel('https://demo01.hrms.example/login?next=/dashboard#top')).toBe(
      'demo01',
    )
  })

  it('strips userinfo and port before reading the label', () => {
    expect(toWorkspaceLabel('http://someone@demo01.hrms.example')).toBe('demo01')
    expect(toWorkspaceLabel('demo01.localhost:5173')).toBe('demo01')
  })

  it('refuses anything that is not one label once the decoration is gone', () => {
    expect(toWorkspaceLabel('')).toBeNull()
    expect(toWorkspaceLabel('   ')).toBeNull()
    expect(toWorkspaceLabel('//evil.example')).toBeNull()
    expect(toWorkspaceLabel('a_b.example')).toBeNull()
    expect(toWorkspaceLabel('-nope')).toBeNull()
    expect(toWorkspaceLabel('a'.repeat(64))).toBeNull()
  })
})

describe('workspaceUrl', () => {
  const location = { protocol: 'http:', hostname: 'localhost', port: '5173' }

  it('sends a valid label to the same scheme, host and port, at /login', () => {
    expect(workspaceUrl('demo01', location)).toBe('http://demo01.localhost:5173/login')
  })

  it('accepts every shape toWorkspaceLabel accepts', () => {
    expect(workspaceUrl('  HTTPS://Demo01.Example/login ', { protocol: 'https:', hostname: 'example', port: '' })).toBe(
      'https://demo01.example/login',
    )
  })

  it('normalizes a trailing root dot on the current hostname', () => {
    expect(workspaceUrl('demo01', { ...location, hostname: 'localhost.' })).toBe(
      'http://demo01.localhost:5173/login',
    )
  })

  it('refuses an input that reduces to no label', () => {
    expect(workspaceUrl('not a label!', location)).toBeNull()
    expect(workspaceUrl('', location)).toBeNull()
  })
})
