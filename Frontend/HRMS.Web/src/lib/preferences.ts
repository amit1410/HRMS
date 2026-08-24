/**
 * Small, non-sensitive UI preferences. Anything here is readable by any script on the origin, so it
 * holds nothing but conveniences — never a credential, never a permission.
 */

const LAST_TENANT_KEY = 'hrms.lastTenantCode.v1'

function storage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    // Private modes and blocked third-party storage throw on access rather than returning null.
    return null
  }
}

/** The tenant code used at the last successful sign-in, so returning users need not retype it. */
export function getLastTenantCode(): string {
  try {
    return storage()?.getItem(LAST_TENANT_KEY) ?? ''
  } catch {
    return ''
  }
}

export function setLastTenantCode(code: string): void {
  try {
    const trimmed = code.trim()
    if (trimmed) storage()?.setItem(LAST_TENANT_KEY, trimmed)
  } catch {
    // A remembered tenant code is not worth failing a sign-in over.
  }
}
