import axios from 'axios'
import { request } from './client.ts'
import { platformSession, platformSessionExpiredEvent } from '../auth/platformSession.ts'

const configured = (import.meta.env.VITE_PLATFORM_API_BASE_URL ?? '').replace(/\/+$/, '')
function baseUrl() { return configured || `${window.location.protocol}//platform.localhost:5080` }
export const platformApi = axios.create({ headers: { 'Content-Type': 'application/json' }, timeout: 30_000 })
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
