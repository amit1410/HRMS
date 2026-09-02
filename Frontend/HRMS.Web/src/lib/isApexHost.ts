import { isApexHost as resolveApex, type ApiOriginConfig } from './apiOrigin.ts'
import { parseOriginTemplate, type OriginTemplate } from './workspaceHost.ts'

/**
 * Whether `hostname` is the apex (base domain) of the configured workspace template — the address
 * with no workspace label in front of it.
 *
 * Returns `false` when no template is configured (non-workspace deployment) or when the hostname
 * carries a workspace label (a normal workspace page). Only `true` on the bare base domain where
 * the workspace-address picker should render.
 */
export function isApexHost(
  hostname: string,
  env: Pick<ApiOriginConfig, 'originTemplate'> = import.meta.env as unknown as ApiOriginConfig,
): boolean {
  if (!env.originTemplate) return false

  const template: OriginTemplate | null = parseOriginTemplate(env.originTemplate)
  if (!template) return false

  return resolveApex(hostname, template.baseDomain)
}
