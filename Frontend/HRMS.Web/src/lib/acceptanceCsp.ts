const originPattern = /^https?:\/\/[^/\s]+$/i

/**
 * Replaces only the existing connect-src directive for the disposable browser run.
 * An empty origin list deliberately leaves the production/index policy untouched.
 */
export function applyAcceptanceConnectSrc(html: string, origins: readonly string[]): string {
  if (origins.length === 0) return html

  for (const origin of origins) {
    if (!originPattern.test(origin)) {
      throw new Error(`Acceptance CSP origin must be an absolute origin: ${origin}`)
    }

    const parsed = new URL(origin)
    if (parsed.pathname !== '/' || parsed.search || parsed.hash || parsed.username || parsed.password) {
      throw new Error(`Acceptance CSP origin must not contain a path or credentials: ${origin}`)
    }
  }

  const replacement = `connect-src 'self' ${origins.join(' ')};`
  const updated = html.replace(/connect-src\s+[^;]+;/i, replacement)
  if (updated === html) throw new Error('Acceptance CSP connect-src directive was not found.')
  return updated
}
