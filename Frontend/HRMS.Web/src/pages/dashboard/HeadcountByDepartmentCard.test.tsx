import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { HeadcountByDepartmentCard } from './HeadcountByDepartmentCard.tsx'

const useApiQuery = vi.hoisted(() => vi.fn())

vi.mock('../../hooks/useApiQuery.ts', () => ({ useApiQuery }))

describe('HeadcountByDepartmentCard', () => {
  it('renders a zero-count department with an empty bar', () => {
    useApiQuery.mockReturnValue({
      data: { items: [{ id: 'd1', name: 'People', employeeCount: 0 }] },
      error: null,
      isLoading: false,
      isRefreshing: false,
      refetch: vi.fn(),
    })

    render(<HeadcountByDepartmentCard />)

    expect(screen.getAllByText('People')[0]).toBeInTheDocument()
    expect(screen.getAllByText('0')[0]).toBeInTheDocument()
    expect(document.querySelector('.bar-fill')).toHaveStyle({ width: '0%' })
  })

  it('renders multiple departments with readable labels and proportional values', () => {
    useApiQuery.mockReturnValue({
      data: { items: [
        { id: 'd1', name: 'Information Security and Risk Management', employeeCount: 12 },
        { id: 'd2', name: 'People', employeeCount: 3 },
      ] },
      error: null,
      isLoading: false,
      isRefreshing: false,
      refetch: vi.fn(),
    })

    render(<HeadcountByDepartmentCard />)

    expect(screen.getAllByText('Information Security and Risk Management')[0]).toBeInTheDocument()
    expect(screen.getByLabelText('Information Security and Risk Management: 12 employees')).toBeInTheDocument()
    expect(document.querySelectorAll('.bar-fill')[1]).toHaveStyle({ width: '25%' })
  })
})
