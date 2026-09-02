import { api, request } from './client.ts'
import type { ApiResponse, TenantBranding } from './types.ts'

/**
 * Tenant endpoints a client needs before it has a token.
 *
 * There is exactly one, and nothing in it names an organization: the host the request leaves from
 * decides whose branding comes back — see `apiOrigin.ts` for how the base URL follows the address
 * bar. The endpoint answers `200` with {@link TenantBranding} whatever the address is; an unknown,
 * inactive or opted-out organization all produce the same all-null payload rather than a `404`,
 * so callers cannot probe which addresses belong to somebody.
 */

export function fetchCurrentTenantBranding(signal?: AbortSignal): Promise<TenantBranding> {
  return request<TenantBranding>(() =>
    api.get<ApiResponse<TenantBranding>>('/api/tenants/current/branding', { signal }),
  )
}

/**
 * Whether a branding response carries nothing to show — every field absent and no SSO advertised.
 *
 * This mirrors the API's `TenantBrandingDto.Neutral` exactly, including `ssoEnabled: false`: any
 * single field present means the organization did publish something, even if the rest is null.
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
