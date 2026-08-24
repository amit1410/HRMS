import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react'

interface ModalProps {
  title: string
  /** Called for Escape, the backdrop, and the close button alike. */
  onClose: () => void
  /** Footer slot, in reading order — the confirming action last, where a mouse expects it. */
  footer?: ReactNode
  /** `alertdialog` when the dialog interrupts to demand an answer, e.g. a delete confirmation. */
  role?: 'dialog' | 'alertdialog'
  /** Element to focus on open. Defaults to the dialog itself, which is never a destructive button. */
  initialFocus?: RefObject<HTMLElement | null>
  /** Id of the element describing the dialog, for `aria-describedby`. */
  describedById?: string
  children: ReactNode
}

/**
 * A modal dialog.
 *
 * Built from a plain `div` rather than the native `<dialog>` element, for two reasons: `showModal()` is
 * only partially implemented in jsdom, so every test touching a confirmation would need a polyfill; and
 * the top-layer backdrop is awkward to style consistently. The cost is that the behaviour the browser
 * would have provided has to be written out — which is what the rest of this file is.
 *
 * What that behaviour is:
 *
 * - **Escape and the backdrop close it.** A confirmation the user cannot back out of is a trap.
 * - **Focus moves in and comes back.** On open, focus lands inside; on close, it returns to whatever had
 *   it before — usually the row's own Delete button, so the keyboard user is not sent back to the top of
 *   the page.
 * - **Tab cycles within.** Without this the tab order walks out of an `aria-modal` dialog into content a
 *   screen reader has been told is hidden.
 * - **The page behind does not scroll.** Otherwise a wheel over the backdrop moves the list underneath.
 */
export function Modal({
  title,
  onClose,
  footer,
  role = 'dialog',
  initialFocus,
  describedById,
  children,
}: ModalProps) {
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)

  // `onClose` is read through a ref so the key handler is installed once. Re-binding on every render
  // would be harmless but the listener is genuinely global, and one that is repeatedly added and removed
  // is harder to reason about than one that is not.
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    ;(initialFocus?.current ?? dialogRef.current)?.focus()

    const { overflow } = document.body.style
    document.body.style.overflow = 'hidden'

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onCloseRef.current()
        return
      }
      if (event.key === 'Tab') {
        trapTab(event, dialogRef.current)
      }
    }

    document.addEventListener('keydown', onKeyDown, true)

    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      document.body.style.overflow = overflow
      previouslyFocused?.focus()
    }
  }, [initialFocus])

  return (
    <div
      className="modal-backdrop"
      // Mouse *down* rather than click: a click that started inside the dialog and finished on the
      // backdrop — a text selection dragged too far — would otherwise dismiss it.
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        className="modal"
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={describedById}
        tabIndex={-1}
        ref={dialogRef}
      >
        <header className="modal-header">
          <h2 className="modal-title" id={titleId}>
            {title}
          </h2>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Close">
            ×
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer !== undefined && <footer className="modal-footer">{footer}</footer>}
      </div>
    </div>
  )
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/** Keeps Tab and Shift+Tab inside `container`, wrapping at either end. */
function trapTab(event: KeyboardEvent, container: HTMLElement | null): void {
  if (!container) return

  const focusable = [...container.querySelectorAll<HTMLElement>(FOCUSABLE)]
  const first = focusable[0]
  const last = focusable[focusable.length - 1]

  // Nothing to move to, so there is nowhere for Tab to go but out — which is what we are preventing.
  if (!first || !last) {
    event.preventDefault()
    return
  }

  const active = document.activeElement
  if (event.shiftKey && (active === first || active === container)) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && active === last) {
    event.preventDefault()
    first.focus()
  }
}
