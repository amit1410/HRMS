import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { WorkspacePickerPage } from './WorkspacePickerPage.tsx'

function renderPicker() {
  const user = userEvent.setup()
  render(
    <MemoryRouter>
      <WorkspacePickerPage />
    </MemoryRouter>,
  )
  return { user }
}

describe('WorkspacePickerPage', () => {
  it('renders the workspace address input', () => {
    renderPicker()
    expect(screen.getByLabelText(/workspace address/i)).toBeInTheDocument()
  })

  it('renders the submit button', () => {
    renderPicker()
    expect(screen.getByRole('button', { name: /go to workspace/i })).toBeInTheDocument()
  })

  it('shows an error when the input is not a valid workspace label', async () => {
    const { user } = renderPicker()
    const input = screen.getByLabelText(/workspace address/i)

    await user.type(input, 'not a valid label with spaces!')
    await user.click(screen.getByRole('button', { name: /go to workspace/i }))

    expect(screen.getByRole('alert')).toBeInTheDocument()
  })

  it('navigates to the workspace URL on valid input', async () => {
    const { user } = renderPicker()
    const input = screen.getByLabelText(/workspace address/i)

    await user.type(input, 'demo01')
    await user.click(screen.getByRole('button', { name: /go to workspace/i }))

    expect(window.location.href).toContain('demo01.localhost')
  })

  it('clears the error when the user starts typing again', async () => {
    const { user } = renderPicker()
    const input = screen.getByLabelText(/workspace address/i)

    await user.type(input, 'not a valid label with spaces!')
    await user.click(screen.getByRole('button', { name: /go to workspace/i }))
    expect(screen.getByRole('alert')).toBeInTheDocument()

    await user.type(screen.getByLabelText(/workspace address/i), 'a')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('accepts a pasted full workspace URL', async () => {
    const { user } = renderPicker()
    const input = screen.getByLabelText(/workspace address/i)
    await user.click(input)
    await user.paste('demo01.hrms.com')
    await user.click(screen.getByRole('button', { name: /go to workspace/i }))

    expect(window.location.href).toContain('demo01.')
  })
})
