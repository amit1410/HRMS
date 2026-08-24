import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

/**
 * Test bootstrap.
 *
 * `globals: false` in the Vitest config means nothing is injected into the global scope — tests import
 * `describe`/`it`/`expect` explicitly. The trade-off is that Testing Library's automatic cleanup, which
 * hooks a global `afterEach`, never registers. It is registered here instead; without it, one test's
 * mounted tree leaks into the next and queries start matching two of everything.
 */
afterEach(() => {
  cleanup()
  window.localStorage.clear()
})
