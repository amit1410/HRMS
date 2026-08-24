import { useEffect } from 'react'

const SUFFIX = 'HRMS'

/** Sets the tab title, restoring the previous one on unmount so a back-navigation is not left stale. */
export function useDocumentTitle(title: string): void {
  useEffect(() => {
    const previous = document.title
    document.title = title ? `${title} · ${SUFFIX}` : SUFFIX
    return () => {
      document.title = previous
    }
  }, [title])
}
