import { Component, type ErrorInfo, type ReactNode } from 'react'

interface State {
  error: Error | null
}

/**
 * Last resort for a render-time crash.
 *
 * Without one, a thrown error unmounts the whole tree and leaves a blank page — which looks identical
 * to the app failing to load at all. This keeps the shell on screen and gives the user a way out.
 *
 * It does not attempt to report anywhere: there is no client telemetry sink in this system, and the
 * error may carry whatever was on screen, which for an HR application is exactly the sort of thing not
 * to ship off to a third party by default.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  override state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // The console is the one place a developer will look; in production this is all that is kept.
    console.error('Unhandled UI error', error, info.componentStack)
  }

  override render(): ReactNode {
    const { error } = this.state
    if (!error) return this.props.children

    return (
      <div className="full-page-center">
        <div className="state-block state-page" role="alert">
          <p className="state-title">Something went wrong</p>
          <p className="state-message">
            The screen could not be displayed. Reloading usually clears it.
          </p>
          <div className="state-action">
            <button
              type="button"
              className="button button-primary"
              onClick={() => window.location.reload()}
            >
              Reload
            </button>
          </div>
        </div>
      </div>
    )
  }
}
