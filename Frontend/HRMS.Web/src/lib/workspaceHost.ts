/**
 * Reading a workspace out of the address bar, and building one back up.
 *
 * A workspace is **one** DNS label in front of a fixed base domain — `demo01.hrms.example`, never
 * `a.b.hrms.example`, and never the base domain on its own. That rule is the tenancy boundary as this
 * side of the wire sees it: the label the browser is at decides which database the API opens, so the
 * client has to agree with the server about what counts as a label.
 *
 * It mirrors `Backend/HRMS.API/Security/WorkspaceOriginPattern.cs`, which applies the same rule to CORS
 * origins, and it is deliberately structural for the same reason. A regular expression over a whole URL
 * has to get anchoring, dot-escaping and the label character class all right at once, and every way of
 * getting one of them wrong is silent: an unescaped `hrms.example` also matches `hrmsXexample`, and an
 * unanchored one also matches `demo01.hrms.example.attacker.test`.
 *
 * Nothing here reads configuration or `window`. It is string handling, which is what lets the cases that
 * actually matter — a nested label, a trailing dot, the base domain itself, a pasted URL — be tested
 * without a browser or a build.
 */

/** What a configured origin template writes where the workspace label goes. */
export const WORKSPACE_PLACEHOLDER = '{workspace}'

/** The longest a DNS label may be. */
const MAX_LABEL_LENGTH = 63

/** Letters, digits and inner hyphens: a hostname label, already lowercased. */
const LABEL_PATTERN = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/

/**
 * Whether `value` is a single hostname label. Notably false for anything containing a dot, which is what
 * keeps `a.b` from being accepted as one workspace named `a.b`.
 */
export function isWorkspaceLabel(value: string): boolean {
  return value.length <= MAX_LABEL_LENGTH && LABEL_PATTERN.test(value)
}

/**
 * The workspace label in `hostname`, or `null` when there is none — which covers the apex (`hostname` is
 * the base domain), a nested host, and a host in some other domain entirely.
 *
 * `null` is not an error. It is the apex answer, and the apex is a real screen: it is where a visitor who
 * typed the bare product address goes to say which workspace they want.
 */
export function workspaceLabelFor(hostname: string, baseDomain: string): string | null {
  // A fully-qualified name may end in a dot, and `demo01.hrms.example.` is the same host as
  // `demo01.hrms.example`. Comparing without normalising would send that visitor to the apex.
  const host = hostname.trim().toLowerCase().replace(/\.+$/, '')
  const base = baseDomain.trim().toLowerCase().replace(/^\.+|\.+$/g, '')
  if (!host || !base) return null

  const suffix = `.${base}`
  if (!host.endsWith(suffix)) return null

  const label = host.slice(0, -suffix.length)
  return isWorkspaceLabel(label) ? label : null
}

/** A parsed `VITE_API_ORIGIN_TEMPLATE`, split at the label so either origin can be rebuilt from it. */
export interface OriginTemplate {
  /** Everything before the label: scheme and separator, `http://`. */
  prefix: string
  /** Everything after the label's dot, port included: `localhost:5080`. */
  suffix: string
  /** {@link suffix} without its port — the domain a workspace label sits in front of. */
  baseDomain: string
}

/**
 * Parses `http://{workspace}.localhost:5080` into its two halves, or returns `null` when the template is
 * not that shape.
 *
 * The placeholder must be the host's **first label**, with a scheme in front of it and a dot behind it.
 * A placeholder anywhere else would substitute into part of a label, which is exactly how a base domain
 * of `hrms.example` comes to serve `evil-hrms.example`.
 */
export function parseOriginTemplate(template: string): OriginTemplate | null {
  const parts = template.trim().split(WORKSPACE_PLACEHOLDER)

  // Exactly one placeholder. A second one would land inside the base domain, giving a template that
  // resolves to a host nobody serves while reading as though it should work.
  if (parts.length !== 2) return null

  const [prefix = '', rest = ''] = parts
  if (!prefix.endsWith('://') || !rest.startsWith('.')) return null

  const suffix = rest.slice(1).replace(/\/+$/, '').toLowerCase()
  const baseDomain = suffix.split(':', 1)[0] ?? ''
  if (!baseDomain) return null

  return { prefix: prefix.toLowerCase(), suffix, baseDomain }
}

/** The origin serving `label`'s workspace. */
export function originForWorkspace(template: OriginTemplate, label: string): string {
  return `${template.prefix}${label}.${template.suffix}`
}

/** The origin with no workspace label at all — the apex, where no organization is resolved. */
export function apexOrigin(template: OriginTemplate): string {
  return `${template.prefix}${template.suffix}`
}

/**
 * Reduces whatever a visitor typed into the workspace picker to a label, or `null` when it is not one.
 *
 * People paste. `demo01`, `demo01.hrms.example`, `https://demo01.hrms.example/login` and
 * `HTTPS://DEMO01.hrms.example` are all the same answer to "which workspace", and refusing three of them
 * on a screen whose only job is to get someone to their own sign-in page would be pedantry. What is *not*
 * accepted is anything that fails {@link isWorkspaceLabel} once the decoration is stripped.
 */
export function toWorkspaceLabel(input: string): string | null {
  let value = input.trim().toLowerCase()
  if (!value) return null

  value = value.replace(/^[a-z][a-z0-9+.-]*:\/\//, '')
  // Path, query and fragment, then userinfo, then port — in that order, so a `@` or `:` inside a path
  // cannot be mistaken for either.
  value = value.split(/[/?#]/, 1)[0] ?? ''
  value = value.split('@').pop() ?? ''
  value = value.split(':', 1)[0] ?? ''

  const label = value.split('.', 1)[0] ?? ''
  return isWorkspaceLabel(label) ? label : null
}

/** The parts of `window.location` the picker needs, so it can be given them rather than reach for them. */
export interface OriginParts {
  protocol: string
  hostname: string
  port: string
}

/**
 * Where the workspace picker sends someone: the same scheme and port as the page they are on, with their
 * label in front of the host they typed it at.
 *
 * The base domain comes from `location.hostname` rather than from configuration on purpose. The picker
 * only renders when the current address has no workspace label, so the address *is* the base domain —
 * and if configuration and reality disagree, the address the visitor actually reached is the one that
 * demonstrably resolves.
 */
export function workspaceUrl(input: string, location: OriginParts): string | null {
  const label = toWorkspaceLabel(input)
  if (label === null) return null

  const host = location.hostname.trim().toLowerCase().replace(/\.+$/, '')
  if (!host) return null

  const port = location.port ? `:${location.port}` : ''
  return `${location.protocol}//${label}.${host}${port}/login`
}
