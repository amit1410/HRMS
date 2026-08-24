import { useRef, useState, type ReactNode } from 'react'
import { toApiError, type ApiError } from '../api/errors.ts'
import { Modal } from './Modal.tsx'
import { Spinner } from './Spinner.tsx'

interface ConfirmDialogProps {
  title: string
  /** What is about to happen, naming the specific record — never just "this item". */
  message: ReactNode
  /** Anything worth knowing before answering: what is kept, what cannot be undone. */
  hint?: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  /** Resolves when the action succeeded; rejects with an {@link ApiError} the dialog then displays. */
  onConfirm: () => Promise<void>
  onClose: () => void
}

/**
 * "Are you sure?", with the answer's failure handled where the question was asked.
 *
 * The reason this owns its own error state rather than reporting upward: the API refuses a delete with
 * 409 when the row is still referenced — a department with employees, a manager with reports — and that
 * message is the most useful thing on the screen. Closing the dialog and showing it as a banner above
 * the table would move the explanation away from where the user is looking, and would make "did anything
 * happen?" a question. So a refusal keeps the dialog open with the server's own wording inside it, and
 * only a success closes it.
 */
export function ConfirmDialog({
  title,
  message,
  hint,
  confirmLabel = 'Delete',
  cancelLabel = 'Cancel',
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  // Opening focus goes to Cancel, not to the destructive button: a dialog that deletes on a reflexive
  // Enter is worse than no confirmation at all.
  const cancelRef = useRef<HTMLButtonElement>(null)

  async function confirm() {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
      onClose()
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={title}
      role="alertdialog"
      onClose={busy ? () => undefined : onClose}
      initialFocus={cancelRef}
      describedById="confirm-dialog-message"
      footer={
        <>
          <button
            type="button"
            className="button button-secondary"
            onClick={onClose}
            disabled={busy}
            ref={cancelRef}
          >
            {cancelLabel}
          </button>
          <button type="button" className="button button-danger" onClick={confirm} disabled={busy}>
            {busy ? <Spinner size={14} label="Working…" /> : confirmLabel}
          </button>
        </>
      }
    >
      <p id="confirm-dialog-message">{message}</p>
      {hint !== undefined && <p className="modal-hint">{hint}</p>}
      {error && (
        <p className="form-error" role="alert">
          {error.message}
        </p>
      )}
    </Modal>
  )
}
