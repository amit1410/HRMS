import { api, request } from './client.ts'
import type { MyEmployeeProfile } from './types.ts'

export function getMyProfile(signal?: AbortSignal): Promise<MyEmployeeProfile> {
  return request(() => api.get('/api/me/profile', { signal }))
}
