const refreshTokenKey = 'hrms.platform.refreshToken.v1'
export const platformSessionExpiredEvent = 'hrms-platform-session-expired'
let accessToken: string | null = null

function storage(): Storage | null {
  try { return window.localStorage } catch { return null }
}

export const platformSession = {
  getAccessToken: () => accessToken,
  getRefreshToken: () => storage()?.getItem(refreshTokenKey) ?? null,
  save: (tokens: { accessToken: string; refreshToken: string }) => {
    accessToken = tokens.accessToken
    storage()?.setItem(refreshTokenKey, tokens.refreshToken)
  },
  clear: () => { accessToken = null; storage()?.removeItem(refreshTokenKey) },
  hasStoredSession: () => Boolean(storage()?.getItem(refreshTokenKey)),
  refreshTokenKey,
}
