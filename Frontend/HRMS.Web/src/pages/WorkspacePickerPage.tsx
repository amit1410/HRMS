import { useState, type FormEvent } from 'react'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { toWorkspaceLabel, workspaceUrl } from '../lib/workspaceHost.ts'

/**
 * Workspace-address picker, shown only on the apex host (the base domain with no workspace label).
 *
 * A visitor who types `hrms.com` has not yet said which organization they belong to. This page
 * gives them a single input — their own workspace address — and navigates to it. No API call is
 * made, so no organization name is confirmed or denied. The user types what Slack and Atlassian
 * already taught them to type: their own company's address.
 */
export function WorkspacePickerPage() {
  useDocumentTitle('Find your workspace')

  const [input, setInput] = useState('')
  const [error, setError] = useState<string | null>(null)

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const label = toWorkspaceLabel(input)
    if (label === null) {
      setError('That doesn\u2019t look like a workspace address. Try something like "demo01".')
      return
    }

    const url = workspaceUrl(input, {
      protocol: window.location.protocol,
      hostname: window.location.hostname,
      port: window.location.port,
    })
    if (url === null) {
      setError('Could not build a workspace URL. Please check the address and try again.')
      return
    }

    window.location.href = url
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <span className="sidebar-mark login-mark" aria-hidden="true">
            HR
          </span>
          <div>
            <h1 className="login-title">HRMS</h1>
            <p className="login-subtitle">Sign in to your workspace</p>
          </div>
        </div>

        <form onSubmit={onSubmit} noValidate>
          <div className="field">
            <label htmlFor="workspace" className="field-label">
              Workspace address
            </label>
            <input
              id="workspace"
              className="input"
              type="text"
              placeholder="e.g. demo01"
              value={input}
              onChange={(event) => {
                setInput(event.target.value)
                setError(null)
              }}
              autoComplete="off"
              autoFocus
            />
            {error && (
              <p className="field-error" role="alert">
                {error}
              </p>
            )}
          </div>

          <button type="submit" className="button button-primary button-block">
            Go to workspace
          </button>
        </form>
      </div>
    </div>
  )
}
