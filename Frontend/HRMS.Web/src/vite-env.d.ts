/// <reference types="vite/client" />

/**
 * Declared explicitly so a typo in an environment variable is a compile error rather than
 * `undefined` reaching the axios base URL, where it turns into requests against the dev server's
 * own origin and 404s that look like routing bugs.
 */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
