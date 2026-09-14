/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The release, inlined at build time by the Dockerfile; an empty string on a local build. */
  readonly VITE_APP_VERSION?: string
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
