const developmentSources = [
  'http://platform.localhost:5080',
  'http://*.localhost:5080',
  'ws://platform.localhost:5173',
  'ws://*.localhost:5173',
] as const

/**
 * Returns the explicitly configured CSP destinations, or the narrow local-development destinations.
 * Production has no implicit localhost allowance: deployments must provide their real API/HMR origins
 * through VITE_API_CSP_CONNECT_SRC when a CSP replacement is required.
 */
export function cspConnectSources(mode: string, configured: readonly string[]): readonly string[] {
  if (configured.length > 0) {
    return mode === 'development'
      ? [...configured, ...developmentSources.filter((source) => source.startsWith('ws://'))]
      : configured
  }
  return mode === 'development' ? developmentSources : []
}
