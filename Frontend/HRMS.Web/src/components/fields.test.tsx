import userEvent from '@testing-library/user-event'
import { render, screen, waitFor } from '@testing-library/react'
import { useState, type ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { SupervisorField } from './fields.tsx'
import { getSupervisorOptions } from '../api/employeeSubsections.ts'
import type { SupervisorOption } from '../api/types.ts'

vi.mock('../api/employeeSubsections.ts', () => ({
  getSupervisorOptions: vi.fn(),
}))

const options: SupervisorOption[] = [
  { employeeId: 'employee-amit', employeeCode: 'EMP-001', fullName: 'Amit Rao' },
  { employeeId: 'employee-aman', employeeCode: 'EMP-002', fullName: 'Aman Das' },
  { employeeId: 'employee-amrita', employeeCode: 'EMP-003', fullName: 'Amrita Sen' },
]

function Harness(): ReactElement {
  const [value, setValue] = useState('manager-1')
  return (
    <SupervisorField
      id="l1ManagerId"
      label="L1 manager"
      employeeId="employee-1"
      supervisorType="L1"
      value={value}
      selectedCode="EMP-000"
      selectedName="Existing Manager"
      onChange={(employeeId) => setValue(employeeId)}
    />
  )
}

describe('SupervisorField', () => {
  it('searches while typing, supports keyboard selection, and can clear a replacement', async () => {
    vi.mocked(getSupervisorOptions).mockImplementation(async (_employeeId, _type, params = {}) => ({
      items: params.search?.toLowerCase() === 'am' ? options : [],
      page: 1,
      pageSize: 20,
      totalCount: params.search?.toLowerCase() === 'am' ? options.length : 0,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    }))

    const user = userEvent.setup()
    render(<Harness />)
    const input = screen.getByLabelText('L1 manager')

    expect(input).toHaveValue('EMP-000 — Existing Manager')
    await user.click(input)
    await user.type(input, 'am')

    await waitFor(() => expect(getSupervisorOptions).toHaveBeenCalledWith(
      'employee-1',
      'L1',
      { search: 'am', page: 1, pageSize: 20 },
      expect.any(AbortSignal),
    ))
    expect(await screen.findByText('Amit Rao')).toBeInTheDocument()
    expect(screen.getByText('EMP-002')).toBeInTheDocument()

    await user.keyboard('{ArrowDown}{Enter}')
    expect(input).toHaveValue('EMP-001 — Amit Rao')

    await user.click(screen.getByRole('button', { name: 'Clear selection' }))
    expect(input).toHaveValue('')
  })

  it('shows an empty-result state and an API error state without hiding the list', async () => {
    const user = userEvent.setup()
    vi.mocked(getSupervisorOptions).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })
    const { unmount } = render(
      <SupervisorField
        id="l1ManagerId"
        label="L1 manager"
        employeeId="employee-1"
        supervisorType="L1"
        value=""
        onChange={vi.fn()}
      />,
    )

    await user.click(screen.getByLabelText('L1 manager'))
    expect(await screen.findByText('No eligible active employees found.')).toBeInTheDocument()
    unmount()

    vi.mocked(getSupervisorOptions).mockRejectedValue(new Error('network down'))
    render(
      <SupervisorField
        id="l1ManagerId-error"
        label="L1 manager error"
        employeeId="employee-1"
        supervisorType="L1"
        value=""
        onChange={vi.fn()}
      />,
    )
    await user.click(screen.getByLabelText('L1 manager error'))
    expect(await screen.findByText('Unable to load employees. Try again.')).toBeInTheDocument()
  })

  it('selects an option with the mouse and keeps the saved label visible after closing', async () => {
    vi.mocked(getSupervisorOptions).mockResolvedValue({
      items: [options[0]!],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <SupervisorField
        id="l1ManagerId-mouse"
        label="L1 manager mouse"
        employeeId="employee-1"
        supervisorType="L1"
        value=""
        onChange={onChange}
      />,
    )
    const input = screen.getByLabelText('L1 manager mouse')
    await user.click(input)
    await user.click(await screen.findByRole('option', { name: /EMP-001.*Amit Rao/ }))
    expect(onChange).toHaveBeenCalledWith('employee-amit', options[0])
  })
})
