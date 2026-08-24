import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/errors.ts'
import { ConfirmDialog } from './ConfirmDialog.tsx'

type DialogProps = ComponentProps<typeof ConfirmDialog>

function renderDialog(overrides: Partial<DialogProps> = {}) {
  const props: DialogProps = {
    title: 'Delete department?',
    message: <strong>Engineering</strong>,
    onConfirm: vi.fn().mockResolvedValue(undefined),
    onClose: vi.fn(),
    ...overrides,
  }
  render(<ConfirmDialog {...props} />)
  return { onConfirm: props.onConfirm, onClose: props.onClose }
}

describe('ConfirmDialog', () => {
  it('interrupts, and names the record rather than "this item"', () => {
    renderDialog({ hint: 'A department with employees cannot be deleted.' })

    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveAccessibleName('Delete department?')
    expect(dialog).toHaveAccessibleDescription('Engineering')
    // The likely refusal is said before the request, not after it comes back.
    expect(screen.getByText('A department with employees cannot be deleted.')).toBeInTheDocument()
  })

  it('opens with Cancel focused, not the destructive button', () => {
    renderDialog()

    // A dialog that deletes on a reflexive Enter is worse than no confirmation at all.
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  })

  it('closes once the action succeeds', async () => {
    const { onConfirm, onClose } = renderDialog()

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('keeps the question open and shows what the server said when the delete is refused', async () => {
    const onConfirm = vi
      .fn()
      .mockRejectedValue(
        new ApiError('Engineering has 12 employees assigned to it.', { status: 409 }),
      )
    const { onClose } = renderDialog({ onConfirm })

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    // The explanation belongs where the user is looking, not as a banner behind a closed dialog.
    expect(await screen.findByText('Engineering has 12 employees assigned to it.')).toBeInTheDocument()
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('cannot be answered twice while the first answer is in flight', async () => {
    const onConfirm = vi.fn().mockReturnValue(new Promise<void>(() => undefined))
    renderDialog({ onConfirm })

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Working…'))
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('backs out on Cancel, Escape, the close button and the backdrop alike', async () => {
    const { onConfirm, onClose } = renderDialog()

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await userEvent.keyboard('{Escape}')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    // The backdrop, which is the parent of the dialog itself.
    await userEvent.click(screen.getByRole('alertdialog').parentElement as HTMLElement)

    expect(onClose).toHaveBeenCalledTimes(4)
    // Nothing was deleted on the way out.
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('is not dismissible while the delete is running', async () => {
    const onConfirm = vi.fn().mockReturnValue(new Promise<void>(() => undefined))
    const { onClose } = renderDialog({ onConfirm })

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled())

    // Escape and the backdrop are stopped too: the request is already on its way to the server.
    await userEvent.keyboard('{Escape}')
    await userEvent.click(screen.getByRole('alertdialog').parentElement as HTMLElement)

    expect(onClose).not.toHaveBeenCalled()
  })

  it('takes the caller’s wording for a confirmation that is not a delete', () => {
    renderDialog({ confirmLabel: 'Deactivate', cancelLabel: 'Keep active' })

    expect(screen.getByRole('button', { name: 'Deactivate' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Keep active' })).toBeInTheDocument()
  })
})
