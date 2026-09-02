import {
  apexOrigin,
  originForWorkspace,
  parseOriginTemplate,
  workspaceLabelFor,
} from './workspaceHost.ts'

/**
 * Which API origin a request leaves for, given the address the page is on.
 *
 * Two environment variables decide it, read per request because both inputs are per request:
 *
 * - `VITE_API_BASE_URL` is one fixed origin — the behaviour everything had before workspaces, and
 *   still what runs when no template is configured.
 * - `VITE_API_ORIGIN_TEMPLATE`, e.g. `http://{workspace}.localhost:5080`, makes routing
 *   workspace-aware: a page served at `demo01.localhost` talks to `demo01.localhost:5080`, so the
 *   API resolves the same tenant the browser's address does and its CORS policy — which allows
 *   exactly these pairs of origins — accepts the call.
 *
 * Like `workspaceHost.ts`, nothing here reads `window`: the hostname arrives as a parameter, which
 * is what keeps every case — plain localhost, the apex, a foreign host, a trailing dot — testable
 * without a browser.
 */

/** The dev default for `VITE_API_BASE_URL`; matches the API's `http` launch profile. */
const DEFAULT_BASE_URL = 'http://localhost:5080'

/** What {@link resolveApiBaseUrl} needs: the two variables, unparsed. */
export interface ApiOriginConfig {
  /** `VITE_API_BASE_URL`. */
  baseUrl?: string
  /** `VITE_API_ORIGIN_TEMPLATE`. An unparseable value falls back to {@link baseUrl}. */
  originTemplate?: string
}

/** Reads the two variables out of an `ImportMetaEnv`, so callers never touch `env` twice. */
export function apiOriginConfigFromEnv(env: {
  VITE_API_BASE_URL?: string
  VITE_API_ORIGIN_TEMPLATE?: string
}): ApiOriginConfig {
  return { baseUrl: env.VITE_API_BASE_URL, originTemplate: env.VITE_API_ORIGIN_TEMPLATE }
}

/**
 * The base URL for a page served from `hostname`.
 *
 * A hostname carrying exactly one label in front of the template's base domain routes to that
 * workspace's API origin. The apex — the base domain itself, `localhost` against
 * `{workspace}.localhost` — has no workspace to name, so it goes to the template's own origin,
 * which is the same address `VITE_API_BASE_URL` already pointed at in development. Anything else
 * (a host from another domain entirely) cannot be routed by the template at all and keeps the
 * statically-configured origin rather than guessing one.
 */
export function resolveApiBaseUrl(config: ApiOriginConfig, hostname: string): string {
  const fallback = (config.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, '')
  if (!config.originTemplate) return fallback

  const template = parseOriginTemplate(config.originTemplate)
  if (!template) return fallback

  const label = workspaceLabelFor(hostname, template.baseDomain)
  if (label !== null) return originForWorkspace(template, label)

  return isApexHost(hostname, template.baseDomain) ? apexOrigin(template) : fallback
}

/** Whether `hostname` is the template's base domain itself, with no workspace label in front. */
export function isApexHost(hostname: string, baseDomain: string): boolean {
  const normalize = (value: string) => value.trim().toLowerCase().replace(/\.+$/g, '')
  const host = normalize(hostname)
  return host !== '' && host === normalize(baseDomain)
}
