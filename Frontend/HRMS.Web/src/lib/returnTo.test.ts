import { describe, expect, it } from 'vitest'
import { returnPath } from './returnTo.ts'

/**
 * History state is whatever the thing that navigated chose to put there, so `from` is treated as input
 * that needs checking rather than a destination to trust — the same posture `LoginPage` takes with the
 * `from` a route guard hands it.
 */
describe('returnPath', () => {
  it('goes back to the exact view the link came from, query string and all', () => {
    expect(returnPath({ from: '/departments?page=3&search=eng' }, '/departments')).toBe(
      '/departments?page=3&search=eng',
    )
    expect(returnPath({ from: '/departments' }, '/departments')).toBe('/departments')
  })

  it('falls back to the list when nothing usable was handed over', () => {
    expect(returnPath(null, '/departments')).toBe('/departments')
    expect(returnPath(undefined, '/departments')).toBe('/departments')
    expect(returnPath({}, '/departments')).toBe('/departments')
    expect(returnPath({ from: 42 }, '/departments')).toBe('/departments')
    expect(returnPath('/departments?page=3', '/departments')).toBe('/departments')
  })

  it('refuses a path that does not address this module’s own list', () => {
    // Another module's list is not where Cancel belongs, even though it is a real screen.
    expect(returnPath({ from: '/employees' }, '/departments')).toBe('/departments')
    // Nor is somewhere outside the app: a protocol-relative path is an origin, not a route.
    expect(returnPath({ from: '//elsewhere.test/departments' }, '/departments')).toBe('/departments')
    expect(returnPath({ from: 'https://elsewhere.test/departments' }, '/departments')).toBe(
      '/departments',
    )
    // A shared prefix is not the same path.
    expect(returnPath({ from: '/departments-archive' }, '/departments')).toBe('/departments')
    // The form itself, which would be a Cancel that goes nowhere.
    expect(returnPath({ from: '/departments/d1/edit' }, '/departments')).toBe('/departments')
  })

  it('serves both modules from the base path it is given', () => {
    expect(returnPath({ from: '/designations?dir=desc' }, '/designations')).toBe(
      '/designations?dir=desc',
    )
    expect(returnPath({ from: '/designations' }, '/employees')).toBe('/employees')
  })
})
