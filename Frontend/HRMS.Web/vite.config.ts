/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * Dev server runs on 5173, which is the origin the API's CORS policy allows
 * (`Cors:AllowedOrigins` in `Backend/HRMS.API/appsettings.json`). `strictPort` makes a port
 * clash fail loudly instead of silently moving to 5174, where every request would be blocked
 * by CORS and look like a broken API.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/main.tsx', 'src/vite-env.d.ts'],
    },
  },
})
