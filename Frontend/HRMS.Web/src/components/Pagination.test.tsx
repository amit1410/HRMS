import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Pagination, pageWindow, type PageInfo } from './Pagination.tsx'
import { paged } from '../test/fixtures.ts'

/** A `PagedResult`'s counts, derived the way the API derives them, so the flags cannot contradict them. */
function info(overrides: Partial<PageInfo>): PageInfo {
  return paged([], overrides)
}

describe('pageWindow', () => {
  it('lists every page while they still fit', () => {
    expect(pageWindow(1, 1)).toEqual([1])
    expect(pageWindow(3, 7)).toEqual([1, 2, 3, 4, 5, 6, 7])
  })

  it('keeps the first, the last and the current with its neighbours', () => {
    expect(pageWindow(5, 20)).toEqual([1, 'gap', 4, 5, 6, 'gap', 20])
  })

  it('does not open a gap where there is nothing to skip', () => {
    // Page 3 of 20: 1, 2, 3, 4 are consecutive, so a "…" between 1 and 2 would stand for nothing.
    expect(pageWindow(3, 20)).toEqual([1, 2, 3, 4, 'gap', 20])
    expect(pageWindow(19, 20)).toEqual([1, 'gap', 18, 19, 20])
  })

  it('stays within the range at either end', () => {
    expect(pageWindow(1, 20)).toEqual([1, 2, 'gap', 20])
    expect(pageWindow(20, 20)).toEqual([1, 'gap', 19, 20])
  })

  it('caps the row rather than growing with the result set', () => {
    expect(pageWindow(250, 500).length).toBeLessThanOrEqual(7)
  })
})

describe('Pagination', () => {
  it('says which rows these are, not just which page', () => {
    render(<Pagination info={info({ page: 3, pageSize: 20, totalCount: 137 })} onPageChange={vi.fn()} />)

    // The page number alone does not answer "how much is there".
    expect(screen.getByRole('navigation', { name: 'Pagination' })).toHaveTextContent(
      'Showing 41–60 of 137',
    )
  })

  it('stops the range at the last row of a partial final page', () => {
    render(<Pagination info={info({ page: 7, pageSize: 20, totalCount: 137 })} onPageChange={vi.fn()} />)

    expect(screen.getByRole('navigation')).toHaveTextContent('Showing 121–137 of 137')
  })

  it('groups the thousands, because 1234567 is not a readable count', () => {
    render(<Pagination info={info({ page: 1, pageSize: 20, totalCount: 4321 })} onPageChange={vi.fn()} />)

    expect(screen.getByRole('navigation')).toHaveTextContent('Showing 1–20 of 4,321')
  })

  it('renders nothing at all when there is nothing to page through', () => {
    // The empty state has already said so; a "Showing 0–0 of 0" underneath it would be noise.
    render(<Pagination info={info({ totalCount: 0 })} onPageChange={vi.fn()} />)

    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('announces the current page rather than only colouring it', () => {
    render(<Pagination info={info({ page: 2, pageSize: 10, totalCount: 45 })} onPageChange={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Page 3' })).not.toHaveAttribute('aria-current')
  })

  it('offers no step off either end', async () => {
    const onPageChange = vi.fn()
    const { unmount } = render(
      <Pagination info={info({ page: 1, pageSize: 10, totalCount: 25 })} onPageChange={onPageChange} />,
    )

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(onPageChange).toHaveBeenCalledWith(2)

    unmount()
    render(
      <Pagination info={info({ page: 3, pageSize: 10, totalCount: 25 })} onPageChange={onPageChange} />,
    )

    // `hasNextPage` comes from the API: 25 rows over 10 is three pages, and the third has five of them.
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled()
  })

  it('takes the last page of an exactly divisible set as the last page', () => {
    render(<Pagination info={info({ page: 2, pageSize: 20, totalCount: 40 })} onPageChange={vi.fn()} />)

    // `items.length === pageSize` would have looked like there was one more.
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })

  it('changes the page size when one is offered', async () => {
    const onPageSizeChange = vi.fn()
    render(
      <Pagination
        info={info({ page: 1, pageSize: 20, totalCount: 137 })}
        onPageChange={vi.fn()}
        onPageSizeChange={onPageSizeChange}
      />,
    )

    await userEvent.selectOptions(screen.getByLabelText('Rows'), '50')
    expect(onPageSizeChange).toHaveBeenCalledWith(50)
  })

  it('leaves the size control out when the caller cannot honour it', () => {
    render(<Pagination info={info({ totalCount: 137 })} onPageChange={vi.fn()} />)

    expect(screen.queryByLabelText('Rows')).not.toBeInTheDocument()
  })

  it('takes no clicks while a reload is in flight', async () => {
    const onPageChange = vi.fn()
    render(
      <Pagination
        info={info({ page: 2, pageSize: 10, totalCount: 45 })}
        onPageChange={onPageChange}
        onPageSizeChange={vi.fn()}
        disabled
      />,
    )

    // A second click would queue a page for rows the user never saw.
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Page 3' })).toBeDisabled()
    expect(screen.getByLabelText('Rows')).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Page 3' }))
    expect(onPageChange).not.toHaveBeenCalled()
  })
})
