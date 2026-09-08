import { api, request } from './client.ts'
import type { ApiResponse, TenantBranding } from './types.ts'

/**
 * Tenant endpoints a client needs before it has a token.
 *
 * There is exactly one, and nothing in it names an organization: the host the request leaves from
 * decides whose branding comes back — see `apiOrigin.ts` for how the base URL follows the address
 * bar. Active tenants answer `200` with fallback values when custom branding is absent; unknown,
 * inactive, and unpublished workspaces answer `404`.
 */

export function fetchCurrentTenantBranding(signal?: AbortSignal): Promise<TenantBranding> {
  return request<TenantBranding>(() =>
    api.get<ApiResponse<TenantBranding>>('/api/tenants/current/branding', { signal }),
  )
}

/**
 * Whether a branding response carries no custom fields. This is not an existence check: a successful
 * active-tenant response may contain fallback fields or may intentionally leave optional fields absent.
 */
export function isNeutralBranding(branding: TenantBranding): boolean {
  return (
    !branding.ssoEnabled &&
    !branding.displayName &&
    !branding.logoUrl &&
    !branding.primaryColor &&
    !branding.welcomeMessage &&
    !branding.supportEmail &&
    !branding.ssoProviderName
  )
}
