/// <reference types="vite/client" />

/**
 * Declared explicitly so a typo in an environment variable is a compile error rather than
 * `undefined` reaching the axios base URL, where it turns into requests against the dev server's
 * own origin and 404s that look like routing bugs.
 */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string

  /**
   * Origin template for workspace-aware API routing, e.g. `http://{workspace}.localhost:5080`.
   * When set, the label in the current address picks the API origin; when absent, requests keep
   * going to `VITE_API_BASE_URL`.
   */
  readonly VITE_API_ORIGIN_TEMPLATE?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
