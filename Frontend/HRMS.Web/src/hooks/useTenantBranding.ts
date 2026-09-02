import { fetchCurrentTenantBranding, isNeutralBranding } from '../api/tenants.ts'
import type { ApiError } from '../api/errors.ts'
import type { TenantBranding } from '../api/types.ts'
import { useApiQuery } from './useApiQuery.ts'

export interface TenantBrandingResult {
  /**
   * The branding published for the organization at the current address, or `null` while the first
   * load is in flight or after a failure — the two states a sign-in screen cannot render.
   */
  branding: TenantBranding | null
  /** True when a response arrived but carries nothing to personalize: no name, logo, colours, SSO. */
  isNeutral: boolean
  isLoading: boolean
  isRefreshing: boolean
  error: ApiError | null
  refetch: () => void
}

/**
 * Reads the tenant branding for the current address once per consumer.
 *
 * Deliberately local state, not a store: the only screen that needs it is the one at the address,
 * the answer cannot change without a full navigation (a different workspace is a different origin
 * and therefore a fresh page), and caching it would outlive exactly the boundary it describes.
 */
export function useTenantBranding(): TenantBrandingResult {
  const query = useApiQuery((signal) => fetchCurrentTenantBranding(signal), [])

  return {
    branding: query.data,
    // A neutral response is still success — the hook hands over the payload and flags it, so the
    // caller renders an unbranded screen rather than an error for an address nobody publishes.
    isNeutral: query.data !== null && isNeutralBranding(query.data),
    isLoading: query.isLoading,
    isRefreshing: query.isRefreshing,
    error: query.error,
    refetch: query.refetch,
  }
}
