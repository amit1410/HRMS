import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { renderAsUser } from '../test/renderWith.tsx'
import { MasterDropdown } from './MasterDropdown.tsx'

const active = { id: 'active-1', code: 'AC', name: 'Active Master', isActive: true }
const inactive = { id: 'inactive-1', code: 'IN', name: 'Inactive Master', isActive: false }
const otherInactive = { id: 'inactive-2', code: 'IN2', name: 'Other Inactive Master', isActive: false }

describe('MasterDropdown', () => {
  it('fetches active records only by default', async () => {
    const fetcher = vi.fn().mockResolvedValue([active])
    renderAsUser(<MasterDropdown id="master" label="Master" value="" onChange={() => undefined} fetcher={fetcher} />)

    await userEvent.click(await screen.findByLabelText('Master'))
    expect(await screen.findByRole('option', { name: 'AC - Active Master' })).toBeInTheDocument()
    expect(fetcher).toHaveBeenCalledWith({ parentId: undefined, isActive: true }, expect.anything())
  })

  it('fetches active and inactive records when includeInactive is enabled', async () => {
    const fetcher = vi.fn().mockResolvedValue([active, inactive, otherInactive])
    renderAsUser(<MasterDropdown id="master" label="Master" value="" onChange={() => undefined} fetcher={fetcher} includeInactive />)

    await userEvent.click(await screen.findByLabelText('Master'))
    expect(await screen.findByRole('option', { name: 'AC - Active Master' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /^IN - Inactive Master/ })).toBeInTheDocument()
    expect(fetcher).toHaveBeenCalledWith({ parentId: undefined, isActive: undefined }, expect.anything())
  })

  it('keeps an existing inactive value visible but blocks selecting a different inactive value', async () => {
    const onChange = vi.fn()
    const fetcher = vi.fn().mockResolvedValue([active, inactive, otherInactive])
    renderAsUser(<MasterDropdown id="master" label="Master" value={inactive.id} onChange={onChange} fetcher={fetcher} includeInactive />)

    const input = await screen.findByLabelText('Master')
    expect(input).toHaveValue('IN - Inactive Master')
    await userEvent.click(input)
    await userEvent.click(screen.getByRole('option', { name: 'AC - Active Master' }))
    expect(onChange).toHaveBeenCalledWith(active.id)

    onChange.mockClear()
    await userEvent.keyboard('{ArrowDown}')
    await userEvent.click(screen.getByRole('option', { name: /IN2.*Other Inactive Master/ }))
    expect(onChange).not.toHaveBeenCalled()
  })
})
