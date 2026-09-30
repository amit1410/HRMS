import axios from 'axios'
import { request } from './client.ts'
import { platformSession, platformSessionExpiredEvent } from '../auth/platformSession.ts'

const configured = (import.meta.env.VITE_PLATFORM_API_BASE_URL ?? '').replace(/\/+$/, '')
function baseUrl() { return configured || `${window.location.protocol}//platform.localhost:5080` }

// Tenant creation provisions a database and applies the complete EF migration chain. That can
// legitimately take longer than an ordinary API request, especially on MySQL or when the server is
// contending for a metadata lock. The browser cancellation token is propagated to the API, so a
// short client timeout would cancel the migration itself and leave the tenant inactive/partially
// provisioned. Keep this separate from the normal tenant API timeout.
export const PLATFORM_PROVISIONING_TIMEOUT_MS = 5 * 60_000

export const platformApi = axios.create({
  headers: { 'Content-Type': 'application/json' },
  timeout: PLATFORM_PROVISIONING_TIMEOUT_MS,
})
platformApi.interceptors.request.use((config) => {
  config.baseURL = baseUrl()
  const token = platformSession.getAccessToken()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})
platformApi.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401 && !String(error?.config?.url ?? '').endsWith('/api/platform/auth/login')) {
      platformSession.clear()
      window.dispatchEvent(new Event(platformSessionExpiredEvent))
    }
    return Promise.reject(error)
  },
)
export { request }
