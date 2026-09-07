export function isPlatformHost(hostname: string): boolean {
  const configured = (import.meta.env.VITE_PLATFORM_HOSTS ?? 'platform.localhost')
    .split(',').map((host: string) => host.trim().toLowerCase()).filter(Boolean)
  return configured.includes(hostname.toLowerCase())
}
