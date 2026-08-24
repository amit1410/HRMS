/** Indeterminate progress. `role="status"` so a screen reader announces the wait. */
export function Spinner({ size = 20, label }: { size?: number; label?: string }) {
  return (
    <span className="spinner-wrap" role="status" aria-live="polite">
      <span
        className="spinner"
        style={{ width: size, height: size, borderWidth: Math.max(2, Math.round(size / 8)) }}
        aria-hidden="true"
      />
      <span className={label ? 'spinner-label' : 'sr-only'}>{label ?? 'Loading…'}</span>
    </span>
  )
}

/** The whole-screen variant: session restore, and the first paint of a guarded route. */
export function FullPageSpinner({ label }: { label?: string }) {
  return (
    <div className="full-page-center">
      <Spinner size={32} label={label} />
    </div>
  )
}
