import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'
import { ShiftPatternsPage } from './ShiftPatternsPage.tsx'

describe('ShiftPatternsPage', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.() })

  it('renders a dynamic N-day editor and weekly-off option', async () => {
    const stub = installStubAdapter()
    restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'M', shiftName: 'Morning' }]) }))
    stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) }))
    renderAsUser(<ShiftPatternsPage />)
    expect(await screen.findByText('Shift Patterns')).toBeInTheDocument()
    expect(screen.getByText('Cycle length (days)')).toBeInTheDocument()
    expect(screen.getAllByText('Weekly Off').length).toBeGreaterThan(0)
  })
})
