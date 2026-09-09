import { unwrap } from './client.ts'
import type { ApiResponse } from './types.ts'
import { platformSession } from '../auth/platformSession.ts'
import { platformApi } from './platformClient.ts'

export interface PlatformUser {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  roles: string[]
  permissions: string[]
}

export interface PlatformLoginResponse {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAtUtc: string
  expiresInSeconds: number
  user: PlatformUser
}

let platformRefreshInFlight: Promise<PlatformLoginResponse | null> | null = null

export async function platformLogin(email: string, password: string): Promise<PlatformLoginResponse> {
  const response = await platformApi.post<ApiResponse<PlatformLoginResponse>>('/api/platform/auth/login', { email, password })
  const result = unwrap(response.data, response.status)
  platformSession.save(result)
  return result
}

export function platformRefresh(): Promise<PlatformLoginResponse | null> {
  platformRefreshInFlight ??= performPlatformRefresh().finally(() => {
    platformRefreshInFlight = null
  })
  return platformRefreshInFlight
}

async function performPlatformRefresh(): Promise<PlatformLoginResponse | null> {
  const refreshToken = platformSession.getRefreshToken()
  if (!refreshToken) return null
  try {
    const response = await platformApi.post<ApiResponse<PlatformLoginResponse>>('/api/platform/auth/refresh', { refreshToken })
    const result = unwrap(response.data, response.status)
    platformSession.save(result)
    return result
  } catch { platformSession.clear(); return null }
}

export async function platformLogout(): Promise<void> {
  const refreshToken = platformSession.getRefreshToken()
  try { if (refreshToken) await platformApi.post('/api/platform/auth/logout', { refreshToken }) } finally { platformSession.clear() }
}

export async function platformMe(): Promise<PlatformUser> {
  const response = await platformApi.get<ApiResponse<PlatformUser>>('/api/platform/auth/me')
  return unwrap(response.data, response.status)
}
