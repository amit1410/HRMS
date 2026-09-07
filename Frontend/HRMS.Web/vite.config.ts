/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { applyAcceptanceConnectSrc } from './src/lib/acceptanceCsp.js'
import { cspConnectSources } from './src/lib/cspConnectSources.js'

function cspPlugin(mode: string, configured: readonly string[]) {
  return {
    name: 'hrms-csp',
    transformIndexHtml(html: string) {
      return applyAcceptanceConnectSrc(html, cspConnectSources(mode, configured))
    },
  }
}

/**
 * Dev server runs on 5173, which is the origin the API's CORS policy allows
 * (`Cors:AllowedOrigins` in `Backend/HRMS.API/appsettings.json`). `strictPort` makes a port
 * clash fail loudly instead of silently moving to 5174, where every request would be blocked
 * by CORS and look like a broken API.
 */
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const configured = (env.VITE_API_CSP_CONNECT_SRC ?? process.env.VITE_API_CSP_CONNECT_SRC ?? '').split(/\s+/).filter(Boolean)

  return {
  plugins: [react(), cspPlugin(mode, configured)],
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    environmentOptions: {
      jsdom: {
        url: 'http://demo01.localhost:5173',
      },
    },
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/main.tsx', 'src/vite-env.d.ts'],
    },
  },
  }
})
