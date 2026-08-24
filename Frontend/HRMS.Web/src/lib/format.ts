/**
 * Display formatting for the values the API sends.
 *
 * The date handling here is not busywork. The API sends two different shapes and they must not be
 * parsed the same way:
 *
 * - `DateOnly` fields (`dateOfJoining`, `dateOfBirth`, `dateOfLeaving`) arrive as `yyyy-MM-dd`. Passing
 *   that to `new Date()` gives **UTC** midnight, which in any negative-offset timezone renders as the
 *   day before. A joining date must never shift because the browser is in Chicago, so those are split
 *   and rebuilt as a local date.
 * - `DateTime` fields (`createdDate`, `lastLoginDateUtc`) are UTC instants and are shown in the
 *   viewer's own timezone, which is the point of storing them in UTC.
 */

const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/

/** `yyyy-MM-dd` → a `Date` at local midnight, so the calendar day cannot drift. */
export function parseDateOnly(value: string): Date | null {
  const match = DATE_ONLY.exec(value.trim())
  if (!match) return null
  const [, year, month, day] = match
  const date = new Date(Number(year), Number(month) - 1, Number(day))
  return Number.isNaN(date.getTime()) ? null : date
}

/** A `DateOnly` as e.g. "14 Mar 2023". Returns an em dash for absent values so tables stay aligned. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = parseDateOnly(value)
  if (!date) return value
  return date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })
}

/** A UTC `DateTime` in the viewer's timezone, date and time. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(hasTimezone(value) ? value : `${value}Z`)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString(undefined, {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * A serializer configured for UTC ends timestamps with `Z`, but an offset-less string would be read as
 * *local* time by `Date` — turning a UTC instant into a value hours out. Assume UTC when unmarked.
 */
function hasTimezone(value: string): boolean {
  return /(?:Z|[+-]\d{2}:?\d{2})$/.test(value)
}

export function formatNumber(value: number): string {
  return value.toLocaleString()
}

/** Up to two initials for the avatar chip in the header. */
export function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  return (first + last).toUpperCase()
}
