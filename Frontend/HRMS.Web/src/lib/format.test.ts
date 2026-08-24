import { describe, expect, it } from 'vitest'
import { formatDate, formatDateTime, formatNumber, initials, parseDateOnly } from './format.ts'

describe('date-only formatting', () => {
  it('keeps the calendar day the API sent', () => {
    // The bug this guards: `new Date('2023-03-14')` is UTC midnight, which renders as the 13th
    // anywhere west of Greenwich. A joining date must not depend on where the browser is.
    const parsed = parseDateOnly('2023-03-14')

    expect(parsed?.getFullYear()).toBe(2023)
    expect(parsed?.getMonth()).toBe(2)
    expect(parsed?.getDate()).toBe(14)
    expect(formatDate('2023-03-14')).toContain('14')
    expect(formatDate('2023-03-14')).toContain('2023')
  })

  it('shows an em dash for an absent date, so table columns stay aligned', () => {
    expect(formatDate(null)).toBe('—')
    expect(formatDate(undefined)).toBe('—')
    expect(formatDate('')).toBe('—')
  })

  it('returns anything unrecognized unchanged rather than "Invalid Date"', () => {
    expect(formatDate('14/03/2023')).toBe('14/03/2023')
    expect(parseDateOnly('14/03/2023')).toBeNull()
  })
})

describe('timestamp formatting', () => {
  it('formats a UTC instant', () => {
    expect(formatDateTime('2026-01-05T09:30:00Z')).toContain('2026')
  })

  it('treats an offset-less timestamp as UTC, not as local time', () => {
    // A serializer configured for UTC may omit the marker; reading it as local would shift the value
    // by the viewer's offset.
    expect(formatDateTime('2026-01-05T09:30:00')).toBe(formatDateTime('2026-01-05T09:30:00Z'))
  })

  it('shows an em dash for an absent timestamp', () => {
    expect(formatDateTime(null)).toBe('—')
  })
})

describe('initials', () => {
  it('takes the first and last name', () => {
    expect(initials('Priya Raman')).toBe('PR')
    expect(initials('Grace Adaeze Okoro')).toBe('GO')
  })

  it('copes with a single name and with nothing', () => {
    expect(initials('Cher')).toBe('C')
    expect(initials('   ')).toBe('?')
  })
})

describe('numbers', () => {
  it('groups thousands', () => {
    expect(formatNumber(1234)).toMatch(/1[.,\s ]?234/)
  })
})
