import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'

/** The shape a screen puts in `navigate(..., { state })` to have the next screen announce something. */
export interface FlashState {
  flash?: string
}

export interface Flash {
  /** The message to show, or `null` when there is nothing to say. */
  message: string | null
  /** For an action that happened on this screen — a delete — with no navigation to carry a message. */
  show: (message: string) => void
  dismiss: () => void
}

/**
 * "Department created." — a message handed from the screen that did the work to the screen the user
 * lands on.
 *
 * It travels in the router's history state rather than a query parameter or a shared store. A query
 * parameter would survive a bookmark and re-announce a save that happened yesterday; a store would need
 * clearing by whoever read it, and forgetting to would show the message again on the next visit.
 *
 * History state has the same staleness problem in one case — reload, and the browser replays the entry
 * including its state — so the entry is rewritten without it as soon as the message has been read. The
 * message survives in component state; the history entry no longer carries it.
 */
export function useFlash(): Flash {
  const location = useLocation()
  const navigate = useNavigate()

  const arrived = (location.state as FlashState | null)?.flash ?? null
  const [message, setMessage] = useState<string | null>(arrived)

  useEffect(() => {
    if (arrived === null) return
    // The path *and* the query string, or clearing the message would also clear the filters the user
    // arrived back to.
    navigate(`${location.pathname}${location.search}`, { replace: true, state: null })
  }, [arrived, location.pathname, location.search, navigate])

  return {
    message,
    show: setMessage,
    dismiss: () => setMessage(null),
  }
}
